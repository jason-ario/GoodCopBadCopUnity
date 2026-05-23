using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Cycles the flashlight through three states on each use: Off → Regular → UV → Off.
/// </summary>
public class Flashlight : PickableObject
{
    [SerializeField] GameObject flashlightLight;
    [SerializeField] GameObject uvLight;
    [SerializeField] AudioClip flashlightOnClip;
    [SerializeField] AudioClip flashlightOffClip;
    [SerializeField] private AudioSource audioSource;

    private InternalBattery internalBattery;

    // 0 = Off, 1 = Regular, 2 = UV
    private NetworkVariable<int> _lightState = new NetworkVariable<int>(0);

    [SerializeField] private MeshRenderer _meshRenderer;
    [SerializeField] Material offMaterial;
    [SerializeField] Material onMaterial;
    [SerializeField] Material uvMaterial;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _lightState.OnValueChanged += OnLightStateChanged;

        // Sync state for late-joining clients.
        ApplyLightState(_lightState.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _lightState.OnValueChanged -= OnLightStateChanged;
    }

    void Start()
    {
        internalBattery = GetComponent<InternalBattery>();
        internalBattery.OnBatteryDrained += TurnOffServerRpc;
    }

    void Update()
    {
        if (_lightState.Value != 0 && IsOwner && internalBattery != null)
        {
            internalBattery.DrainBattery();
        }
    }

    /// <summary>Applies the given light state to GameObjects and the emissive mesh material.</summary>
    private void ApplyLightState(int state)
    {
        flashlightLight.SetActive(state == 1);
        uvLight.SetActive(state == 2);

        Material[] materials = _meshRenderer.materials;
        if (state == 1)
        {
            materials[1] = onMaterial;
        }
        else if (state == 2)
        {
            materials[1] = uvMaterial != null ? uvMaterial : onMaterial;
        }
        else
        {
            materials[1] = offMaterial;
        }
        _meshRenderer.materials = materials;
    }

    /// <summary>Reacts to _lightState changes on all clients, driving lights and audio.</summary>
    private void OnLightStateChanged(int previous, int current)
    {
        bool turningOn  = previous == 0 && current != 0;
        bool turningOff = current == 0;

        if (turningOn)
            audioSource.PlayOneShot(flashlightOnClip);
        else if (turningOff)
            audioSource.PlayOneShot(flashlightOffClip);

        ApplyLightState(current);
    }

    public override void OnStartUse()
    {
        base.OnStartUse();
        CycleFlashlightServerRpc();
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
    private void CycleFlashlightServerRpc()
    {
        if (internalBattery.IsBatteryEmpty())
        {
            return;
        }

        // Cycle: Off(0) → Regular(1) → UV(2) → Off(0)
        _lightState.Value = (_lightState.Value + 1) % 3;
    }

    [Rpc(SendTo.Server)]
    private void TurnOffServerRpc()
    {
        _lightState.Value = 0;
    }

    /// <summary>Returns the current battery level from the internal battery component.</summary>
    public float GetBatteryLevel()
    {
        return internalBattery?.GetBatteryLevel() ?? 0f;
    }

    /// <summary>Returns true if the regular white light is currently active.</summary>
    public bool IsRegularLightOn() => _lightState.Value == 1;

    /// <summary>Returns true if the UV light is currently active.</summary>
    public bool IsUVLightOn() => _lightState.Value == 2;
}
