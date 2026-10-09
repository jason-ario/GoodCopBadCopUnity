using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Screen-space emote wheel overlay. The wheel visuals (background, dividers, and one
/// <see cref="EmoteButton"/> per slot) are pre-placed in the scene/prefab hierarchy under this
/// object rather than generated at runtime. Each <see cref="EmoteButton"/> calls
/// <see cref="PlayEmote(string)"/> directly when clicked, which fires
/// <see cref="OnEmoteSelected"/> with the matching emote's index.
///
/// Lives on a hidden Canvas/Panel in the scene UI hierarchy (e.g. inside Player UI).
/// Discovered at runtime by <see cref="EmoteInputController"/> via the static Instance.
/// </summary>
public class EmoteWheelUI : MonoBehaviour
{
    public static EmoteWheelUI Instance { get; private set; }

    [Header("Emotes")]
    [Tooltip("Order does not need to match the pre-placed button layout — buttons look themselves " +
             "up by Name via PlayEmote(string).")]
    [SerializeField] private EmoteDefinition[] _emotes = new EmoteDefinition[]
    {
        new EmoteDefinition { Name = "Wave",          AnimTriggerName = "Waving",        Duration = 2.5f },
        new EmoteDefinition { Name = "Shrug",         AnimTriggerName = "Shrug",         Duration = 2.0f },
        new EmoteDefinition { Name = "Dance",         AnimTriggerName = "Dance",         Duration = 4.0f },
        new EmoteDefinition { Name = "Thumbs Up",     AnimTriggerName = "Thumbs Up",     Duration = 2.0f },
        new EmoteDefinition { Name = "Puke",          AnimTriggerName = "Puke",          Duration = 3.0f },
        new EmoteDefinition { Name = "Cough",         AnimTriggerName = "Cough",         Duration = 2.5f },
        new EmoteDefinition { Name = "Point",         AnimTriggerName = "Point",         Duration = 2.0f },
        new EmoteDefinition { Name = "Middle Finger", AnimTriggerName = "Middle Finger", Duration = 1.5f },
    };

    /// <summary>Fired with the selected emote index when the player clicks an emote button.</summary>
    public event Action<int> OnEmoteSelected;

    /// <summary>Read-only access to the emote definitions so <see cref="EmoteInputController"/> can look up data.</summary>
    public EmoteDefinition[] Emotes => _emotes;

    [Header("Gamepad")]
    [Tooltip("Minimum stick tilt (0-1) needed to aim at a slot.")]
    [SerializeField, Range(0.1f, 0.95f)] private float _stickDeadzone = 0.5f;

    private const float MouseMoveThresholdPixels = 4f;

    private EmoteButton[] _buttons = Array.Empty<EmoteButton>();
    private EmoteButton   _gamepadSlot;
    private Vector2       _lastMousePosition;

    // ─── Unity lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _buttons = GetComponentsInChildren<EmoteButton>(includeInactive: true);
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnEnable()
    {
        _gamepadSlot = null;
        _lastMousePosition = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    }

    /// <summary>
    /// Gamepad selection: either stick aims at the slot closest to its direction (the highlight
    /// stays after the stick is released), and A / RT plays the highlighted emote. Moving the
    /// mouse hands highlighting back to pointer hover.
    /// </summary>
    private void Update()
    {
        if (Mouse.current != null)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            if ((mousePos - _lastMousePosition).sqrMagnitude >= MouseMoveThresholdPixels * MouseMoveThresholdPixels)
            {
                _lastMousePosition = mousePos;
                SetGamepadSlot(null);
            }
        }

        Gamepad gp = Gamepad.current;
        if (gp == null) return;

        Vector2 left = gp.leftStick.ReadValue();
        Vector2 right = gp.rightStick.ReadValue();
        Vector2 aim = right.sqrMagnitude >= left.sqrMagnitude ? right : left;
        if (aim.sqrMagnitude >= _stickDeadzone * _stickDeadzone)
            SetGamepadSlot(FindSlotInDirection(aim));

        if (_gamepadSlot != null && (gp.buttonSouth.wasPressedThisFrame || gp.rightTrigger.wasPressedThisFrame))
            _gamepadSlot.GamepadPress();
    }

    private EmoteButton FindSlotInDirection(Vector2 aim)
    {
        EmoteButton best = null;
        float bestDelta = float.MaxValue;
        float aimAngle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;

        foreach (EmoteButton button in _buttons)
        {
            if (button == null || !button.isActiveAndEnabled) continue;
            if (!button.TryGetScreenDirection(out Vector2 dir)) continue;

            float delta = Mathf.Abs(Mathf.DeltaAngle(aimAngle, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg));
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = button;
            }
        }

        return best;
    }

    private void SetGamepadSlot(EmoteButton slot)
    {
        if (_gamepadSlot == slot) return;
        if (_gamepadSlot != null) _gamepadSlot.SetGamepadHighlight(false);
        _gamepadSlot = slot;
        if (_gamepadSlot != null) _gamepadSlot.SetGamepadHighlight(true);
    }

    // ─── Public API ─────────────────────────────────────────────────────────

    public void Show()
    {
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        _gamepadSlot = null;
        foreach (EmoteButton button in _buttons)
            if (button != null) button.Deselect();

        gameObject.SetActive(false);
    }

    /// <summary>
    /// Called by an <see cref="EmoteButton"/> when clicked. Looks up the emote by
    /// <see cref="EmoteDefinition.Name"/> and fires <see cref="OnEmoteSelected"/> with its index.
    /// </summary>
    public void PlayEmote(string emoteName)
    {
        int index = Array.FindIndex(_emotes, e => e.Name == emoteName);
        if (index < 0)
        {
            Debug.LogWarning($"[EmoteWheelUI] No emote found named '{emoteName}'.", this);
            return;
        }

        PlayEmote(index);
    }

    /// <summary>Fires <see cref="OnEmoteSelected"/> for the emote at <paramref name="index"/>.</summary>
    public void PlayEmote(int index)
    {
        if (index < 0 || index >= _emotes.Length) return;
        OnEmoteSelected?.Invoke(index);
    }
}
