using GoodCopBadCop.Input;
using UnityEngine;
using UnityEngine.UI;

namespace GoodCopBadCop.UI
{
    /// <summary>
    /// Attach to a "Helper Icon" instance (or directly to its "Key and Button Icon" child) to keep
    /// the key/button prompt sprite in sync with the current <see cref="RebindableInput"/> binding
    /// for <see cref="action"/>, automatically swapping to the gamepad icon set while a gamepad is
    /// the active input device.
    /// </summary>
    public class HelperIconKeyDisplay : MonoBehaviour
    {
        [SerializeField] private GameAction action;
        [SerializeField] private InputIconDatabase database;
        [SerializeField] private Image targetImage;

        private const string KeyAndButtonIconChildName = "Key and Button Icon";

        private void Awake()
        {
            ResolveTargetImage();
        }

        private void OnEnable()
        {
            ResolveTargetImage();
            ActiveInputDeviceTracker.EnsureSubscribed();
            RebindableInput.BindingChanged += OnBindingChanged;
            ActiveInputDeviceTracker.DeviceChanged += OnDeviceChanged;
            Refresh();
        }

        private void OnDisable()
        {
            RebindableInput.BindingChanged -= OnBindingChanged;
            ActiveInputDeviceTracker.DeviceChanged -= OnDeviceChanged;
        }

        private void ResolveTargetImage()
        {
            if (targetImage != null) return;

            Transform child = transform.Find(KeyAndButtonIconChildName);
            targetImage = child != null ? child.GetComponent<Image>() : GetComponent<Image>();
        }

        /// <summary>Allows re-using the same prefab/component for a different action at runtime.</summary>
        public void SetAction(GameAction newAction)
        {
            action = newAction;
            Refresh();
        }

        private void OnBindingChanged(GameAction changedAction)
        {
            if (changedAction == action) Refresh();
        }

        private void OnDeviceChanged(bool isGamepad) => Refresh();

        public void Refresh()
        {
            if (targetImage == null || database == null) return;

            Sprite sprite = ActiveInputDeviceTracker.IsGamepad
                ? database.GetGamepadSprite(action)
                : database.GetKeyboardMouseSprite(action);

            if (sprite != null) targetImage.sprite = sprite;
        }
    }
}
