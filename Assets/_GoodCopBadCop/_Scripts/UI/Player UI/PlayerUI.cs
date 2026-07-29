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

    private void Awake()
    {
        Instance = this;
    }
}
