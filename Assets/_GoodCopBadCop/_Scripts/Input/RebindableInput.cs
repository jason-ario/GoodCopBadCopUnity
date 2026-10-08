using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace GoodCopBadCop.Input
{
    /// <summary>
    /// Gameplay actions that can be rebound from Settings -> Controls. Each action has an optional
    /// keyboard/mouse binding and an optional gamepad binding; both are persisted and rebindable.
    /// Values are serialized as ints on existing assets (e.g. <see cref="InputIconDatabase"/>), so
    /// only ever append new entries.
    /// </summary>
    public enum GameAction
    {
        Interact,
        Crouch,
        PlaceObject,
        ThrowObject,
        ToggleMask,
        OpenEmotes,
        // Appended (not inserted) so serialized GameAction ints on existing assets stay valid.
        ZoomHeldItem,
        Reload,
        // Not exposed in Settings -> Controls; registered so helper icons can show the Tab / View prompt.
        OpenGuidebook,
        // Held to transmit when Voice Input is set to Push To Talk (read by DissonanceVoiceChatAdapter).
        PushToTalk,
        // Gamepad-rebindable actions whose keyboard/mouse control is fixed (see FixedKeyboardLabels).
        UseItem,
        Jump,
        Sprint,
        NextSlot,
        PreviousSlot
    }

    /// <summary>
    /// Central, persisted bindings for <see cref="GameAction"/>. Gameplay code should query bindings
    /// through this class instead of hardcoding <see cref="KeyCode"/>, mouse button, or gamepad control
    /// literals so that rebinding (Settings -> Controls) and the helper-icon UI stay in sync.
    ///
    /// Gamepad bindings are stored as control paths relative to <see cref="Gamepad"/>
    /// (e.g. "buttonWest", "dpad/up", "rightStickPress"); an empty string means unbound.
    /// </summary>
    public static class RebindableInput
    {
        private const string PrefPrefix = "rebind.";

        /// <summary>Raised whenever a binding changes (from rebinding UI or ResetToDefault).</summary>
        public static event Action<GameAction> BindingChanged;

        private static readonly Dictionary<GameAction, KeyCode> DefaultKeys = new()
        {
            { GameAction.Interact, KeyCode.E },
            { GameAction.Crouch, KeyCode.LeftControl },
            { GameAction.ToggleMask, KeyCode.V },
            { GameAction.OpenEmotes, KeyCode.T },
            { GameAction.ZoomHeldItem, KeyCode.F },
            { GameAction.Reload, KeyCode.R },
            { GameAction.OpenGuidebook, KeyCode.Tab },
            { GameAction.PushToTalk, KeyCode.B },
        };

        private static readonly Dictionary<GameAction, int> DefaultMouseButtons = new()
        {
            { GameAction.PlaceObject, 1 }, // RMB
            { GameAction.ThrowObject, 2 }, // MMB
        };

        /// <summary>
        /// Keyboard/mouse labels for actions whose keyboard control is not rebindable (it is read
        /// through the legacy Input Manager or the mouse wheel). Shown read-only in Settings.
        /// </summary>
        private static readonly Dictionary<GameAction, string> FixedKeyboardLabels = new()
        {
            { GameAction.UseItem, "LMB" },
            { GameAction.Jump, "Space" },
            { GameAction.Sprint, "Left Shift" },
            { GameAction.NextSlot, "Scroll Up" },
            { GameAction.PreviousSlot, "Scroll Down" },
        };

        /// <summary>Default gamepad control path per action. Empty = unbound by default.</summary>
        private static readonly Dictionary<GameAction, string> DefaultGamepadControls = new()
        {
            { GameAction.Interact, "buttonWest" },
            { GameAction.Crouch, "rightStickPress" },
            { GameAction.PlaceObject, "leftTrigger" },
            { GameAction.ThrowObject, "rightShoulder" },
            { GameAction.ToggleMask, "" },
            { GameAction.OpenEmotes, "dpad/up" },
            { GameAction.ZoomHeldItem, "dpad/down" },
            { GameAction.Reload, "buttonNorth" },
            { GameAction.OpenGuidebook, "select" },
            { GameAction.PushToTalk, "" },
            { GameAction.UseItem, "rightTrigger" },
            { GameAction.Jump, "buttonSouth" },
            { GameAction.Sprint, "leftStickPress" },
            { GameAction.NextSlot, "dpad/right" },
            { GameAction.PreviousSlot, "dpad/left" },
        };

        /// <summary>
        /// Gamepad controls a player may bind. Start is reserved for Pause and the sticks' axes for
        /// movement/look, so they are not offered.
        /// </summary>
        public static readonly IReadOnlyList<string> BindableGamepadControls = new[]
        {
            "buttonSouth", "buttonEast", "buttonWest", "buttonNorth",
            "leftShoulder", "rightShoulder", "leftTrigger", "rightTrigger",
            "leftStickPress", "rightStickPress",
            "dpad/up", "dpad/down", "dpad/left", "dpad/right",
            "select",
        };

        private static readonly Dictionary<GameAction, KeyCode> Keys = new();
        private static readonly Dictionary<GameAction, int> MouseButtons = new();
        private static readonly Dictionary<GameAction, string> GamepadControls = new();
        private static bool _initialized;

        // Resolved ButtonControls for the current gamepad, rebuilt when the device or a binding changes.
        private static readonly Dictionary<GameAction, ButtonControl> ResolvedControls = new();
        private static Gamepad _resolvedFor;

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            foreach (KeyValuePair<GameAction, KeyCode> pair in DefaultKeys)
                Keys[pair.Key] = (KeyCode)PlayerPrefs.GetInt(PrefPrefix + pair.Key + ".key", (int)pair.Value);

            foreach (KeyValuePair<GameAction, int> pair in DefaultMouseButtons)
                MouseButtons[pair.Key] = PlayerPrefs.GetInt(PrefPrefix + pair.Key + ".mouse", pair.Value);

            foreach (KeyValuePair<GameAction, string> pair in DefaultGamepadControls)
            {
                string stored = PlayerPrefs.GetString(PrefPrefix + pair.Key + ".pad", pair.Value);
                GamepadControls[pair.Key] = IsBindableGamepadControl(stored) ? stored : pair.Value;
            }
        }

        // ── Keyboard / mouse ────────────────────────────────────────────────────────

        public static bool HasKeyBinding(GameAction action)
        {
            EnsureInitialized();
            return DefaultKeys.ContainsKey(action);
        }

        public static bool HasMouseBinding(GameAction action)
        {
            EnsureInitialized();
            return DefaultMouseButtons.ContainsKey(action);
        }

        /// <summary>True if the keyboard/mouse binding of this action can be changed in Settings.</summary>
        public static bool IsKeyboardRebindable(GameAction action) => HasKeyBinding(action) || HasMouseBinding(action);

        public static KeyCode GetKey(GameAction action)
        {
            EnsureInitialized();
            return Keys.TryGetValue(action, out KeyCode key) ? key : KeyCode.None;
        }

        public static int GetMouseButton(GameAction action)
        {
            EnsureInitialized();
            return MouseButtons.TryGetValue(action, out int button) ? button : -1;
        }

        public static void SetKey(GameAction action, KeyCode key)
        {
            EnsureInitialized();
            if (!DefaultKeys.ContainsKey(action)) return;
            Keys[action] = key;
            PlayerPrefs.SetInt(PrefPrefix + action + ".key", (int)key);
            PlayerPrefs.Save();
            BindingChanged?.Invoke(action);
        }

        public static void SetMouseButton(GameAction action, int button)
        {
            EnsureInitialized();
            if (!DefaultMouseButtons.ContainsKey(action)) return;
            MouseButtons[action] = button;
            PlayerPrefs.SetInt(PrefPrefix + action + ".mouse", button);
            PlayerPrefs.Save();
            BindingChanged?.Invoke(action);
        }

        // ── Gamepad ─────────────────────────────────────────────────────────────────

        /// <summary>True if this action has a gamepad slot (bound or not) that can be rebound in Settings.</summary>
        public static bool IsGamepadRebindable(GameAction action) => DefaultGamepadControls.ContainsKey(action);

        /// <summary>Current gamepad control path for the action, or empty if unbound.</summary>
        public static string GetGamepadBinding(GameAction action)
        {
            EnsureInitialized();
            return GamepadControls.TryGetValue(action, out string path) ? path : string.Empty;
        }

        public static bool HasGamepadBinding(GameAction action) => !string.IsNullOrEmpty(GetGamepadBinding(action));

        /// <summary>Kept for existing callers; returns the current (possibly rebound) gamepad control path.</summary>
        public static string GetGamepadButtonName(GameAction action) => GetGamepadBinding(action);

        public static bool IsBindableGamepadControl(string path)
        {
            if (string.IsNullOrEmpty(path)) return true; // unbound is valid
            foreach (string control in BindableGamepadControls)
                if (control == path) return true;
            return false;
        }

        /// <summary>
        /// Binds <paramref name="action"/> to a gamepad control. If another action already uses that
        /// control, the two bindings are swapped so no control is ever silently double-bound.
        /// Pass an empty string to unbind.
        /// </summary>
        public static void SetGamepadBinding(GameAction action, string controlPath)
        {
            EnsureInitialized();
            if (!DefaultGamepadControls.ContainsKey(action)) return;
            controlPath ??= string.Empty;
            if (!IsBindableGamepadControl(controlPath)) return;

            string previous = GetGamepadBinding(action);
            if (previous == controlPath) return;

            GameAction? swapped = null;
            if (!string.IsNullOrEmpty(controlPath))
            {
                foreach (KeyValuePair<GameAction, string> pair in GamepadControls)
                {
                    if (pair.Key != action && pair.Value == controlPath)
                    {
                        swapped = pair.Key;
                        break;
                    }
                }
            }

            WriteGamepadBinding(action, controlPath);
            if (swapped.HasValue) WriteGamepadBinding(swapped.Value, previous);
            PlayerPrefs.Save();

            BindingChanged?.Invoke(action);
            if (swapped.HasValue) BindingChanged?.Invoke(swapped.Value);
        }

        private static void WriteGamepadBinding(GameAction action, string controlPath)
        {
            GamepadControls[action] = controlPath;
            PlayerPrefs.SetString(PrefPrefix + action + ".pad", controlPath);
            _resolvedFor = null;
        }

        /// <summary>Resolved button on the current gamepad, or null if unbound / no gamepad.</summary>
        public static ButtonControl GetGamepadControl(GameAction action)
        {
            Gamepad gamepad = Gamepad.current;
            if (gamepad == null) return null;

            if (_resolvedFor != gamepad)
            {
                ResolvedControls.Clear();
                _resolvedFor = gamepad;
            }

            if (ResolvedControls.TryGetValue(action, out ButtonControl cached)) return cached;

            string path = GetGamepadBinding(action);
            ButtonControl control = string.IsNullOrEmpty(path) ? null : gamepad.TryGetChildControl<ButtonControl>(path);
            ResolvedControls[action] = control;
            return control;
        }

        public static bool GetGamepadDown(GameAction action) => GetGamepadControl(action)?.wasPressedThisFrame ?? false;
        public static bool GetGamepadHeld(GameAction action) => GetGamepadControl(action)?.isPressed ?? false;
        public static bool GetGamepadUp(GameAction action) => GetGamepadControl(action)?.wasReleasedThisFrame ?? false;

        /// <summary>
        /// Returns the first bindable gamepad control pressed this frame (for the Settings rebind UI).
        /// </summary>
        public static bool TryGetPressedGamepadControl(out string controlPath)
        {
            controlPath = null;
            Gamepad gamepad = Gamepad.current;
            if (gamepad == null) return false;

            foreach (string path in BindableGamepadControls)
            {
                ButtonControl control = gamepad.TryGetChildControl<ButtonControl>(path);
                if (control != null && control.wasPressedThisFrame)
                {
                    controlPath = path;
                    return true;
                }
            }

            return false;
        }

        // ── Reset ───────────────────────────────────────────────────────────────────

        /// <summary>Resets both the keyboard/mouse and the gamepad binding of an action.</summary>
        public static void ResetToDefault(GameAction action)
        {
            if (DefaultKeys.TryGetValue(action, out KeyCode key)) SetKey(action, key);
            if (DefaultMouseButtons.TryGetValue(action, out int button)) SetMouseButton(action, button);
            ResetGamepadToDefault(action);
        }

        public static void ResetGamepadToDefault(GameAction action)
        {
            EnsureInitialized();
            if (!DefaultGamepadControls.TryGetValue(action, out string path)) return;
            if (GetGamepadBinding(action) == path) return;

            WriteGamepadBinding(action, path);
            PlayerPrefs.Save();
            BindingChanged?.Invoke(action);
        }

        // ── Convenience queries used by gameplay call sites ─────────────────────────
        // Keyboard queries are suppressed while an in-game text field owns the keyboard (see TextInputFocus).
        public static bool GetKeyDown(GameAction action) => HasKeyBinding(action) && !TextInputFocus.IsCapturingKeyboard && UnityEngine.Input.GetKeyDown(GetKey(action));
        public static bool GetKeyHeld(GameAction action) => HasKeyBinding(action) && !TextInputFocus.IsCapturingKeyboard && UnityEngine.Input.GetKey(GetKey(action));
        public static bool GetKeyUp(GameAction action) => HasKeyBinding(action) && !TextInputFocus.IsCapturingKeyboard && UnityEngine.Input.GetKeyUp(GetKey(action));
        public static bool GetMouseButtonDown(GameAction action) => HasMouseBinding(action) && UnityEngine.Input.GetMouseButtonDown(GetMouseButton(action));
        public static bool GetMouseButtonHeld(GameAction action) => HasMouseBinding(action) && UnityEngine.Input.GetMouseButton(GetMouseButton(action));
        public static bool GetMouseButtonUp(GameAction action) => HasMouseBinding(action) && UnityEngine.Input.GetMouseButtonUp(GetMouseButton(action));

        // ── Display ─────────────────────────────────────────────────────────────────

        /// <summary>Human readable label for the keyboard/mouse control (e.g. "E", "Left Ctrl", "RMB").</summary>
        public static string GetDisplayName(GameAction action)
        {
            if (HasKeyBinding(action)) return KeyCodeToDisplayName(GetKey(action));
            if (HasMouseBinding(action)) return MouseButtonToDisplayName(GetMouseButton(action));
            if (FixedKeyboardLabels.TryGetValue(action, out string label)) return label;
            return "-";
        }

        /// <summary>Human readable label for the gamepad control (Xbox naming, e.g. "X", "LT", "D-Pad Up").</summary>
        public static string GetGamepadDisplayName(GameAction action) => GamepadControlToDisplayName(GetGamepadBinding(action));

        public static string GamepadControlToDisplayName(string controlPath)
        {
            switch (controlPath)
            {
                case "buttonSouth": return "A";
                case "buttonEast": return "B";
                case "buttonWest": return "X";
                case "buttonNorth": return "Y";
                case "leftShoulder": return "LB";
                case "rightShoulder": return "RB";
                case "leftTrigger": return "LT";
                case "rightTrigger": return "RT";
                case "leftStickPress": return "L3";
                case "rightStickPress": return "R3";
                case "dpad/up": return "D-Pad Up";
                case "dpad/down": return "D-Pad Down";
                case "dpad/left": return "D-Pad Left";
                case "dpad/right": return "D-Pad Right";
                case "select": return "View";
                case "start": return "Menu";
                case null:
                case "": return "-";
                default: return controlPath;
            }
        }

        public static string KeyCodeToDisplayName(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftControl: return "Left Ctrl";
                case KeyCode.RightControl: return "Right Ctrl";
                case KeyCode.LeftShift: return "Left Shift";
                case KeyCode.RightShift: return "Right Shift";
                case KeyCode.LeftAlt: return "Left Alt";
                case KeyCode.RightAlt: return "Right Alt";
                case KeyCode.Alpha0: case KeyCode.Alpha1: case KeyCode.Alpha2: case KeyCode.Alpha3:
                case KeyCode.Alpha4: case KeyCode.Alpha5: case KeyCode.Alpha6: case KeyCode.Alpha7:
                case KeyCode.Alpha8: case KeyCode.Alpha9:
                    return key.ToString().Replace("Alpha", string.Empty);
                // Extra mouse buttons can be bound to keyboard actions (e.g. side buttons for push-to-talk).
                case KeyCode.Mouse3: return "Mouse 4";
                case KeyCode.Mouse4: return "Mouse 5";
                case KeyCode.Mouse5: return "Mouse 6";
                case KeyCode.Mouse6: return "Mouse 7";
                default:
                    return key.ToString();
            }
        }

        public static string MouseButtonToDisplayName(int button)
        {
            switch (button)
            {
                case 0: return "LMB";
                case 1: return "RMB";
                case 2: return "MMB";
                default: return "Mouse " + button;
            }
        }
    }
}
