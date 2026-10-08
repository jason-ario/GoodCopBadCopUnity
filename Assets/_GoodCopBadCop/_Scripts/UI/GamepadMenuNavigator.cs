using System.Collections.Generic;
using GoodCopBadCop.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Gamepad / keyboard focus for a list of menu buttons. The InputSystemUIInputModule drives
/// D-pad / left-stick navigation, Submit (A) and Cancel through Unity's EventSystem once a button
/// is selected; this component decides *when* a button is selected:
/// <list type="bullet">
///   <item>When the menu opens while a gamepad is the active device (<see cref="ActiveInputDeviceTracker"/>),
///   the last focused button (or the first one) is selected immediately.</item>
///   <item>With a mouse, nothing is highlighted until the player starts navigating (D-pad, left stick,
///   or arrow/WASD keys), so the menu doesn't look "pre-hovered".</item>
///   <item>If the selection is lost while a gamepad is active (a confirmation dialog closed, the selected
///   button was hidden or became non-interactable), focus is restored automatically.</item>
///   <item>Moving the mouse clears the selection so hover styling takes over again.</item>
/// </list>
/// While a <see cref="ConfirmationDialogController"/> outside this menu is open, the dialog owns focus.
///
/// Attach to any menu root that contains a list of buttons. Assign the ordered list of Buttons in the
/// Inspector; leave it empty to use every Button under this object in hierarchy order.
/// </summary>
public class GamepadMenuNavigator : MonoBehaviour
{
    [Tooltip("Ordered list of buttons to navigate. Only active, interactable entries are selectable. Empty = all child Buttons.")]
    [SerializeField] private List<Button> _buttons;

    [Tooltip("Minimum stick displacement (0-1) required to count as an intentional navigation move.")]
    [SerializeField] private float _stickDeadzone = 0.5f;

    private const float MouseMoveThresholdPixels = 4f;

    private Vector2 _lastMousePosition;
    private bool _hasMouseBaseline;
    private Button _lastFocused;
    private readonly List<Button> _childButtons = new List<Button>();

    private void OnEnable()
    {
        ActiveInputDeviceTracker.EnsureSubscribed();
        _hasMouseBaseline = false;
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

        // A confirmation dialog that isn't part of this menu owns focus while it's open.
        if (ConfirmationDialogController.IsAnyOpenOutside(transform)) return;

        GameObject selected = eventSystem.currentSelectedGameObject;
        if (IsUsableSelection(selected))
        {
            Button selectedButton = selected.GetComponent<Button>();
            if (selectedButton != null && Contains(selectedButton)) _lastFocused = selectedButton;
            return;
        }

        if (ActiveInputDeviceTracker.IsGamepad || NavigationInputDetected())
            SelectFirst();
    }

    private static bool IsUsableSelection(GameObject selected)
    {
        if (selected == null || !selected.activeInHierarchy) return false;
        Selectable selectable = selected.GetComponent<Selectable>();
        return selectable == null || selectable.IsInteractable();
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

    private IReadOnlyList<Button> Buttons
    {
        get
        {
            if (_buttons != null && _buttons.Count > 0) return _buttons;
            _childButtons.Clear();
            GetComponentsInChildren(false, _childButtons);
            return _childButtons;
        }
    }

    private bool Contains(Button button)
    {
        IReadOnlyList<Button> buttons = Buttons;
        for (int i = 0; i < buttons.Count; i++)
            if (buttons[i] == button) return true;
        return false;
    }

    private static bool IsSelectable(Button btn) =>
        btn != null && btn.gameObject.activeInHierarchy && btn.IsInteractable();

    /// <summary>
    /// Selects the last focused button if it's still usable, otherwise the first active,
    /// interactable button.
    /// </summary>
    public void SelectFirst()
    {
        if (EventSystem.current == null) return;

        if (IsSelectable(_lastFocused) && Contains(_lastFocused))
        {
            EventSystem.current.SetSelectedGameObject(_lastFocused.gameObject);
            return;
        }

        foreach (Button btn in Buttons)
        {
            if (IsSelectable(btn))
            {
                EventSystem.current.SetSelectedGameObject(btn.gameObject);
                return;
            }
        }
    }

    /// <summary>
    /// Refreshes the selection after the button list changes at runtime
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
