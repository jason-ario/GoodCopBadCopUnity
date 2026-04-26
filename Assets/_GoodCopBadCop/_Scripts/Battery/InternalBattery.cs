using System;
using UnityEngine;
using Unity.Netcode;

public class InternalBattery : NetworkBehaviour
{
    [SerializeField] private float maxBatteryCapacity = 1f;
    private NetworkVariable<float> currentBatteryJuice = new NetworkVariable<float>(1f);
    PickableObject pickableObject;

    void Start()
    {
        pickableObject = GetComponent<PickableObject>();
        currentBatteryJuice.Value = maxBatteryCapacity;
        pickableObject.OnEquip += PlayerUI.Instance.BatteryBar.Show;
        pickableObject.OnUnEquip += PlayerUI.Instance.BatteryBar.Hide;
        PlayerUI.Instance.BatteryBar.UpdateBar(this);
    }
    
    public void SetJuiceValue(float batteryJuiceValue)
    {
        if (IsOwner)
        {
            SetJuiceValueServerRpc(batteryJuiceValue);
        }
    }

    [Rpc(SendTo.Server)]
    private void SetJuiceValueServerRpc(float batteryJuiceValue)
    {
        currentBatteryJuice.Value = Mathf.Clamp(batteryJuiceValue, 0f, maxBatteryCapacity);
    }

    public void DrainBattery(float amount)
    {
        if (IsOwner)
        {
            DrainBatteryServerRpc(amount);
        }
    }

    [Rpc(SendTo.Server)]
    private void DrainBatteryServerRpc(float amount)
    {
        currentBatteryJuice.Value = Mathf.Max(currentBatteryJuice.Value - amount, 0f);
        PlayerUI.Instance.BatteryBar.UpdateBar(this);
    }

    public void Recharge(float amount)
    {
        if (IsOwner)
        {
            RechargeServerRpc(amount);
        }
    }

    [Rpc(SendTo.Server)]
    private void RechargeServerRpc(float amount)
    {
        currentBatteryJuice.Value = Mathf.Min(currentBatteryJuice.Value + amount, maxBatteryCapacity);
    }

    public float GetBatteryLevel()
    {
        return currentBatteryJuice.Value;
    }

    public float GetMaxCapacity()
    {
        return maxBatteryCapacity;
    }

    public float GetBatteryPercentage()
    {
        return Mathf.Clamp01(currentBatteryJuice.Value / maxBatteryCapacity);
    }

    public bool IsBatteryEmpty()
    {
        return currentBatteryJuice.Value <= 0;
    }
}
