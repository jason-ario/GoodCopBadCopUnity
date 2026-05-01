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

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isOn.OnValueChanged += OnIsOnChanged;

        // Sync the light state for late-joining clients.
        flashlightLight.SetActive(_isOn.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isOn.OnValueChanged -= OnIsOnChanged;
    }

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

    /// <summary>Reacts to _isOn changes on all clients, driving the light and audio.</summary>
    private void OnIsOnChanged(bool previous, bool current)
    {
        flashlightLight.SetActive(current);
        audioSource.PlayOneShot(current ? flashlightOnClip : flashlightOffClip);
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
    }

    [Rpc(SendTo.Server)]
    private void TurnOffServerRpc()
    {
        _isOn.Value = false;
    }

    /// <summary>Returns the current battery level from the internal battery component.</summary>
    public float GetBatteryLevel()
    {
        return internalBattery?.GetBatteryLevel() ?? 0f;
    }
}
