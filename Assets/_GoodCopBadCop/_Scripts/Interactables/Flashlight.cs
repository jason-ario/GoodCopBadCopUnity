using Unity.Netcode;
using UnityEngine;

public class Flashlight : PickableObject
{
    [SerializeField] GameObject flashlightLight;
    [SerializeField] AudioClip flashlightOnClip;
    [SerializeField] AudioClip flashlightOffClip;
    [SerializeField] private AudioSource audioSource;
    
    private InternalBattery internalBattery;
    private NetworkVariable<bool> _isOn = new NetworkVariable<bool>(false);

    void Start()
    {
        internalBattery = GetComponent<InternalBattery>();
        internalBattery.OnBatteryDrained += TurnOffServerRpc;
    }

    void Update()
    {
        if (_isOn.Value && IsOwner && internalBattery != null)
        {
            internalBattery.DrainBattery();
        }
    }

    public override void OnStartUse()
    {
        base.OnStartUse();
        ToggleFlashlightServerRpc();
    }

    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        base.InteractWithItem(playerInteractionController, item);
        
        if (item.name == "Battery")
        {
            if (internalBattery.GetBatteryLevel() < 1)
            {
                internalBattery.Recharge(1);
                playerInteractionController.pickupController.DestroyEquippedItem();
            }
            else
            {
                Debug.Log("Battery is already full");
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void ToggleFlashlightServerRpc()
    {
        if (internalBattery.IsBatteryEmpty())
        {
            return; // Can't turn on if battery is empty
        }

        _isOn.Value = !_isOn.Value;
        flashlightLight.SetActive(_isOn.Value);
        audioSource.PlayOneShot(_isOn.Value ? flashlightOnClip : flashlightOffClip);
    }

    [Rpc(SendTo.Server)]
    private void TurnOffServerRpc()
    {
        _isOn.Value = false;
        flashlightLight.SetActive(false);
    }

    public float GetBatteryLevel()
    {
        return internalBattery?.GetBatteryLevel() ?? 0f;
    }
}
