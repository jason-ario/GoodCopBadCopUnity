using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoodCopBadCop.Input
{
    /// <summary>
    /// Maps keyboard keys / mouse buttons / gamepad prompts (per <see cref="GameAction"/>) to the
    /// sprites used by helper-icon UI. Keyboard/mouse sprites should come from the "T-Dark" category
    /// of the Game Input Icons Pack (Keyboard_Mouse/Dark); gamepad sprites from the matching gamepad
    /// pack (e.g. XGamepad/Default).
    /// </summary>
    [CreateAssetMenu(fileName = "InputIconDatabase", menuName = "Good Cop Bad Cop/Input Icon Database")]
    public class InputIconDatabase : ScriptableObject
    {
        [Serializable]
        public struct KeyIcon
        {
            public KeyCode key;
            public Sprite sprite;
        }

        [Serializable]
        public struct MouseIcon
        {
            public int button;
            public Sprite sprite;
        }

        [Serializable]
        public struct GamepadIcon
        {
            public GameAction action;
            public Sprite sprite;
        }

        /// <summary>Sprite per physical gamepad control path (see <see cref="RebindableInput.BindableGamepadControls"/>).</summary>
        [Serializable]
        public struct GamepadControlIcon
        {
            public string controlPath;
            public Sprite sprite;
        }

        [SerializeField] private KeyIcon[] keyboardIcons;
        [SerializeField] private MouseIcon[] mouseIcons;
        [Tooltip("Legacy per-action gamepad icons. Used only when the action's bound control has no entry in Gamepad Control Icons.")]
        [SerializeField] private GamepadIcon[] gamepadIcons;
        [Tooltip("Gamepad icons keyed by control path (e.g. buttonWest, dpad/up). Follows gamepad rebinding.")]
        [SerializeField] private GamepadControlIcon[] gamepadControlIcons;
        [SerializeField] private Sprite fallbackSprite;

        private Dictionary<KeyCode, Sprite> _keyLookup;
        private Dictionary<int, Sprite> _mouseLookup;
        private Dictionary<GameAction, Sprite> _gamepadLookup;
        private Dictionary<string, Sprite> _gamepadControlLookup;

        private void EnsureLookups()
        {
            if (_keyLookup != null) return;

            _keyLookup = new Dictionary<KeyCode, Sprite>();
            if (keyboardIcons != null)
                foreach (KeyIcon entry in keyboardIcons)
                    _keyLookup[entry.key] = entry.sprite;

            _mouseLookup = new Dictionary<int, Sprite>();
            if (mouseIcons != null)
                foreach (MouseIcon entry in mouseIcons)
                    _mouseLookup[entry.button] = entry.sprite;

            _gamepadLookup = new Dictionary<GameAction, Sprite>();
            if (gamepadIcons != null)
                foreach (GamepadIcon entry in gamepadIcons)
                    _gamepadLookup[entry.action] = entry.sprite;

            _gamepadControlLookup = new Dictionary<string, Sprite>();
            if (gamepadControlIcons != null)
                foreach (GamepadControlIcon entry in gamepadControlIcons)
                    if (!string.IsNullOrEmpty(entry.controlPath))
                        _gamepadControlLookup[entry.controlPath] = entry.sprite;
        }

        private void OnValidate()
        {
            _keyLookup = null; // rebuild lookups after Inspector edits
        }

        public Sprite GetKeyboardMouseSprite(GameAction action)
        {
            EnsureLookups();

            if (RebindableInput.HasKeyBinding(action))
            {
                KeyCode key = RebindableInput.GetKey(action);
                if (_keyLookup.TryGetValue(key, out Sprite sprite) && sprite != null) return sprite;
            }

            if (RebindableInput.HasMouseBinding(action))
            {
                int button = RebindableInput.GetMouseButton(action);
                if (_mouseLookup.TryGetValue(button, out Sprite sprite) && sprite != null) return sprite;
            }

            return fallbackSprite;
        }

        /// <summary>
        /// Icon for the action's current (possibly rebound) gamepad control. Falls back to the legacy
        /// per-action entry only while the action still uses its default control, so a stale icon is
        /// never shown after a rebind.
        /// </summary>
        public Sprite GetGamepadSprite(GameAction action)
        {
            EnsureLookups();

            string path = RebindableInput.GetGamepadBinding(action);
            Sprite byControl = GetGamepadControlSprite(path);
            if (byControl != null) return byControl;

            if (!string.IsNullOrEmpty(path) && _gamepadLookup.TryGetValue(action, out Sprite sprite) && sprite != null)
                return sprite;

            return fallbackSprite;
        }

        /// <summary>Icon for a gamepad control path, or null if none is configured.</summary>
        public Sprite GetGamepadControlSprite(string controlPath)
        {
            EnsureLookups();
            if (string.IsNullOrEmpty(controlPath)) return null;
            return _gamepadControlLookup.TryGetValue(controlPath, out Sprite sprite) ? sprite : null;
        }
    }
}
