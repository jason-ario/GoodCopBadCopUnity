using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoodCopBadCop.Input
{
    /// <summary>
    /// The small set of gameplay actions that support user rebinding from the Settings ->
    /// Controls tab. Gamepad bindings for these actions are intentionally fixed (see
    /// <see cref="RebindableInput.GamepadIconKey"/> and the relevant controller call sites) and are
    /// not exposed for rebinding here.
    /// </summary>
    public enum GameAction
    {
        Interact,
        Crouch,
        PlaceObject,
        ThrowObject,
        ToggleMask,
        OpenEmotes
    }

    /// <summary>
    /// Central, persisted keyboard/mouse bindings for <see cref="GameAction"/>. Gameplay code should
    /// query bindings through this class instead of hardcoding <see cref="KeyCode"/>/mouse button
    /// literals so that rebinding (Settings -> Controls) and the helper-icon UI stay in sync.
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
        };

        private static readonly Dictionary<GameAction, int> DefaultMouseButtons = new()
        {
            { GameAction.PlaceObject, 1 }, // RMB
            { GameAction.ThrowObject, 2 }, // MMB
        };

        /// <summary>Fixed (non-rebindable) gamepad button, used by gameplay code and the icon UI.</summary>
        public static readonly Dictionary<GameAction, string> GamepadButtonName = new()
        {
            { GameAction.Interact, "buttonWest" },
            { GameAction.Crouch, "buttonEast" },
            { GameAction.PlaceObject, "leftTrigger" },
            { GameAction.ThrowObject, "rightShoulder" },
            { GameAction.ToggleMask, "" },
            { GameAction.OpenEmotes, "dpad/up" },
        };

        private static readonly Dictionary<GameAction, KeyCode> Keys = new();
        private static readonly Dictionary<GameAction, int> MouseButtons = new();
        private static bool _initialized;

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            foreach (KeyValuePair<GameAction, KeyCode> pair in DefaultKeys)
                Keys[pair.Key] = (KeyCode)PlayerPrefs.GetInt(PrefPrefix + pair.Key + ".key", (int)pair.Value);

            foreach (KeyValuePair<GameAction, int> pair in DefaultMouseButtons)
                MouseButtons[pair.Key] = PlayerPrefs.GetInt(PrefPrefix + pair.Key + ".mouse", pair.Value);
        }

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

        public static string GetGamepadButtonName(GameAction action)
        {
            return GamepadButtonName.TryGetValue(action, out string name) ? name : string.Empty;
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

        public static void ResetToDefault(GameAction action)
        {
            if (DefaultKeys.TryGetValue(action, out KeyCode key)) SetKey(action, key);
            if (DefaultMouseButtons.TryGetValue(action, out int button)) SetMouseButton(action, button);
        }

        // ── Convenience queries used by gameplay call sites ─────────────────────────
        public static bool GetKeyDown(GameAction action) => HasKeyBinding(action) && UnityEngine.Input.GetKeyDown(GetKey(action));
        public static bool GetKeyHeld(GameAction action) => HasKeyBinding(action) && UnityEngine.Input.GetKey(GetKey(action));
        public static bool GetKeyUp(GameAction action) => HasKeyBinding(action) && UnityEngine.Input.GetKeyUp(GetKey(action));
        public static bool GetMouseButtonDown(GameAction action) => HasMouseBinding(action) && UnityEngine.Input.GetMouseButtonDown(GetMouseButton(action));
        public static bool GetMouseButtonHeld(GameAction action) => HasMouseBinding(action) && UnityEngine.Input.GetMouseButton(GetMouseButton(action));
        public static bool GetMouseButtonUp(GameAction action) => HasMouseBinding(action) && UnityEngine.Input.GetMouseButtonUp(GetMouseButton(action));

        /// <summary>Human readable label for the currently bound keyboard/mouse control (e.g. "E", "Left Ctrl", "RMB").</summary>
        public static string GetDisplayName(GameAction action)
        {
            if (HasKeyBinding(action)) return KeyCodeToDisplayName(GetKey(action));
            if (HasMouseBinding(action)) return MouseButtonToDisplayName(GetMouseButton(action));
            return "-";
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
