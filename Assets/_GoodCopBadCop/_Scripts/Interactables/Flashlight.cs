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

    private Light _flashlightLightComp;
    private Light _uvLightComp;
    private float _baseFlashlightIntensity;
    private float _baseUVIntensity;

    private float _flickerTimer;
    private const float FLICKER_THRESHOLD = 0.25f; // Battery level where flickering starts
    private const float MIN_DIM_THRESHOLD = 0.5f;   // Battery level where dimming starts to be noticeable

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

        if (flashlightLight != null)
        {
            _flashlightLightComp = flashlightLight.GetComponent<Light>();
            if (_flashlightLightComp != null) _baseFlashlightIntensity = _flashlightLightComp.intensity;
        }

        if (uvLight != null)
        {
            _uvLightComp = uvLight.GetComponent<Light>();
            if (_uvLightComp != null) _baseUVIntensity = _uvLightComp.intensity;
        }
    }

    void Update()
    {
        if (_lightState.Value != 0 && IsOwner && internalBattery != null)
        {
            internalBattery.DrainBattery();
        }

        UpdateLightVisuals();
    }

    private void UpdateLightVisuals()
    {
        if (internalBattery == null) return;

        float batteryLevel = internalBattery.GetBatteryPercentage();
        int state = _lightState.Value;

        if (state == 0) return;

        Light targetLight = (state == 1) ? _flashlightLightComp : _uvLightComp;
        float baseIntensity = (state == 1) ? _baseFlashlightIntensity : _baseUVIntensity;

        if (targetLight == null) return;

        // Calculate dimming
        // We start dimming when battery is below MIN_DIM_THRESHOLD
        float dimFactor = 1f;
        if (batteryLevel < MIN_DIM_THRESHOLD)
        {
            dimFactor = Mathf.Lerp(0.1f, 1f, batteryLevel / MIN_DIM_THRESHOLD);
        }

        float currentIntensity = baseIntensity * dimFactor;

        // Calculate flickering
        if (batteryLevel < FLICKER_THRESHOLD)
        {
            // The lower the battery, the more aggressive the flicker
            float flickerChance = Mathf.Lerp(0.8f, 0.1f, batteryLevel / FLICKER_THRESHOLD);
            _flickerTimer -= Time.deltaTime;

            if (_flickerTimer <= 0)
            {
                // Randomly drop intensity to near zero or keep it
                bool isOn = UnityEngine.Random.value > (1f - flickerChance);
                targetLight.intensity = isOn ? currentIntensity : currentIntensity * UnityEngine.Random.Range(0f, 0.3f);
                _flickerTimer = UnityEngine.Random.Range(0.02f, 0.15f);
            }
        }
        else
        {
            targetLight.intensity = currentIntensity;
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

        if (item.GetComponent<Battery>() != null)
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

    /// <summary>Sends a server RPC to turn the flashlight off. Safe to call from any client.</summary>
    public void TurnOff()
    {
        TurnOffServerRpc();
    }

    public override void OnStowed() => TurnOff();

    /// <summary>
    /// Disables lights directly on the local ghost clone so the placement preview
    /// never shows the flashlight as active, regardless of the real item's state.
    /// </summary>
    public override void OnSpawnedAsPlacementClone()
    {
        flashlightLight.SetActive(false);
        uvLight.SetActive(false);
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
