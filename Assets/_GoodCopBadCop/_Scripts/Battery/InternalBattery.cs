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

    private void Awake()
    {
        pickableObject = GetComponent<PickableObject>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        currentBatteryJuice.OnValueChanged += OnBatteryJuiceChanged;

        if (IsServer)
        {
            currentBatteryJuice.Value = maxBatteryCapacity;
        }
    }

    public override void OnNetworkDespawn()
    {
        currentBatteryJuice.OnValueChanged -= OnBatteryJuiceChanged;
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
    }

    private void OnBatteryJuiceChanged(float previousValue, float newValue)
    {
        if (newValue <= 0 && previousValue > 0)
        {
            OnBatteryDrained?.Invoke();
        }
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

    /// <summary>
    /// Recharges this battery. Callable from any peer: recharging is done by a player holding a
    /// Battery and interacting with equipment that is usually lying in the world (server-owned),
    /// so the caller is typically NOT the owner of this object.
    /// </summary>
    public void Recharge(float amount)
    {
        if (!IsSpawned) return;
        RechargeServerRpc(amount);
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void RechargeServerRpc(float amount)
    {
        if (amount <= 0f) return;
        currentBatteryJuice.Value = Mathf.Min(currentBatteryJuice.Value + amount, maxBatteryCapacity);
    }

    public float GetBatteryLevel()
    {
        return currentBatteryJuice.Value;
    }

    /// <summary>Server-only restore entry point used by the workday item snapshot.</summary>
    public void RestoreBatteryLevelServer(float value)
    {
        if (!IsServer) return;
        currentBatteryJuice.Value = Mathf.Clamp(value, 0f, maxBatteryCapacity);
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
