using Unity.Netcode;
using Unity.Netcode.Editor;
using UnityEngine;

public class Battery : PickableObject
{
    [SerializeField] private float batteryCapacity = 1f; // Full battery = 1
    
    public float GetBatteryCapacity()
    {
        return batteryCapacity;
    }
    
    public void UseBatteryOnEquipment(PickableObject itemToUseOn)
    {
        InternalBattery internalBattery = itemToUseOn.GetComponent<InternalBattery>();
    
        if (internalBattery != null)
        {
            float batteryCapacity = GetBatteryCapacity();
            internalBattery.Recharge(batteryCapacity);
            NetworkHelper.Despawn(GetComponent<NetworkObject>());
        }
    }
}
