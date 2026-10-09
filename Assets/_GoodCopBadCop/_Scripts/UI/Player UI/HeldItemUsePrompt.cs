using GoodCopBadCop.Input;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-right HUD prompt "[LMB] to {verb}" naming what LMB / RT does with the held item right now —
/// LMB / RT is reserved for using the held item (see the input convention in IInteractable.cs).
/// The verb is the held item's tool action on the targeted object when there is one
/// (<see cref="PlayerInteractionController.TargetItemUseVerb"/>, e.g. "Stamp"), otherwise its own
/// in-place use (<see cref="PickableObject.GetHeldUseVerb"/>, e.g. "Take photo"). Hidden when the
/// item has no LMB action (e.g. a supply box).
/// The row lives in the "Held Item Controls" stack (<see cref="HeldItemControlsPrompt"/>) inside the
/// "Right HUD Stack", below "[F] to inspect" and above the reload / place / throw rows.
/// The icon swaps between the mouse and gamepad sprite via <see cref="ActiveInputDeviceTracker"/>.
///
/// Attach to the always-active "Player UI" root (not the prompt row itself, which is toggled).
/// </summary>
public class HeldItemUsePrompt : MonoBehaviour
{
    [Tooltip("Prompt row toggled while the held item has an LMB action (child of the Right HUD Stack).")]
    [SerializeField] private GameObject _root;

    [Tooltip("Image that displays the use button icon.")]
    [SerializeField] private Image _keyImage;

    [Tooltip("Label next to the icon: the current action verb.")]
    [SerializeField] private TMP_Text _label;

    [Tooltip("Icon for left mouse button.")]
    [SerializeField] private Sprite _mouseSprite;

    [Tooltip("Icon for the gamepad use button (right trigger). Fallback when no icon database is assigned.")]
    [SerializeField] private Sprite _gamepadSprite;

    [Tooltip("When assigned, the gamepad icon follows the rebindable Use Item binding.")]
    [SerializeField] private InputIconDatabase _iconDatabase;

    private void OnEnable()
    {
        ActiveInputDeviceTracker.EnsureSubscribed();
        ActiveInputDeviceTracker.DeviceChanged += OnDeviceChanged;
        RebindableInput.BindingChanged += OnBindingChanged;
        RefreshIcon();
        SetVisible(false);
    }

    private void OnDisable()
    {
        ActiveInputDeviceTracker.DeviceChanged -= OnDeviceChanged;
        RebindableInput.BindingChanged -= OnBindingChanged;
        SetVisible(false);
    }

    private void OnDeviceChanged(bool isGamepad) => RefreshIcon();

    private void OnBindingChanged(GameAction action)
    {
        if (action == GameAction.UseItem) RefreshIcon();
    }

    private void Update()
    {
        bool paused = UIController.Instance != null && UIController.Instance.IsPaused;
        string verb = paused ? null : ResolveVerb();
        bool visible = !string.IsNullOrEmpty(verb);

        if (visible && _label != null)
        {
            string text = FormatLabel(verb);
            if (_label.text != text) _label.text = text;
        }
        SetVisible(visible);
    }

    /// <summary>"Take photo" → "to take photo". Only the first letter is lowered so acronyms ("UV") survive.</summary>
    private static string FormatLabel(string verb) =>
        "to " + char.ToLowerInvariant(verb[0]) + verb.Substring(1);

    private static string ResolveVerb()
    {
        // The held item is only known locally, so hide while mirroring a teammate's HUD.
        if (SpectateManager.IsHudMirroringTarget) return null;

        PlayerInstance player = PlayerInstance.Instance;
        PlayerPickupController pickup = player != null ? player.PlayerPickupController : null;
        PickableObject held = pickup != null ? pickup.HeldObject : null;
        if (held == null) return null;

        PlayerInteractionController interaction = player.PlayerInteractionController;
        string targetVerb = interaction != null ? interaction.TargetItemUseVerb : null;
        return !string.IsNullOrEmpty(targetVerb) ? targetVerb : held.GetHeldUseVerb();
    }

    private void SetVisible(bool visible)
    {
        if (_root != null && _root.activeSelf != visible)
            _root.SetActive(visible);
    }

    private void RefreshIcon()
    {
        if (_keyImage == null) return;

        Sprite icon = _mouseSprite;
        if (ActiveInputDeviceTracker.IsGamepad)
        {
            Sprite bound = _iconDatabase != null
                ? _iconDatabase.GetGamepadControlSprite(RebindableInput.GetGamepadBinding(GameAction.UseItem))
                : null;
            icon = bound != null ? bound : _gamepadSprite;
        }
        if (icon != null) _keyImage.sprite = icon;
    }
}
