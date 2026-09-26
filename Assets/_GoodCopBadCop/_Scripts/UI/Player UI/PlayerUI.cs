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

    private PlayerPickupController _pickupController;
    private InternalBattery _currentBattery;

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        _batteryBar?.Hide();
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
        // Poll until PlayerInstance is available (it sets itself in OnNetworkSpawn).
        if (_pickupController == null)
        {
            TrySubscribeToPickupController();
            return;
        }

        // While an item with a battery is held, keep the fill amount live as it drains/recharges.
        if (_currentBattery != null)
        {
            _batteryBar?.UpdateBar(_currentBattery);
        }
    }

    private void OnDisable()
    {
        CheckpointIntegrityService.OnEnabledChanged -= OnCheckpointIntegrityEnabledChanged;

        if (_pickupController != null)
        {
            _pickupController.OnHeldObjectChanged -= OnHeldObjectChanged;
            _pickupController = null;
        }

        _currentBattery = null;
        _batteryBar?.Hide();
    }

    /// <summary>Shows or hides the radiation mask helper icon.</summary>
    public void SetMaskHelperIconVisible(bool visible)
    {
        if (_maskHelperIcon != null)
            _maskHelperIcon.SetActive(visible);
    }

    // ── Battery bar wiring ───────────────────────────────────────────────────

    private void TrySubscribeToPickupController()
    {
        if (PlayerInstance.Instance?.PlayerPickupController == null) return;

        _pickupController = PlayerInstance.Instance.PlayerPickupController;
        _pickupController.OnHeldObjectChanged += OnHeldObjectChanged;

        // Sync immediately in case an item is already held when this UI first activates.
        OnHeldObjectChanged(_pickupController.HeldObject);
    }

    private void OnHeldObjectChanged(PickableObject heldObject)
    {
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
