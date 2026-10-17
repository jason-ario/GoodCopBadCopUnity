using GoodCopBadCop.Input;
using UnityEngine;
using UnityEngine.UI;

namespace GoodCopBadCop.UI
{
    /// <summary>
    /// Key prompt on the shared Back button. Shows the keyboard key (Escape, handled by
    /// <see cref="KeyBackButtonActivator"/>) while keyboard/mouse is active and the gamepad East
    /// button (handled by <see cref="GamepadBackButtonActivator"/>) while a controller is active,
    /// following <see cref="ActiveInputDeviceTracker"/> like the interaction tooltip.
    /// Back is not a <see cref="GameAction"/>, so the keyboard sprite is serialized here; the
    /// gamepad sprite comes from <see cref="InputIconDatabase"/> by control path when available.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class BackButtonKeyIcon : MonoBehaviour
    {
        [SerializeField] private Image _targetImage;

        [Tooltip("Optional. Used to look up the gamepad icon for Gamepad Control Path.")]
        [SerializeField] private InputIconDatabase _iconDatabase;

        [Tooltip("Icon while keyboard/mouse is active (matches KeyBackButtonActivator's key).")]
        [SerializeField] private Sprite _keyboardSprite;

        [Tooltip("Fallback icon while a controller is active, if the database has no entry.")]
        [SerializeField] private Sprite _gamepadSprite;

        [Tooltip("Gamepad control used for Back (matches GamepadBackButtonActivator).")]
        [SerializeField] private string _gamepadControlPath = "buttonEast";

        private void Awake()
        {
            if (_targetImage == null) _targetImage = GetComponent<Image>();
        }

        private void OnEnable()
        {
            ActiveInputDeviceTracker.EnsureSubscribed();
            ActiveInputDeviceTracker.DeviceChanged += OnDeviceChanged;
            Refresh();
        }

        private void OnDisable()
        {
            ActiveInputDeviceTracker.DeviceChanged -= OnDeviceChanged;
        }

        private void OnDeviceChanged(bool isGamepad) => Refresh();

        public void Refresh()
        {
            if (_targetImage == null) return;

            Sprite sprite;
            if (ActiveInputDeviceTracker.IsGamepad)
            {
                sprite = _iconDatabase != null ? _iconDatabase.GetGamepadControlSprite(_gamepadControlPath) : null;
                if (sprite == null) sprite = _gamepadSprite;
            }
            else
            {
                sprite = _keyboardSprite;
            }

            if (sprite != null) _targetImage.sprite = sprite;
        }
    }
}
