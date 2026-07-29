using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked interactable light switch for the booth.
/// Toggles a set of <see cref="ElectricObject"/> components — invoking their
/// <see cref="ElectricObject.OnElectricityTurnOn"/> / <see cref="ElectricObject.OnElectricityTurnOff"/>
/// UnityEvents — only when <see cref="ElectricityController"/> reports power is available.
/// Starts ON by default every day, including Day 1.
///
/// Electricity responsiveness:
///   Add an <see cref="ElectricObject"/> to this GameObject and register it with the booth's
///   <see cref="ElectricityController"/>. Wire its events:
///     OnElectricityTurnOn  → BoothLightSwitch.OnElectricityOn
///     OnElectricityTurnOff → BoothLightSwitch.OnElectricityOff
/// </summary>
public class BoothLightSwitch : Interactable, IHeldItemPassthrough
{
    [Header("Switch Visual")]
    [Tooltip("The child Transform that physically rotates (the 'switch' child).")]
    [SerializeField] private Transform _switchTransform;

    [Tooltip("Local euler angles when the switch is ON.")]
    [SerializeField] private Vector3 _onRotation = new Vector3(-45f, 0f, 0f);

    [Tooltip("Local euler angles when the switch is OFF.")]
    [SerializeField] private Vector3 _offRotation = new Vector3(45f, 0f, 0f);

    [Header("Controlled Objects")]
    [Tooltip("ElectricObjects whose OnElectricityTurnOn/Off events are invoked by this switch.")]
    [SerializeField] private ElectricObject[] _controlledObjects;

    [Tooltip("Reference to the booth's ElectricityController. " +
             "Controlled objects only activate when both the switch is ON and power is active.")]
    [SerializeField] private ElectricityController _electricityController;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _switchOnSound;
    [SerializeField] private AudioClip _switchOffSound;

    // -----------------------------------------------------------------------
    // Network state
    // -----------------------------------------------------------------------

    private NetworkVariable<bool> _isOn = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsOn => _isOn.Value;

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    public override void OnNetworkSpawn()
    {
        _isOn.OnValueChanged += OnSwitchStateChanged;

        // Snap the switch transform to the current networked state.
        // Do NOT call RefreshLights here — electricity is off at spawn time and would
        // immediately disable lights that are meant to be on by default.
        // Light state is driven by OnElectricityOn/Off (wired via ElectricObject) and
        // by OnSwitchStateChanged for late-joining clients that missed a toggle.
        ApplySwitchVisual(_isOn.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isOn.OnValueChanged -= OnSwitchStateChanged;
    }

    private void Start()
    {
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += HandleDayStart;
    }

    private void OnDestroy()
    {
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= HandleDayStart;
    }

    // -----------------------------------------------------------------------
    // Day reset
    // -----------------------------------------------------------------------

    private void HandleDayStart()
    {
        if (!IsServer) return;
        if (ShiftManager.Instance.CurrentDay <= 1) return;

        // Reset to ON at the start of every day — the booth should always start lit.
        _isOn.Value = true;
        ResetSwitchClientRpc(true);
    }

    // -----------------------------------------------------------------------
    // Interaction
    // -----------------------------------------------------------------------

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        ToggleSwitchServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleSwitchServerRpc()
    {
        _isOn.Value = !_isOn.Value;
        UpdateSwitchClientRpc(_isOn.Value);
    }

    [ClientRpc]
    private void UpdateSwitchClientRpc(bool isOn)
    {
        ApplySwitchVisual(isOn);
        PlaySwitchSound(isOn);
        RefreshObjects(isOn);
    }

    [ClientRpc]
    private void ResetSwitchClientRpc(bool isOn)
    {
        ApplySwitchVisual(isOn);
        RefreshObjects(isOn);
    }

    // -----------------------------------------------------------------------
    // Electricity callbacks — wire via Inspector through ElectricObject events
    // -----------------------------------------------------------------------

    /// <summary>
    /// Wire to the <see cref="ElectricObject.OnElectricityTurnOn"/> UnityEvent on this
    /// GameObject (or the booth's matching ElectricObject) in the Inspector.
    /// </summary>
    public void OnElectricityOn()
    {
        RefreshObjects(_isOn.Value);
    }

    public void OnElectricityOff()
    {
        FireElectricEvents(false);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void ApplySwitchVisual(bool isOn)
    {
        if (_switchTransform == null) return;
        _switchTransform.localEulerAngles = isOn ? _onRotation : _offRotation;
    }

    private void RefreshObjects(bool switchIsOn)
    {
        bool powerAvailable = _electricityController == null || _electricityController.IsPowerOn;
        FireElectricEvents(switchIsOn && powerAvailable);
    }

    private void FireElectricEvents(bool on)
    {
        foreach (ElectricObject obj in _controlledObjects)
        {
            if (obj == null) continue;
            if (on)
                obj.OnElectricityTurnOn?.Invoke();
            else
                obj.OnElectricityTurnOff?.Invoke();
        }
    }

    private void PlaySwitchSound(bool isOn)
    {
        if (_audioSource == null) return;
        AudioClip clip = isOn ? _switchOnSound : _switchOffSound;
        if (clip != null)
            _audioSource.PlayOneShot(clip);
    }

    /// <summary>
    /// NetworkVariable change callback — catches up late-joining clients without replaying SFX.
    /// </summary>
    private void OnSwitchStateChanged(bool previous, bool current)
    {
        ApplySwitchVisual(current);
        RefreshObjects(current);
    }

    // -----------------------------------------------------------------------
    // Public server-side API
    // -----------------------------------------------------------------------

    /// <summary>Sets the switch to a specific state from server code.</summary>
    public void SetSwitchState(bool isOn)
    {
        if (!IsServer) return;
        _isOn.Value = isOn;
        UpdateSwitchClientRpc(isOn);
    }
}
