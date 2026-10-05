using GoodCopBadCop.Input;
using GoodCopBadCop.UI;
using TMPro;
using UnityEngine;

/// <summary>
/// Bottom-right HUD prompt "[R] to reload" shown while the local player holds an
/// <see cref="IInventoryReloadable"/> weapon that isn't full (<see cref="IInventoryReloadable.NeedsReload"/>)
/// and the local <see cref="PlayerAmmoReserve"/> has ammo of that weapon's type, i.e. only when R would reload.
/// The row sits in the "Right HUD Stack" directly above the "[LMB] to use" row (<see cref="HeldItemUsePrompt"/>).
/// The icon is driven by a <see cref="HelperIconKeyDisplay"/> pinned to <see cref="GameAction.Reload"/>,
/// so it follows the live binding and swaps to the gamepad sprite with the active device.
///
/// Attach to the always-active "Player HUD" root (not the prompt row itself, which is toggled).
/// </summary>
public class HeldItemReloadPrompt : MonoBehaviour
{
    [Tooltip("Prompt row toggled while a non-full weapon is held (child of the Right HUD Stack).")]
    [SerializeField] private GameObject _root;

    [Tooltip("Key icon driven by HelperIconKeyDisplay (forced to GameAction.Reload).")]
    [SerializeField] private HelperIconKeyDisplay _keyDisplay;

    [Tooltip("Label next to the icon.")]
    [SerializeField] private TMP_Text _label;

    [SerializeField] private string _text = "to reload";

    private void Awake()
    {
        if (_keyDisplay != null) _keyDisplay.SetAction(GameAction.Reload);
    }

    private void OnEnable()
    {
        if (_label != null) _label.text = _text;
        SetVisible(false);
    }

    private void OnDisable()
    {
        SetVisible(false);
    }

    private void Update()
    {
        bool paused = UIController.Instance != null && UIController.Instance.IsPaused;
        SetVisible(!paused && HeldWeaponNeedsReload());
    }

    private static bool HeldWeaponNeedsReload()
    {
        // The held item is only known locally, so hide while mirroring a teammate's HUD.
        if (SpectateManager.IsHudMirroringTarget) return false;

        PlayerInstance player = PlayerInstance.Instance;
        PlayerPickupController pickup = player != null ? player.PlayerPickupController : null;
        if (pickup == null || !(pickup.HeldObject is IInventoryReloadable weapon) || !weapon.NeedsReload)
            return false;

        // Only prompt when R would actually do something: the reserve must hold this weapon's ammo type.
        PlayerAmmoReserve reserve = PlayerAmmoReserve.Local;
        return reserve != null && reserve.Get(weapon.ReserveAmmoType) > 0;
    }

    private void SetVisible(bool visible)
    {
        if (_root != null && _root.activeSelf != visible)
            _root.SetActive(visible);
    }
}
