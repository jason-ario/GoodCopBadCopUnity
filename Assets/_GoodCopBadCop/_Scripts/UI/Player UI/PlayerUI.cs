using UnityEngine;

public class PlayerUI : MonoBehaviour
{
    public static PlayerUI Instance;

    [SerializeField] private BatteryBar _batteryBar;
    public BatteryBar BatteryBar => _batteryBar;

    [SerializeField] private HealthBar _healthBar;
    public HealthBar HealthBar => _healthBar;

    private void Awake()
    {
        Instance = this;
    }
}
