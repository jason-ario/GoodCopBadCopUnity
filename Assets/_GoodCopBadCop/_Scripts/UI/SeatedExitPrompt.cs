using GoodCopBadCop.Input;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace GoodCopBadCop.UI
{
    /// <summary>
    /// HUD prompt shown while the local player is seated in a <see cref="Chair"/>, telling them which
    /// button stands them up. Standing up is driven by the Jump input in
    /// <see cref="PlayerMovementController"/> (Input Manager "Jump" / gamepad buttonSouth), so the icon
    /// swaps between the keyboard and gamepad sprite via <see cref="ActiveInputDeviceTracker"/>.
    ///
    /// Put this on an always-active HUD object; it toggles <see cref="_root"/> on and off.
    /// </summary>
    public class SeatedExitPrompt : MonoBehaviour
    {
        [Tooltip("Visual root toggled while seated. Must be a child of (or separate from) this object so polling keeps running.")]
        [SerializeField] private GameObject _root;

        [Tooltip("Image that displays the jump key/button icon.")]
        [SerializeField] private Image _keyImage;

        [Tooltip("Optional label next to the icon.")]
        [SerializeField] private TextMeshProUGUI _label;

        [SerializeField] private string _text = "Stand Up";

        [Tooltip("Icon for the keyboard Jump input (Space).")]
        [SerializeField] private Sprite _keyboardSprite;

        [Tooltip("Icon for the gamepad Jump input (button south / Xbox A).")]
        [SerializeField] private Sprite _gamepadSprite;

        private PlayerMovementController _localMovement;

        private void OnEnable()
        {
            ActiveInputDeviceTracker.EnsureSubscribed();
            ActiveInputDeviceTracker.DeviceChanged += OnDeviceChanged;
            if (_label != null) _label.text = _text;
            RefreshIcon();
            SetVisible(false);
        }

        private void OnDisable()
        {
            ActiveInputDeviceTracker.DeviceChanged -= OnDeviceChanged;
        }

        private void OnDeviceChanged(bool isGamepad) => RefreshIcon();

        private void Update()
        {
            PlayerMovementController movement = ResolveLocalMovement();

            bool paused = UIController.Instance != null && UIController.Instance.IsPaused;
            bool visible = movement != null && movement.IsSitting && movement.CanSitOrStand && !paused;

            SetVisible(visible);
        }

        private PlayerMovementController ResolveLocalMovement()
        {
            if (_localMovement != null) return _localMovement;

            NetworkManager nm = NetworkManager.Singleton;
            NetworkObject playerObject = nm != null && nm.IsListening ? nm.LocalClient?.PlayerObject : null;
            if (playerObject != null)
                _localMovement = playerObject.GetComponent<PlayerMovementController>();

            return _localMovement;
        }

        private void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible)
                _root.SetActive(visible);
        }

        private void RefreshIcon()
        {
            if (_keyImage == null) return;

            Sprite icon = ActiveInputDeviceTracker.IsGamepad ? _gamepadSprite : _keyboardSprite;
            if (icon != null) _keyImage.sprite = icon;
        }
    }
}
