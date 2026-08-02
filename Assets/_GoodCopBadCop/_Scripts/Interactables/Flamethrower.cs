using System;
using GoodCopBadCop.Effects;
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
public class Flamethrower : PickableObject, IAmmoProvider
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

    [Tooltip("Maximum flame reach in metres at full fuel, used for enemy hit detection.")]
    [SerializeField] private float _maxFlameRange = 4f;

    [Tooltip("Spherecast radius for enemy hit detection — controls how wide the flame cone feels.")]
    [SerializeField] private float _flameWidth = 0.5f;

    [Header("Flamethrower — Combat")]
    [Tooltip("Damage dealt to a fellow player per hit-check tick (every HitCheckInterval seconds) while they stand in the flame.")]
    [SerializeField] private float _playerDamagePerTick = 5f;

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

    // ── IAmmoProvider ─────────────────────────────────────────────────────────

    public float CurrentAmmo => _fuel.Value;
    public float MaxAmmo => MaxFuel;
    public event Action OnAmmoChanged;

    // ── Hit check throttle (owner only) ───────────────────────────────────────

    private const float HitCheckInterval = 0.2f;
    private float _hitCheckTimer;

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

        // Late-join sync: non-owners need to start the flame if it is already firing.
        if (!IsOwner && _isFiring.Value)
            StartFlame();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        _fuel.OnValueChanged     -= OnFuelChanged;
        _isFiring.OnValueChanged -= OnIsFiringChanged;
    }

    private void Update()
    {
        if (!IsOwner || !isUsing || _fuel.Value <= 0f) return;

        // Drain fuel every frame.
        DrainFuelServerRpc(_fuelDrainRate * Time.deltaTime);

        // Throttled enemy hit check — avoids sending a heavy RPC every frame.
        _hitCheckTimer -= Time.deltaTime;
        if (_hitCheckTimer <= 0f)
        {
            _hitCheckTimer = HitCheckInterval;
            Camera cam = Camera.main;
            if (cam != null)
                FireHitCheckServerRpc(cam.transform.position, cam.transform.forward);
        }
    }

    // ── NetworkVariable callbacks ──────────────────────────────────────────────

    private void OnFuelChanged(float previous, float current)
    {
        UpdateInteractText(current);
        OnAmmoChanged?.Invoke();

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
        // The owner drives VFX directly in OnStartUse/OnStopUse for instant,
        // lag-free feedback. Applying the NetworkVariable echo here would create
        // a race: a delayed StopFlame() could kill a flame the owner just restarted.
        if (IsOwner) return;

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
        if (_flameVFX != null)
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

    /// <summary>
    /// Server-side: SphereCasts along the aim direction to find enemies inside the
    /// current flame range. Newly detected enemies are ignited on all clients via
    /// <see cref="IgniteEnemyClientRpc"/>. Already-ignited enemies are skipped.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void FireHitCheckServerRpc(Vector3 origin, Vector3 direction, ServerRpcParams rpcParams = default)
    {
        if (!TryGetHolder(rpcParams.Receive.SenderClientId, out _)) return;

        ulong shooterClientId = rpcParams.Receive.SenderClientId;

        float fuelRatio  = Mathf.Clamp01(_fuel.Value / MaxFuel);
        float effectiveRange = Mathf.Lerp(_minVelocityRatio * _maxFlameRange, _maxFlameRange, fuelRatio);

        RaycastHit[] hits = Physics.SphereCastAll(origin, _flameWidth, direction, effectiveRange, ~0, QueryTriggerInteraction.Collide);
        foreach (RaycastHit hit in hits)
        {
            // ── Fire pits ─────────────────────────────────────────────────────
            FirePit firePit = hit.collider.GetComponentInParent<FirePit>();
            if (firePit != null)
            {
                if (!firePit.IsLit)
                    firePit.Ignite();
                continue;
            }

            // ── Dead player corpses / resurrected mutants / living fellow players ──
            // Must be checked BEFORE the generic MutantEnemy branch below: once a
            // CorpseResurrectionController's dormant MutantEnemy is added to the Player
            // prefab, GetComponentInParent<MutantEnemy>() would otherwise match every
            // player (dead or alive) and never reach this branch.
            CorpseResurrectionController corpse = hit.collider.GetComponentInParent<CorpseResurrectionController>();
            if (corpse != null)
            {
                // Never burn the shooter's own player.
                NetworkObject corpseNetObj = corpse.GetComponent<NetworkObject>();
                if (corpseNetObj != null && corpseNetObj.OwnerClientId == shooterClientId)
                    continue;

                PlayerHealth corpsePlayerHealth = corpse.GetComponent<PlayerHealth>();
                if (corpsePlayerHealth != null && corpsePlayerHealth.IsDead)
                {
                    // Cancel any pending resurrection (no-op once already resurrected).
                    corpse.BurnCorpse();

                    // Ignite fire VFX + damage-over-time on all clients. SetOnFire ticks damage
                    // into the same MutantEnemy component that drives the resurrected corpse,
                    // permanently destroying it regardless of resurrection state — fire is the
                    // only thing that can finish it off for good.
                    SetOnFire corpseSetOnFire = corpse.GetComponent<SetOnFire>();
                    if (corpseSetOnFire != null && !corpseSetOnFire.IsAtMaxFire)
                        IgniteEnemyClientRpc(new NetworkObjectReference(corpse.NetworkObject));
                }
                else if (corpsePlayerHealth != null)
                {
                    // Living fellow player caught in the flame — friendly-fire damage.
                    corpsePlayerHealth.TakeDamage(_playerDamagePerTick, EffectKeys.FriendlyFlamethrowerDamage);
                }

                // Never fall through to the MutantEnemy check below for players.
                continue;
            }

            // ── Mutant enemies ────────────────────────────────────────────────
            MutantEnemy enemy = hit.collider.GetComponentInParent<MutantEnemy>();
            if (enemy != null)
            {
                if (!enemy.IsDead)
                {
                    // Skip enemies already at max flames — SetOnFire handles re-ignition
                    // automatically once emitters burn out and the count drops below the cap.
                    SetOnFire setOnFire = enemy.GetComponent<SetOnFire>();
                    if (setOnFire != null && !setOnFire.IsAtMaxFire)
                        IgniteEnemyClientRpc(new NetworkObjectReference(enemy.NetworkObject));
                }
                continue;
            }
        }
    }

    /// <summary>
    /// Received on all clients. Resolves the enemy NetworkObject and calls
    /// <see cref="SetOnFire.Ignite"/> so fire particles appear on every machine.
    /// </summary>
    [ClientRpc]
    private void IgniteEnemyClientRpc(NetworkObjectReference enemyRef)
    {
        if (!enemyRef.TryGet(out NetworkObject enemyObj)) return;
        enemyObj.GetComponent<SetOnFire>()?.Ignite();
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
