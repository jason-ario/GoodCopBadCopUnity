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

        [SerializeField] private KeyIcon[] keyboardIcons;
        [SerializeField] private MouseIcon[] mouseIcons;
        [SerializeField] private GamepadIcon[] gamepadIcons;
        [SerializeField] private Sprite fallbackSprite;

        private Dictionary<KeyCode, Sprite> _keyLookup;
        private Dictionary<int, Sprite> _mouseLookup;
        private Dictionary<GameAction, Sprite> _gamepadLookup;

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

        public Sprite GetGamepadSprite(GameAction action)
        {
            EnsureLookups();
            return _gamepadLookup.TryGetValue(action, out Sprite sprite) && sprite != null ? sprite : fallbackSprite;
        }
    }
}
