using GoodCopBadCop.Input;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-right HUD prompt "[LMB] to use" shown whenever the local player is holding an item —
/// LMB / RT is reserved for using the held item (see the input convention in IInteractable.cs).
/// The prompt row lives at the top of the "Right HUD Stack" vertical layout, so it sits above the
/// ammo panels and drops down to just above the inventory panel when those are hidden.
/// The icon swaps between the mouse and gamepad sprite via <see cref="ActiveInputDeviceTracker"/>.
///
/// Attach to the always-active "Player UI" root (not the prompt row itself, which is toggled).
/// </summary>
public class HeldItemUsePrompt : MonoBehaviour
{
    [Tooltip("Prompt row toggled while an item is held (child of the Right HUD Stack).")]
    [SerializeField] private GameObject _root;

    [Tooltip("Image that displays the use button icon.")]
    [SerializeField] private Image _keyImage;

    [Tooltip("Label next to the icon.")]
    [SerializeField] private TMP_Text _label;

    [SerializeField] private string _text = "to use";

    [Tooltip("Icon for left mouse button.")]
    [SerializeField] private Sprite _mouseSprite;

    [Tooltip("Icon for the gamepad use button (right trigger).")]
    [SerializeField] private Sprite _gamepadSprite;

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
        SetVisible(false);
    }

    private void OnDeviceChanged(bool isGamepad) => RefreshIcon();

    private void Update()
    {
        bool paused = UIController.Instance != null && UIController.Instance.IsPaused;
        SetVisible(!paused && ResolveHeldItem() != null);
    }

    private static PickableObject ResolveHeldItem()
    {
        // The held item is only known locally, so hide while mirroring a teammate's HUD.
        if (SpectateManager.IsHudMirroringTarget) return null;

        PlayerInstance player = PlayerInstance.Instance;
        PlayerPickupController pickup = player != null ? player.PlayerPickupController : null;
        return pickup != null ? pickup.HeldObject : null;
    }

    private void SetVisible(bool visible)
    {
        if (_root != null && _root.activeSelf != visible)
            _root.SetActive(visible);
    }

    private void RefreshIcon()
    {
        if (_keyImage == null) return;

        Sprite icon = ActiveInputDeviceTracker.IsGamepad ? _gamepadSprite : _mouseSprite;
        if (icon != null) _keyImage.sprite = icon;
    }
}
