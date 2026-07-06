using TMPro;
using UnityEngine;

/// <summary>
/// Drives the Ammo Counter HUD panel. Shows <see cref="_container"/> when the local player
/// equips any item implementing <see cref="IAmmoProvider"/> (Pistol, Flamethrower, etc.) and
/// hides it when nothing ammo-consuming is held.
///
/// Attach to the always-active "Player UI" root. Assign <see cref="_container"/> to the
/// "Ammo Counter" child panel and <see cref="_ammoText"/> to its "Text (TMP)" grandchild.
/// </summary>
public class AmmoCounter : MonoBehaviour
{
    [Tooltip("The panel to show/hide — the 'Ammo Counter' child GameObject.")]
    [SerializeField] private GameObject _container;

    [Tooltip("The TMP label inside the container that displays 'current/max'.")]
    [SerializeField] private TextMeshProUGUI _ammoText;

    private PlayerPickupController _pickupController;
    private IAmmoProvider _currentProvider;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        SetContainerActive(false);
        TrySubscribe();
    }

    private void Update()
    {
        // Poll until PlayerInstance is available (it sets itself in OnNetworkSpawn).
        if (_pickupController != null) return;
        TrySubscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    // ── Subscription ──────────────────────────────────────────────────────────

    private void TrySubscribe()
    {
        if (PlayerInstance.Instance?.PlayerPickupController == null) return;

        _pickupController = PlayerInstance.Instance.PlayerPickupController;
        _pickupController.OnHeldObjectChanged += OnHeldObjectChanged;

        // Sync immediately in case an item is already held when this UI first activates.
        OnHeldObjectChanged(_pickupController.HeldObject);
    }

    private void Unsubscribe()
    {
        if (_pickupController != null)
        {
            _pickupController.OnHeldObjectChanged -= OnHeldObjectChanged;
            _pickupController = null;
        }

        DetachFromProvider();
        SetContainerActive(false);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnHeldObjectChanged(PickableObject heldObject)
    {
        DetachFromProvider();

        if (heldObject is IAmmoProvider provider)
        {
            _currentProvider = provider;
            _currentProvider.OnAmmoChanged += RefreshDisplay;
            RefreshDisplay();
            SetContainerActive(true);
        }
        else
        {
            SetContainerActive(false);
        }
    }

    private void DetachFromProvider()
    {
        if (_currentProvider == null) return;
        _currentProvider.OnAmmoChanged -= RefreshDisplay;
        _currentProvider = null;
    }

    private void RefreshDisplay()
    {
        if (_ammoText == null || _currentProvider == null) return;
        int current = Mathf.CeilToInt(_currentProvider.CurrentAmmo);
        int max = Mathf.RoundToInt(_currentProvider.MaxAmmo);
        _ammoText.text = $"{current}/{max}";
    }

    private void SetContainerActive(bool active)
    {
        if (_container != null)
            _container.SetActive(active);
    }
}
