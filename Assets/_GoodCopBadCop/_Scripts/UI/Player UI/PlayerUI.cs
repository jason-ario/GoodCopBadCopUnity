using System;
using UnityEngine;

public class PlayerUI : MonoBehaviour
{
    public static PlayerUI Instance;
    
    [SerializeField] BatteryBar _batteryBar;
    public BatteryBar BatteryBar => _batteryBar;

    private void Awake()
    {
        Instance = this;
    }
}
