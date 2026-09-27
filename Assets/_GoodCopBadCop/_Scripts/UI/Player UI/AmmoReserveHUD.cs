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

    private PlayerAmmoReserve _reserve;

    private void OnEnable()
    {
        Bind(PlayerAmmoReserve.Local);
    }

    private void Update()
    {
        // Poll until the local player spawns, and rebind if it respawns as a new object.
        if (_reserve != PlayerAmmoReserve.Local)
            Bind(PlayerAmmoReserve.Local);
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
