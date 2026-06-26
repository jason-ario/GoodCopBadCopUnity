using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A flamethrower weapon. Hold LMB to continuously emit flames; release to stop.
///
/// Fuel is a networked float [0, <see cref="MaxFuel"/>]. As fuel depletes the
/// WFX_FlameThrower Looped particle stream shortens and thins via velocity-Z and
/// emission-rate multipliers. When fuel reaches zero the fire is forced off.
/// Refuel by using a <see cref="FlamethrowerCannister"/> on the world pickup.
///
/// Prefab requirements:
///   - NetworkObject
///   - NetworkTransform
///   - HighlightEffect       (required by Interactable)
///   - ParentConstraint      (required by PickableObject)
///   - Collider on the Interactable layer
///   - "Item Data" field     → Flamethrower.asset
///   - "_flameVFX"           → child WFX_FlameThrower Looped ParticleSystem
///   - "itemsThatCanInteractWith" → FlamethrowerCannister.asset
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class Flamethrower : PickableObject
{
    /// <summary>Maximum fuel the tank can hold.</summary>
    public const float MaxFuel = 100f;

    [Header("Flamethrower — VFX")]
    [Tooltip("WFX_FlameThrower Looped particle system child that represents the flame stream.")]
    [SerializeField] private ParticleSystem _flameVFX;

    [Header("Flamethrower — Fuel")]
    [Tooltip("Fuel consumed per second while firing.")]
    [SerializeField] private float _fuelDrainRate = 12f;

    [Header("Flamethrower — Range")]
    [Tooltip("Velocity-Z multiplier at minimum fuel (stream barely visible).")]
    [SerializeField] private float _minVelocityRatio = 0.05f;

    [Header("Flamethrower — Audio")]
    [Tooltip("Looping AudioSource for the flame sound. Starts and stops with firing.")]
    [SerializeField] private AudioSource _flameAudioSource;

    // ── Networked state ────────────────────────────────────────────────────────

    private readonly NetworkVariable<float> _fuel = new(
        MaxFuel,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Authoritative firing state. Watched by all clients to start/stop the flame VFX,
    /// including late-joining clients who would otherwise miss the start event.
    /// </summary>
    private readonly NetworkVariable<bool> _isFiring = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Current fuel level [0, <see cref="MaxFuel"/>].</summary>
    public float Fuel => _fuel.Value;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        if (_flameVFX != null)
        {
            // Prevent the stop-action from destroying the child GameObject after the first use.
            ParticleSystem.MainModule main = _flameVFX.main;
            main.stopAction = ParticleSystemStopAction.None;

            // CFX_AutoDestructShuriken polls IsAlive() every 0.5 s and destroys or
            // deactivates the GameObject when it finds the system stopped — which can
            // happen briefly during networked start/stop transitions. Disable it so
            // the particle system lifetime is controlled entirely by this script.
            CFX_AutoDestructShuriken autoDestruct = _flameVFX.GetComponent<CFX_AutoDestructShuriken>();
            if (autoDestruct != null)
                autoDestruct.enabled = false;
        }

        UpdateInteractText(MaxFuel);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _fuel.OnValueChanged     += OnFuelChanged;
        _isFiring.OnValueChanged += OnIsFiringChanged;

        if (IsServer)
            _fuel.Value = MaxFuel;

        // Sync UI and particle state for late-joining clients.
        UpdateInteractText(_fuel.Value);
        ApplyParticleScale(_fuel.Value / MaxFuel);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        _fuel.OnValueChanged     -= OnFuelChanged;
        _isFiring.OnValueChanged -= OnIsFiringChanged;
    }

    private void Update()
    {
        // Owner drains fuel every frame while firing.
        if (IsOwner && isUsing && _fuel.Value > 0f)
            DrainFuelServerRpc(_fuelDrainRate * Time.deltaTime);
    }

    // ── NetworkVariable callbacks ──────────────────────────────────────────────

    private void OnFuelChanged(float previous, float current)
    {
        UpdateInteractText(current);

        // Scale particle stream length and density to reflect remaining fuel.
        // Called here (on NetworkVariable change) rather than every Update frame
        // so the emission module's internal accumulator is never reset mid-stream.
        if (_isFiring.Value)
            ApplyParticleScale(current / MaxFuel);

        // Server forces a stop when the tank empties.
        if (IsServer && current <= 0f && _isFiring.Value)
            _isFiring.Value = false;
    }

    private void OnIsFiringChanged(bool previous, bool current)
    {
        if (current)
            StartFlame();
        else
            StopFlame();
    }

    private void UpdateInteractText(float fuel)
        => interactText = $"Flamethrower ({fuel:F0}/{MaxFuel:F0})";

    // ── Firing ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called on the owner when LMB is pressed. Starts the flame locally for instant
    /// feedback and tells the server to set the authoritative firing state so all
    /// other clients display the effect.
    /// </summary>
    public override void OnStartUse()
    {
        base.OnStartUse();

        if (_fuel.Value <= 0f) return;

        StartFlame();
        SetFiringServerRpc(true);
    }

    /// <summary>
    /// Called on the owner when LMB is released. Stops the flame locally and clears
    /// the networked firing state.
    /// </summary>
    public override void OnStopUse()
    {
        base.OnStopUse();
        StopFlame();
        SetFiringServerRpc(false);
    }

    // Body-item VFX is handled entirely by the _isFiring NetworkVariable,
    // so body start/stop are intentionally no-ops.
    public override void OnBodyStartUse() { }
    public override void OnBodyStopUse() { }

    private void StartFlame()
    {
        if (_flameVFX != null && !_flameVFX.isPlaying)
            _flameVFX.Play();

        if (_flameAudioSource != null && !_flameAudioSource.isPlaying)
            _flameAudioSource.Play();
    }

    private void StopFlame()
    {
        if (_flameVFX != null)
            _flameVFX.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        if (_flameAudioSource != null)
            _flameAudioSource.Stop();
    }

    /// <summary>
    /// Scales the particle system's velocity-Z multiplier and emission rate based on
    /// <paramref name="fuelRatio"/> [0, 1] to shorten and thin the stream as fuel depletes.
    /// </summary>
    private void ApplyParticleScale(float fuelRatio)
    {
        if (_flameVFX == null) return;

        ParticleSystem.VelocityOverLifetimeModule vel = _flameVFX.velocityOverLifetime;
        vel.zMultiplier = Mathf.Lerp(_minVelocityRatio, 1f, Mathf.Clamp01(fuelRatio));
    }

    // ── Server RPCs ────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates that the requesting client is currently holding this flamethrower,
    /// then updates <see cref="_isFiring"/> which propagates to all clients.
    /// RequireOwnership = false because ownership transfer may still be in flight.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void SetFiringServerRpc(bool firing, ServerRpcParams rpcParams = default)
    {
        if (!TryGetHolder(rpcParams.Receive.SenderClientId, out _)) return;
        if (firing && _fuel.Value <= 0f) return;

        _isFiring.Value = firing;
    }

    /// <summary>
    /// Deducts <paramref name="amount"/> fuel on the server. Validated against the
    /// current holder so no client can drain another player's flamethrower remotely.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void DrainFuelServerRpc(float amount, ServerRpcParams rpcParams = default)
    {
        if (!TryGetHolder(rpcParams.Receive.SenderClientId, out _)) return;

        _fuel.Value = Mathf.Max(_fuel.Value - amount, 0f);
    }

    // ── Refuelling ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the player presses E while targeting the flamethrower while
    /// holding a <see cref="FlamethrowerCannister"/>. Delegates to
    /// <see cref="TryRefuel"/>.
    /// </summary>
    public override void InteractAlternate(PlayerInteractionController player)
        => TryRefuel(player);

    /// <summary>
    /// Called when LMB is pressed with a compatible item in hand
    /// (<see cref="FlamethrowerCannister"/> listed in <c>itemsThatCanInteractWith</c>).
    /// Delegates to <see cref="TryRefuel"/>.
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        base.InteractWithItem(playerInteractionController, item);
        TryRefuel(playerInteractionController);
    }

    private void TryRefuel(PlayerInteractionController player)
    {
        if (player.pickupController.HeldObject is not FlamethrowerCannister) return;
        if (_fuel.Value >= MaxFuel) return;

        RefuelServerRpc();
    }

    /// <summary>
    /// Server-side: validates the sender is holding a <see cref="FlamethrowerCannister"/>
    /// and the tank is not full, then fills the tank to <see cref="MaxFuel"/> and
    /// instructs the sender's client to despawn the cannister.
    /// RequireOwnership = false so any client can refuel regardless of who holds the gun.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RefuelServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            Debug.LogWarning($"[Flamethrower] RefuelServerRpc: client {clientId} not found.");
            return;
        }

        PlayerPickupController ppc = client.PlayerObject?.GetComponent<PlayerPickupController>();
        if (ppc == null || ppc.HeldObject is not FlamethrowerCannister)
        {
            Debug.LogWarning($"[Flamethrower] RefuelServerRpc: client {clientId} is not holding a FlamethrowerCannister.");
            return;
        }

        if (_fuel.Value >= MaxFuel) return;

        _fuel.Value = MaxFuel;

        ConsumeCanisterClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        });
    }

    /// <summary>
    /// Received only by the player who triggered the refuel. Despawns the equipped
    /// cannister via <see cref="PlayerPickupController.DestroyEquippedItem"/>.
    /// </summary>
    [ClientRpc]
    private void ConsumeCanisterClientRpc(ClientRpcParams clientRpcParams = default)
    {
        PlayerPickupController ppc = NetworkManager.Singleton.LocalClient?.PlayerObject
            ?.GetComponent<PlayerPickupController>();

        if (ppc == null)
        {
            Debug.LogWarning("[Flamethrower] ConsumeCanisterClientRpc: could not find local PlayerPickupController.");
            return;
        }

        ppc.DestroyEquippedItem();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> and outputs the <see cref="PlayerPickupController"/>
    /// when <paramref name="clientId"/> is connected and currently holding this flamethrower.
    /// </summary>
    private bool TryGetHolder(ulong clientId, out PlayerPickupController ppc)
    {
        ppc = null;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            return false;

        ppc = client.PlayerObject?.GetComponent<PlayerPickupController>();
        return ppc?.HeldObject == this;
    }
}
