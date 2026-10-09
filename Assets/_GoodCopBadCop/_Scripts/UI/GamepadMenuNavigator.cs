using System.Collections.Generic;
using GoodCopBadCop.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Gamepad / keyboard focus for a menu. The InputSystemUIInputModule drives D-pad / left-stick
/// navigation, Submit (A) and Cancel through Unity's EventSystem once something is selected; this
/// component decides *when* and *what* is selected, and fills the gaps Unity's navigation leaves:
/// <list type="bullet">
///   <item>When the menu opens while a gamepad is the active device (<see cref="ActiveInputDeviceTracker"/>),
///   the last focused control (or the first one) is selected immediately.</item>
///   <item>With a mouse, nothing is highlighted until the player starts navigating (D-pad, left stick,
///   or arrow/WASD keys), so the menu doesn't look "pre-hovered".</item>
///   <item>If the selection is lost while a gamepad is active (a confirmation dialog closed, the selected
///   control was hidden or became non-interactable), focus is restored automatically.</item>
///   <item>Directional fallback: when a control's own Navigation can't reach another control of this menu
///   (mode None, a dead-end automatic search, or a target outside this menu such as a Back button),
///   the nearest control of this menu in the pressed direction is selected instead.</item>
///   <item>Moving the mouse clears the selection so hover styling takes over again.</item>
/// </list>
/// While any <see cref="ConfirmationDialogController"/> is open, the dialog owns focus.
///
/// Candidates = the ordered <see cref="_buttons"/> list (initial-focus priority) plus, when
/// <see cref="_includeChildSelectables"/> is on, every active interactable Selectable under this object
/// (excluding Back buttons, which gamepad B already triggers, scrollbars, and controls owned by a nested
/// navigator). Null list entries are ignored. Screens without a navigator in their prefab can add one at
/// runtime with <see cref="EnsureOn"/>.
/// </summary>
public class GamepadMenuNavigator : MonoBehaviour
{
    [Tooltip("Initial-focus priority order. Only active, interactable entries are used. Empty or all-null = child order.")]
    [SerializeField] private List<Button> _buttons;

    [Tooltip("Also navigate every active, interactable Selectable under this object (not just the list above).")]
    [SerializeField] private bool _includeChildSelectables = true;

    [Tooltip("Minimum stick displacement (0-1) required to count as an intentional navigation move.")]
    [SerializeField] private float _stickDeadzone = 0.5f;

    private const float MouseMoveThresholdPixels = 4f;
    private const float NavInitialRepeatDelay = 0.4f;
    private const float NavRepeatInterval = 0.12f;
    private const float MinDirectionalOffsetPixels = 1f;
    private const float PerpendicularWeight = 2f;
    private const float VisibleAlphaThreshold = 0.01f;

    // Several navigators can be active at once (nested screens, duplicate components); only one
    // fallback move may happen per frame.
    private static int _lastFallbackMoveFrame = -1;

    private Vector2 _lastMousePosition;
    private bool _hasMouseBaseline;
    private Selectable _lastFocused;
    private GameObject _selectedLastFrame;
    private Vector2Int _heldDirection;
    private float _nextRepeatTime;

    private readonly List<Selectable> _candidates = new List<Selectable>();
    private readonly List<Selectable> _childBuffer = new List<Selectable>();
    private readonly List<CanvasGroup> _canvasGroupBuffer = new List<CanvasGroup>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _lastFallbackMoveFrame = -1;

    // ─── Runtime setup ──────────────────────────────────────────────────────

    /// <summary>
    /// Adds a navigator to <paramref name="root"/> if it doesn't already have one, so screens that
    /// were authored without one still get gamepad focus. <paramref name="priority"/> sets the
    /// initial-focus order (null entries are skipped). Returns the existing or new navigator.
    /// </summary>
    public static GamepadMenuNavigator EnsureOn(GameObject root, bool includeChildSelectables = true, params Button[] priority)
    {
        if (root == null) return null;

        GamepadMenuNavigator existing = root.GetComponent<GamepadMenuNavigator>();
        if (existing != null) return existing;

        GamepadMenuNavigator added = root.AddComponent<GamepadMenuNavigator>();
        added._includeChildSelectables = includeChildSelectables;
        added._buttons = new List<Button>();
        if (priority != null)
            foreach (Button button in priority)
                if (button != null) added._buttons.Add(button);
        return added;
    }

    private void Awake()
    {
        // A duplicate navigator on the same object (e.g. a prefab-instance override whose list lost its
        // references) defers to a sibling with a valid list, or to the first one if none has a valid list.
        GamepadMenuNavigator[] siblings = GetComponents<GamepadMenuNavigator>();
        if (siblings.Length <= 1) return;

        GamepadMenuNavigator keeper = siblings[0];
        foreach (GamepadMenuNavigator sibling in siblings)
        {
            if (sibling.HasAnyListedButton())
            {
                keeper = sibling;
                break;
            }
        }
        if (keeper != this) enabled = false;
    }

    private bool HasAnyListedButton()
    {
        if (_buttons == null) return false;
        foreach (Button button in _buttons)
            if (button != null) return true;
        return false;
    }

    private void OnEnable()
    {
        ActiveInputDeviceTracker.EnsureSubscribed();
        _hasMouseBaseline = false;
        _heldDirection = Vector2Int.zero;
        _selectedLastFrame = null;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void OnDisable()
    {
        if (EventSystem.current == null) return;

        // Only drop the selection if it's ours, so a menu opened in the same frame keeps its focus.
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected == null || selected.transform.IsChildOf(transform))
            EventSystem.current.SetSelectedGameObject(null);
    }

    // ─── Per-frame focus maintenance ────────────────────────────────────────

    private void Update()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return;

        // If the mouse moves, defer to hover-based highlighting instead of a persistent selection.
        Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        if (!_hasMouseBaseline)
        {
            _lastMousePosition = mousePos;
            _hasMouseBaseline = true;
        }
        else if ((mousePos - _lastMousePosition).sqrMagnitude >= MouseMoveThresholdPixels * MouseMoveThresholdPixels)
        {
            _lastMousePosition = mousePos;
            GameObject current = eventSystem.currentSelectedGameObject;
            if (current != null && current.transform.IsChildOf(transform) && !IsEditingInputField(current))
                eventSystem.SetSelectedGameObject(null);
            return;
        }

        // An open confirmation dialog owns focus (it focuses its own Cancel button).
        if (ConfirmationDialogController.IsAnyOpen) return;

        RebuildCandidates();

        GameObject selected = eventSystem.currentSelectedGameObject;
        if (IsUsableSelection(selected))
        {
            Selectable selectable = selected.GetComponent<Selectable>();
            if (selectable != null && _candidates.Contains(selectable)) _lastFocused = selectable;
            return;
        }

        if (ActiveInputDeviceTracker.IsGamepad || NavigationInputDetected())
            SelectFirst();
    }

    /// <summary>
    /// Runs after the EventSystem has processed this frame's Move. If the pressed direction didn't
    /// lead to another control of this menu, picks the nearest one in that direction.
    /// </summary>
    private void LateUpdate()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return;

        Vector2Int step = ConsumeDirectionStep();
        GameObject current = eventSystem.currentSelectedGameObject;

        if (step != Vector2Int.zero
            && _lastFallbackMoveFrame != Time.frameCount
            && !ConfirmationDialogController.IsAnyOpen
            && !IsEditingInputField(_selectedLastFrame))
        {
            Selectable from = _selectedLastFrame != null ? _selectedLastFrame.GetComponent<Selectable>() : null;
            if (from != null && _candidates.Contains(from) && IsUsable(from))
            {
                Selectable unityTarget = GetNavigationTarget(from, step);
                bool unityHandlesIt = unityTarget != null && unityTarget != from && _candidates.Contains(unityTarget) && IsUsable(unityTarget);
                bool movedWithinMenu = current != _selectedLastFrame && IsCandidateObject(current);

                if (!unityHandlesIt && !movedWithinMenu)
                {
                    Selectable target = FindNearestInDirection(from, step);
                    if (target != null)
                    {
                        eventSystem.SetSelectedGameObject(target.gameObject);
                        _lastFallbackMoveFrame = Time.frameCount;
                    }
                    else if (current != _selectedLastFrame && !IsCandidateObject(current))
                    {
                        // Navigation escaped this menu (e.g. onto a hidden screen or a Back button): stay put.
                        eventSystem.SetSelectedGameObject(_selectedLastFrame);
                    }
                }
            }
        }

        _selectedLastFrame = eventSystem.currentSelectedGameObject;
    }

    // ─── Candidates ─────────────────────────────────────────────────────────

    private void RebuildCandidates()
    {
        _candidates.Clear();

        if (_buttons != null)
            foreach (Button button in _buttons)
                if (IsUsable(button) && !_candidates.Contains(button))
                    _candidates.Add(button);

        if (!_includeChildSelectables) return;

        _childBuffer.Clear();
        GetComponentsInChildren(false, _childBuffer);
        foreach (Selectable selectable in _childBuffer)
        {
            if (_candidates.Contains(selectable)) continue;
            if (selectable is Scrollbar) continue;
            if (IsBackButton(selectable)) continue;
            if (!IsOwnedByThisNavigator(selectable)) continue;
            if (!IsUsable(selectable)) continue;
            _candidates.Add(selectable);
        }
    }

    private bool IsOwnedByThisNavigator(Selectable selectable)
    {
        // The settings view drives its own focus model and releases EventSystem selection.
        if (selectable.GetComponentInParent<GoodCopBadCop.UI.SettingsMenu.SettingsRedesignPreviewController>() != null)
            return false;

        GamepadMenuNavigator owner = selectable.GetComponentInParent<GamepadMenuNavigator>();
        return owner == null || owner.gameObject == gameObject;
    }

    private static bool IsBackButton(Selectable selectable) =>
        selectable.GetComponent<GamepadBackButtonActivator>() != null
        || selectable.GetComponent<KeyBackButtonActivator>() != null;

    private bool IsCandidateObject(GameObject go)
    {
        if (go == null) return false;
        Selectable selectable = go.GetComponent<Selectable>();
        return selectable != null && _candidates.Contains(selectable);
    }

    private bool IsUsable(Selectable selectable) =>
        selectable != null
        && selectable.isActiveAndEnabled
        && selectable.IsInteractable()
        && IsVisible(selectable.transform);

    private bool IsUsableSelection(GameObject selected)
    {
        if (selected == null || !selected.activeInHierarchy) return false;
        Selectable selectable = selected.GetComponent<Selectable>();
        return selectable == null || (selectable.IsInteractable() && IsVisible(selectable.transform));
    }

    /// <summary>False if a parent CanvasGroup has faded the control out (screens hidden via alpha).</summary>
    private bool IsVisible(Transform target)
    {
        target.GetComponentsInParent(false, _canvasGroupBuffer);
        float alpha = 1f;
        foreach (CanvasGroup group in _canvasGroupBuffer)
        {
            if (!group.enabled) continue;
            alpha *= group.alpha;
            if (group.ignoreParentGroups) break;
        }
        return alpha > VisibleAlphaThreshold;
    }

    /// <summary>True if the given selection is an input field actively being edited (typing),
    /// which should keep focus even while the mouse moves elsewhere.</summary>
    private static bool IsEditingInputField(GameObject selected)
    {
        if (selected == null) return false;

        TMP_InputField tmpField = selected.GetComponent<TMP_InputField>();
        if (tmpField != null && tmpField.isFocused) return true;

        InputField uiField = selected.GetComponent<InputField>();
        if (uiField != null && uiField.isFocused) return true;

        return false;
    }

    // ─── Directional input ──────────────────────────────────────────────────

    private Vector2Int ReadDirection()
    {
        Vector2 value = Vector2.zero;

        Gamepad gp = Gamepad.current;
        if (gp != null)
        {
            Vector2 dpad = gp.dpad.ReadValue();
            Vector2 stick = gp.leftStick.ReadValue();
            value = dpad.sqrMagnitude > 0.01f ? dpad
                  : stick.sqrMagnitude >= _stickDeadzone * _stickDeadzone ? stick
                  : Vector2.zero;
        }

        Keyboard kb = Keyboard.current;
        if (value == Vector2.zero && kb != null && !TextInputFocus.IsCapturingKeyboard)
        {
            if (kb.upArrowKey.isPressed || kb.wKey.isPressed) value.y += 1f;
            if (kb.downArrowKey.isPressed || kb.sKey.isPressed) value.y -= 1f;
            if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) value.x += 1f;
            if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) value.x -= 1f;
        }

        if (value == Vector2.zero) return Vector2Int.zero;
        return Mathf.Abs(value.x) > Mathf.Abs(value.y)
            ? new Vector2Int(value.x > 0f ? 1 : -1, 0)
            : new Vector2Int(0, value.y > 0f ? 1 : -1);
    }

    /// <summary>Returns a direction on the press frame and on each repeat tick while held (unscaled time).</summary>
    private Vector2Int ConsumeDirectionStep()
    {
        Vector2Int direction = ReadDirection();
        if (direction == Vector2Int.zero)
        {
            _heldDirection = Vector2Int.zero;
            return Vector2Int.zero;
        }

        float now = Time.unscaledTime;
        if (direction != _heldDirection)
        {
            _heldDirection = direction;
            _nextRepeatTime = now + NavInitialRepeatDelay;
            return direction;
        }

        if (now < _nextRepeatTime) return Vector2Int.zero;
        _nextRepeatTime = now + NavRepeatInterval;
        return direction;
    }

    private bool NavigationInputDetected()
    {
        Gamepad gp = Gamepad.current;
        if (gp != null)
        {
            Vector2 stick = gp.leftStick.ReadValue();
            if (stick.sqrMagnitude >= _stickDeadzone * _stickDeadzone) return true;
            if (gp.dpad.up.wasPressedThisFrame || gp.dpad.down.wasPressedThisFrame ||
                gp.dpad.left.wasPressedThisFrame || gp.dpad.right.wasPressedThisFrame)
                return true;
        }

        Keyboard kb = Keyboard.current;
        if (kb != null && !TextInputFocus.IsCapturingKeyboard)
        {
            if (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame ||
                kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame ||
                kb.wKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame ||
                kb.sKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)
                return true;
        }

        return false;
    }

    private static Selectable GetNavigationTarget(Selectable from, Vector2Int direction)
    {
        if (direction.y > 0) return from.FindSelectableOnUp();
        if (direction.y < 0) return from.FindSelectableOnDown();
        if (direction.x > 0) return from.FindSelectableOnRight();
        return from.FindSelectableOnLeft();
    }

    private Selectable FindNearestInDirection(Selectable from, Vector2Int direction)
    {
        Vector2 origin = GetScreenCenter(from);
        Vector2 dir = direction;
        Selectable best = null;
        float bestScore = float.MaxValue;

        foreach (Selectable candidate in _candidates)
        {
            if (candidate == null || candidate == from) continue;

            Vector2 offset = GetScreenCenter(candidate) - origin;
            float along = Vector2.Dot(offset, dir);
            if (along < MinDirectionalOffsetPixels) continue;

            float perpendicular = Mathf.Abs(direction.x != 0 ? offset.y : offset.x);
            float score = along + perpendicular * PerpendicularWeight;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Screen-space centre of a UI control, for any canvas render mode.</summary>
    public static Vector2 GetScreenCenter(Selectable selectable)
    {
        Transform t = selectable.transform;
        RectTransform rect = t as RectTransform;
        Vector3 world = rect != null ? rect.TransformPoint(rect.rect.center) : t.position;

        Canvas canvas = selectable.GetComponentInParent<Canvas>();
        Canvas root = canvas != null ? canvas.rootCanvas : null;
        Camera cam = root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
        return RectTransformUtility.WorldToScreenPoint(cam, world);
    }

    // ─── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Selects the last focused control if it's still usable, otherwise the first usable entry of the
    /// priority list, otherwise the first usable child control.
    /// </summary>
    public void SelectFirst()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return;

        RebuildCandidates();

        Selectable target = _lastFocused != null && _candidates.Contains(_lastFocused) && IsUsable(_lastFocused)
            ? _lastFocused
            : _candidates.Count > 0 ? _candidates[0] : null;

        if (target != null)
        {
            eventSystem.SetSelectedGameObject(target.gameObject);
            _selectedLastFrame = target.gameObject;
        }
    }

    /// <summary>
    /// Refreshes the selection after the control list changes at runtime
    /// (e.g. the Continue button becomes visible after a save file is found), but only
    /// if a selection is already active — it won't force a highlight while the mouse is in control.
    /// </summary>
    public void RefreshSelection()
    {
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
        {
            _lastFocused = null;
            SelectFirst();
        }
    }
}
