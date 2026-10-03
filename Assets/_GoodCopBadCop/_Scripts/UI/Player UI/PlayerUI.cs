using UnityEngine;

public class PlayerUI : MonoBehaviour
{
    public static PlayerUI Instance;

    [SerializeField] private BatteryBar _batteryBar;
    public BatteryBar BatteryBar => _batteryBar;

    [SerializeField] private HealthBar _healthBar;
    public HealthBar HealthBar => _healthBar;

    [SerializeField] private GeigerCounterUI _geigerCounterUI;
    public GeigerCounterUI GeigerCounterUI => _geigerCounterUI;

    [SerializeField] private InventoryHUDController _inventoryHUD;
    public InventoryHUDController InventoryHUD => _inventoryHUD;

    [SerializeField] private CheckpointIntegrityBar _checkpointIntegrityBar;
    public CheckpointIntegrityBar CheckpointIntegrityBar => _checkpointIntegrityBar;

    [SerializeField] private CheckpointMaintenanceHUD _checkpointMaintenanceHUD;
    public CheckpointMaintenanceHUD CheckpointMaintenanceHUD => _checkpointMaintenanceHUD;

    [Tooltip("Helper icon shown while the local player is wearing the radiation mask.")]
    [SerializeField] private GameObject _maskHelperIcon;

    [Tooltip("Helper icon (zoom key) shown while the local player holds an item with a HeldItemZoomable.")]
    [SerializeField] private GameObject _zoomHelperIcon;

    [Tooltip("Helper icon (Tab / View) for opening the guidebook. Hidden while the guidebook is disabled.")]
    [SerializeField] private GameObject _guidebookHelperIcon;

    private PlayerPickupController _pickupController;
    private InternalBattery _currentBattery;

    private void Awake()
    {
        Instance = this;

        // The zoom icon is a duplicate of the emotes helper icon; make sure it always shows
        // the zoom binding regardless of what the duplicated component was serialized with.
        if (_zoomHelperIcon != null &&
            _zoomHelperIcon.TryGetComponent(out GoodCopBadCop.UI.HelperIconKeyDisplay zoomKeyDisplay))
            zoomKeyDisplay.SetAction(GoodCopBadCop.Input.GameAction.ZoomHeldItem);

        if (_guidebookHelperIcon != null &&
            _guidebookHelperIcon.TryGetComponent(out GoodCopBadCop.UI.HelperIconKeyDisplay guidebookKeyDisplay))
            guidebookKeyDisplay.SetAction(GoodCopBadCop.Input.GameAction.OpenGuidebook);
    }

    private void UpdateGuidebookHelperIcon()
    {
        if (_guidebookHelperIcon == null) return;
        bool visible = GameSettings.Instance == null || GameSettings.Instance.GuidebookEnabled;
        if (_guidebookHelperIcon.activeSelf != visible) _guidebookHelperIcon.SetActive(visible);
    }

    private void OnEnable()
    {
        _batteryBar?.Hide();
        SetZoomHelperIconVisible(false);
        SetMaskHelperIconVisible(IsLocalMaskEquipped());
        TrySubscribeToPickupController();

        CheckpointIntegrityService.OnEnabledChanged += OnCheckpointIntegrityEnabledChanged;
        OnCheckpointIntegrityEnabledChanged(CheckpointIntegrityService.IsEnabled);
    }

    /// <summary>
    /// Keeps the integrity bar's visibility in step with the service — covers Day 2+ starts
    /// where the day is applied before (or after) this HUD initializes.
    /// </summary>
    private void OnCheckpointIntegrityEnabledChanged(bool enabled)
    {
        if (_checkpointIntegrityBar == null) return;

        if (enabled) _checkpointIntegrityBar.Show();
        else _checkpointIntegrityBar.Hide();
    }

    private void Update()
    {
        UpdateGuidebookHelperIcon();

        // While spectating, mirror the watched teammate. Their HeldObject/pickup events are
        // owner-only, so poll the replicated ProxyHeldObject and mask state instead.
        if (SpectateManager.IsHudMirroringTarget)
        {
            UpdateMirroredTarget(SpectateManager.HudSubject);
            return;
        }

        if (_mirroredTarget != null || _wasMirroring)
            EndMirroring();

        // The HUD can activate after the local player spawned (missing the equipment controller's
        // initial push), so keep the mask icon in step with the replicated mask state every frame.
        SetMaskHelperIconVisible(IsLocalMaskEquipped());

        // Poll until PlayerInstance is available (it sets itself in OnNetworkSpawn), and rebind
        // if the local player respawned as a new object.
        PlayerPickupController localPickup = PlayerInstance.Instance != null ? PlayerInstance.Instance.PlayerPickupController : null;
        if (_pickupController == null || _pickupController != localPickup)
        {
            UnsubscribeFromPickupController();
            TrySubscribeToPickupController();
            return;
        }

        // While an item with a battery is held, keep the fill amount live as it drains/recharges.
        if (_currentBattery != null)
        {
            _batteryBar?.UpdateBar(_currentBattery);
        }
    }

    // ── Spectator mirroring ──────────────────────────────────────────────────

    private PlayerInstance _mirroredTarget;
    private bool _wasMirroring;
    private PickableObject _mirroredHeld;

    private void UpdateMirroredTarget(PlayerInstance target)
    {
        if (!_wasMirroring)
        {
            // Local pickup events must not overwrite the mirrored battery bar while spectating.
            UnsubscribeFromPickupController();
            _wasMirroring = true;
        }

        if (target != _mirroredTarget)
        {
            _mirroredTarget = target;
            _mirroredHeld = null;
            OnHeldObjectChanged(null);
        }

        PlayerPickupController pickup = target != null ? target.PlayerPickupController : null;
        PickableObject held = pickup != null ? pickup.ProxyHeldObject : null;
        if (held != _mirroredHeld)
        {
            _mirroredHeld = held;
            OnHeldObjectChanged(held);
        }
        else if (_currentBattery != null)
        {
            _batteryBar?.UpdateBar(_currentBattery);
        }

        PlayerEquipmentController equipment = target != null ? target.GetComponent<PlayerEquipmentController>() : null;
        SetMaskHelperIconVisible(equipment != null && equipment.IsMaskEquipped);
    }

    private void EndMirroring()
    {
        _mirroredTarget = null;
        _mirroredHeld = null;
        _wasMirroring = false;
        OnHeldObjectChanged(null);

        // Restore the local player's own mask icon state.
        SetMaskHelperIconVisible(IsLocalMaskEquipped());
    }

    private PlayerInstance _equipmentOwner;
    private PlayerEquipmentController _localEquipment;

    private bool IsLocalMaskEquipped()
    {
        PlayerInstance local = PlayerInstance.Instance;
        if (local != _equipmentOwner)
        {
            _equipmentOwner = local;
            _localEquipment = local != null ? local.GetComponent<PlayerEquipmentController>() : null;
        }
        return _localEquipment != null && _localEquipment.IsMaskEquipped;
    }

    private void UnsubscribeFromPickupController()
    {
        if (_pickupController == null) return;

        _pickupController.OnHeldObjectChanged -= OnHeldObjectChanged;
        _pickupController.OnHeldItemStowed -= OnHeldItemStowed;
        _pickupController = null;
    }

    private void OnDisable()
    {
        CheckpointIntegrityService.OnEnabledChanged -= OnCheckpointIntegrityEnabledChanged;

        UnsubscribeFromPickupController();
        _mirroredTarget = null;
        _mirroredHeld = null;
        _wasMirroring = false;

        _currentBattery = null;
        _batteryBar?.Hide();
        SetZoomHelperIconVisible(false);
    }

    /// <summary>Shows or hides the radiation mask helper icon.</summary>
    public void SetMaskHelperIconVisible(bool visible)
    {
        if (_maskHelperIcon != null && _maskHelperIcon.activeSelf != visible)
            _maskHelperIcon.SetActive(visible);
    }

    /// <summary>Shows or hides the zoom-mode helper icon.</summary>
    public void SetZoomHelperIconVisible(bool visible)
    {
        if (_zoomHelperIcon != null && _zoomHelperIcon.activeSelf != visible)
            _zoomHelperIcon.SetActive(visible);
    }

    // ── Battery bar wiring ───────────────────────────────────────────────────

    private void TrySubscribeToPickupController()
    {
        if (PlayerInstance.Instance?.PlayerPickupController == null) return;

        _pickupController = PlayerInstance.Instance.PlayerPickupController;
        _pickupController.OnHeldObjectChanged += OnHeldObjectChanged;
        _pickupController.OnHeldItemStowed += OnHeldItemStowed;

        // Sync immediately in case an item is already held when this UI first activates.
        OnHeldObjectChanged(_pickupController.HeldObject);
    }

    // Stowing empties the hand without firing OnHeldObjectChanged, so treat it as "nothing held".
    private void OnHeldItemStowed(PickableObject stowed) => OnHeldObjectChanged(null);

    private void OnHeldObjectChanged(PickableObject heldObject)
    {
        // Zoom is local-only, so never advertise it while mirroring a spectated teammate.
        SetZoomHelperIconVisible(!_wasMirroring && heldObject != null && heldObject.GetComponent<HeldItemZoomable>() != null);

        _currentBattery = heldObject != null ? heldObject.GetComponent<InternalBattery>() : null;

        if (_currentBattery != null)
        {
            _batteryBar?.UpdateBar(_currentBattery);
            _batteryBar?.Show();
        }
        else
        {
            _batteryBar?.Hide();
        }
    }
}
