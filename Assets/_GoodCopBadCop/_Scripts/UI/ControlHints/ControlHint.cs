using System;
using GoodCopBadCop.Input;
using UnityEngine;

/// <summary>Which input device a <see cref="ControlHint"/> row is shown for.</summary>
public enum ControlHintDevice
{
    Both,
    KeyboardMouseOnly,
    GamepadOnly,
}

/// <summary>
/// One "{prefix} [icon] {text}" row shown by <see cref="ControlHintsOverlay"/>, e.g. "[LMB] to check / uncheck"
/// or "Hold [LMB] to rotate". Serializable so systems can author hints in the Inspector.
///
/// Icon resolution (per active device, first match wins):
///  1. The explicit sprite (<see cref="keyboardMouseSprite"/> / <see cref="gamepadSprite"/>).
///  2. <see cref="action"/> when <see cref="useAction"/> is set: rebind-aware lookup in <see cref="InputIconDatabase"/>.
///  3. The fixed control: <see cref="mouseButton"/> / <see cref="key"/> for keyboard/mouse,
///     <see cref="gamepadControlPath"/> for gamepad (database lookup by path, e.g. "buttonSouth", "dpad/vertical").
/// Use 2 for inputs that go through <see cref="RebindableInput"/>, 3 for hard-coded inputs.
/// </summary>
[Serializable]
public class ControlHint
{
    [Tooltip("Label after the icon, e.g. \"to rotate\".")]
    public string text;

    [Tooltip("Optional label used instead of Text while a gamepad is active.")]
    public string gamepadText;

    [Tooltip("Optional word(s) before the icon, e.g. \"Hold\".")]
    public string prefix;

    [Tooltip("Optional prefix used instead of Prefix while a gamepad is active.")]
    public string gamepadPrefix;

    [Tooltip("Which input device this row is shown for.")]
    public ControlHintDevice showFor = ControlHintDevice.Both;

    [Header("Icon")]
    [Tooltip("Resolve the icon from a rebindable GameAction (follows rebinding on both devices).")]
    public bool useAction;

    public GameAction action;

    [Tooltip("Fixed mouse button for keyboard/mouse (0 = left, 1 = right, 2 = middle). -1 = none.")]
    public int mouseButton = -1;

    [Tooltip("Fixed keyboard key for keyboard/mouse. None = unused.")]
    public KeyCode key = KeyCode.None;

    [Tooltip("Fixed gamepad control path looked up in the icon database, e.g. \"buttonSouth\". Empty = unused.")]
    public string gamepadControlPath;

    [Tooltip("Explicit keyboard/mouse icon. Overrides every lookup.")]
    public Sprite keyboardMouseSprite;

    [Tooltip("Explicit gamepad icon. Overrides every lookup.")]
    public Sprite gamepadSprite;

    public bool IsShownFor(bool gamepad) =>
        showFor == ControlHintDevice.Both
        || (showFor == ControlHintDevice.GamepadOnly && gamepad)
        || (showFor == ControlHintDevice.KeyboardMouseOnly && !gamepad);

    public string TextFor(bool gamepad) => gamepad && !string.IsNullOrEmpty(gamepadText) ? gamepadText : text;

    public string PrefixFor(bool gamepad) => gamepad && !string.IsNullOrEmpty(gamepadPrefix) ? gamepadPrefix : prefix;

    public Sprite ResolveIcon(bool gamepad, InputIconDatabase database)
    {
        Sprite explicitSprite = gamepad ? gamepadSprite : keyboardMouseSprite;
        if (explicitSprite != null) return explicitSprite;
        if (database == null) return null;

        if (useAction)
            return gamepad ? database.GetGamepadSprite(action) : database.GetKeyboardMouseSprite(action);

        if (gamepad)
            return database.GetGamepadControlSprite(gamepadControlPath);

        if (mouseButton >= 0) return database.GetMouseButtonSprite(mouseButton);
        if (key != KeyCode.None) return database.GetKeySprite(key);
        return null;
    }
}
