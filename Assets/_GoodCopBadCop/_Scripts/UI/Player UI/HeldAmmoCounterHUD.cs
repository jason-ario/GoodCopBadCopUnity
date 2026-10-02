using TMPro;
using UnityEngine;

/// <summary>
/// Right-side HUD label showing "current / max" for the held <see cref="IAmmoProvider"/> item
/// (Trash Bag junk vs. capacity, loaded rounds, fuel, pills, ...). Sits above the Ammo Reserve Panel
/// in the Right HUD Stack and hides when the held item has no countable resource.
///
/// Attach to the always-active "Player UI" root (not the panel itself, which is toggled).
/// </summary>
public class HeldAmmoCounterHUD : MonoBehaviour
{
    [Tooltip("Black background panel. Hidden when the held item isn't an IAmmoProvider.")]
    [SerializeField] private GameObject _panel;

    [Tooltip("White label showing \"current / max\".")]
    [SerializeField] private TMP_Text _label;

    private PickableObject _heldItem;
    private IAmmoProvider _provider;

    private void OnEnable()
    {
        Bind(null);
    }

    private void OnDisable()
    {
        Bind(null);
    }

    private void Update()
    {
        PickableObject held = ResolveHeldItem();
        if (held != _heldItem)
            Bind(held);
    }

    private static PickableObject ResolveHeldItem()
    {
        // The held item is only known locally, so hide while mirroring a teammate's HUD.
        if (SpectateManager.IsHudMirroringTarget) return null;

        PlayerInstance player = PlayerInstance.Instance;
        PlayerPickupController pickup = player != null ? player.PlayerPickupController : null;
        return pickup != null ? pickup.HeldObject : null;
    }

    private void Bind(PickableObject held)
    {
        if (_provider != null)
            _provider.OnAmmoChanged -= Refresh;

        _heldItem = held;
        _provider = held != null ? held as IAmmoProvider : null;

        if (_provider != null)
            _provider.OnAmmoChanged += Refresh;

        Refresh();
    }

    private void Refresh()
    {
        bool show = _provider != null && _heldItem != null;

        if (_panel != null && _panel.activeSelf != show)
            _panel.SetActive(show);

        if (!show || _label == null) return;

        int current = Mathf.CeilToInt(_provider.CurrentAmmo);
        int max     = Mathf.CeilToInt(_provider.MaxAmmo);
        _label.text = $"{current} / {max}";
    }
}
