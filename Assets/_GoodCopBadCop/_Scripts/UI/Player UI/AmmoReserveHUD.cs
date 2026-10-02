using GoodCopBadCop.Input;
using GoodCopBadCop.UI;
using UnityEngine;

/// <summary>
/// Bottom-right HUD panel listing the local player's <see cref="PlayerAmmoReserve"/>.
///
/// Only ammo types the player actually carries get a row; the panel's VerticalLayoutGroup +
/// ContentSizeFitter (bottom pivot, reversed arrangement) grow it upward from the bottom and fit
/// its height to the visible rows. The whole panel hides when the reserve is empty.
///
/// Attach to the always-active "Player HUD" object (not the panel itself, which is toggled).
/// </summary>
public class AmmoReserveHUD : MonoBehaviour
{
    [Tooltip("The black background panel containing the rows. Hidden when no ammo is carried.")]
    [SerializeField] private GameObject _panel;

    [Tooltip("One row per ammo type.")]
    [SerializeField] private AmmoReserveRowUI[] _rows;

    [Tooltip("Reload helper icon (child of the panel, above its top-right corner). Shown only while the " +
             "weapon in hand isn't full and the reserve has its ammo type.")]
    [SerializeField] private GameObject _reloadHint;

    private PlayerAmmoReserve _reserve;

    private void Awake()
    {
        // Pin the icon to Reload in code (same pattern as PlayerUI's zoom icon) so the prompt always
        // tracks the live binding and swaps to the gamepad Y sprite with the active device.
        if (_reloadHint != null && _reloadHint.TryGetComponent(out HelperIconKeyDisplay keyDisplay))
            keyDisplay.SetAction(GameAction.Reload);
    }

    private void OnEnable()
    {
        Bind(ResolveReserve());
    }

    private void Update()
    {
        // Poll until the local player spawns, rebind if it respawns as a new object, and follow
        // the watched teammate while spectating (reserve counts are replicated NetworkVariables).
        PlayerAmmoReserve target = ResolveReserve();
        if (_reserve != target)
            Bind(target);

        RefreshReloadHint();
    }

    private void RefreshReloadHint()
    {
        if (_reloadHint == null) return;

        bool show = ShouldShowReloadHint();
        if (_reloadHint.activeSelf != show)
            _reloadHint.SetActive(show);
    }

    private bool ShouldShowReloadHint()
    {
        // The held item is only known locally, so hide the hint while mirroring a teammate.
        if (_reserve == null || SpectateManager.IsHudMirroringTarget) return false;

        PlayerInstance player = PlayerInstance.Instance;
        PlayerPickupController pickup = player != null ? player.PlayerPickupController : null;
        if (pickup == null || !(pickup.HeldObject is IInventoryReloadable weapon)) return false;

        return weapon.NeedsReload && _reserve.Get(weapon.ReserveAmmoType) > 0;
    }

    private static PlayerAmmoReserve ResolveReserve()
    {
        if (!SpectateManager.IsHudMirroringTarget)
            return PlayerAmmoReserve.Local;

        PlayerInstance subject = SpectateManager.HudSubject;
        return subject != null ? subject.GetComponent<PlayerAmmoReserve>() : null;
    }

    private void OnDisable()
    {
        Bind(null);
    }

    private void Bind(PlayerAmmoReserve reserve)
    {
        if (_reserve != null)
        {
            _reserve.OnAmountChanged -= HandleAmountChanged;
            _reserve.OnReserveFull   -= HandleReserveFull;
        }

        _reserve = reserve;

        if (_reserve != null)
        {
            _reserve.OnAmountChanged += HandleAmountChanged;
            _reserve.OnReserveFull   += HandleReserveFull;
        }

        RefreshAll();
    }

    private void RefreshAll()
    {
        foreach (AmmoReserveRowUI row in _rows)
        {
            if (row == null) continue;
            if (_reserve != null)
                row.SetAmount(_reserve.Get(row.AmmoType), _reserve.GetMax(row.AmmoType));
            else
                row.SetAmount(0, 0);
        }

        RefreshPanelVisibility();
    }

    private void HandleAmountChanged(AmmoType type, int previous, int current)
    {
        AmmoReserveRowUI row = FindRow(type);
        if (row == null) return;

        row.SetAmount(current, _reserve.GetMax(type));
        RefreshPanelVisibility();

        if (current > previous)
            row.PlayGain();
    }

    private void HandleReserveFull(AmmoType type)
    {
        FindRow(type)?.PlayFull();
    }

    private void RefreshPanelVisibility()
    {
        if (_panel == null) return;

        bool anyVisible = false;
        foreach (AmmoReserveRowUI row in _rows)
        {
            if (row != null && row.gameObject.activeSelf)
            {
                anyVisible = true;
                break;
            }
        }

        if (_panel.activeSelf != anyVisible)
            _panel.SetActive(anyVisible);
    }

    private AmmoReserveRowUI FindRow(AmmoType type)
    {
        foreach (AmmoReserveRowUI row in _rows)
            if (row != null && row.AmmoType == type)
                return row;
        return null;
    }
}
