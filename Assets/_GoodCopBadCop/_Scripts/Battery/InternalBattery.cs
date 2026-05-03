using System;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.Events;

public class InternalBattery : NetworkBehaviour
{
    [SerializeField] private float maxBatteryCapacity = 1f;
    private NetworkVariable<float> currentBatteryJuice = new NetworkVariable<float>(1f);
    PickableObject pickableObject;
    [SerializeField] private float batteryDrainRate = 0.01f;
    public UnityAction OnBatteryDrained;

    void Start()
    {
        pickableObject = GetComponent<PickableObject>();
        currentBatteryJuice.Value = maxBatteryCapacity;
        currentBatteryJuice.OnValueChanged += OnBatteryJuiceChanged;
        pickableObject.OnEquip += ShowBatteryBar;
        pickableObject.OnUnEquip += PlayerUI.Instance.BatteryBar.Hide;
    }

    public override void OnDestroy()
    {
        currentBatteryJuice.OnValueChanged -= OnBatteryJuiceChanged;
        base.OnDestroy();
    }

    private void OnBatteryJuiceChanged(float previousValue, float newValue)
    {
        if (PlayerUI.Instance != null && PlayerUI.Instance.BatteryBar.gameObject.activeSelf)
        {
            PlayerUI.Instance.BatteryBar.UpdateBar(this);
        }
    }

    void ShowBatteryBar()
    {
        PlayerUI.Instance.BatteryBar.UpdateBar(this); 
        PlayerUI.Instance.BatteryBar.Show();
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

    public void DrainBattery()
    {
        if (IsOwner)
        {
            DrainBatteryServerRpc(batteryDrainRate * Time.deltaTime);
        }
    }

    [Rpc(SendTo.Server)]
    private void DrainBatteryServerRpc(float amount)
    {
        currentBatteryJuice.Value = Mathf.Max(currentBatteryJuice.Value - amount, 0f);
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
