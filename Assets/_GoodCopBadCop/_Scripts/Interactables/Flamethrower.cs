using System;
using System.Collections.Generic;
using GoodCopBadCop.Effects;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A flamethrower weapon. Hold LMB to continuously emit flames; release to stop.
///
/// Fuel is a networked float [0, <see cref="MaxFuel"/>]. As fuel depletes the
/// WFX_FlameThrower Looped particle stream shortens and thins via velocity-Z and
/// emission-rate multipliers. When fuel reaches zero the fire is forced off.
/// Refuel with R from the holder's <see cref="PlayerAmmoReserve"/> (fuel enters the reserve by
/// left-clicking a held <see cref="FlamethrowerCannister"/>).
///
/// Prefab requirements:
///   - NetworkObject
///   - NetworkTransform
///   - HighlightEffect       (required by Interactable)
///   - ParentConstraint      (required by PickableObject)
///   - Collider on the Interactable layer
///   - "Item Data" field     → Flamethrower.asset
///   - "_flameVFX"           → child WFX_FlameThrower Looped ParticleSystem
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class Flamethrower : PickableObject, IAmmoProvider, IInventoryReloadable
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

    [Header("Flamethrower — Burning Remains")]
    [Tooltip("Seconds of sustained flame contact needed to completely burn a corpse (dead player, dead " +
             "mutant, dead suspect) or a burnable gore JunkItem, after which it is removed from the world. " +
             "Burning a piece of cleanup-task gore credits the task as if it had been bagged and dumped.")]
    [Min(0.1f)]
    [SerializeField] private float _remainsBurnSeconds = 3f;

    [Tooltip("If remains go this many seconds without being hit by the flame, their partial burn progress resets.")]
    [Min(0.1f)]
    [SerializeField] private float _remainsBurnResetSeconds = 6f;

    [Tooltip("Fire VFX attached to burning remains that have no SetOnFire component (gore chunks), and " +
             "spawned as a short burst where remains burn away. Local-only, never networked.")]
    [SerializeField] private GameObject _burnFireVfxPrefab;

    [Tooltip("Uniform world scale applied to _burnFireVfxPrefab instances attached to small gore pieces.")]
    [Min(0.01f)]
    [SerializeField] private float _goreFireVfxScale = 0.6f;

    [Tooltip("Seconds the burn-away fire burst lingers after remains are removed.")]
    [Min(0f)]
    [SerializeField] private float _burnAwayVfxLifetime = 2.5f;

    [Header("Flamethrower — Audio")]
    [Tooltip("Looping AudioSource for the flame sound. Starts and stops with firing.")]
    [SerializeField] private AudioSource _flameAudioSource;

    [Tooltip("Sound played on every client when the flamethrower is refuelled from the reserve (R).")]
    [SerializeField] private AudioClip _reloadSound;

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

    protected override void CaptureMutableSaveData(PickableObjectSaveData data)
    {
        data.HasResourceAmount = true;
        data.ResourceAmount = _fuel.Value;
        data.SecondaryState = _isFiring.Value ? 1 : 0;
    }

    protected override void RestoreMutableSaveData(PickableObjectSaveData data)
    {
        if (data.HasResourceAmount)
            _fuel.Value = Mathf.Clamp(data.ResourceAmount, 0f, MaxFuel);
        _isFiring.Value = data.SecondaryState != 0 && _fuel.Value > 0f;
    }

    // ── Hit check throttle (owner only) ───────────────────────────────────────

    private const float HitCheckInterval = 0.2f;
    private float _hitCheckTimer;

    /// <summary>
    /// Extra slack (in metres) added on top of <see cref="_maxFlameRange"/> + <see cref="_flameWidth"/>
    /// when sanity-checking a client-reported flame hit against the shooter's reported origin.
    /// Covers latency-driven position drift between the moment the client resolved the hit and
    /// the moment the RPC lands on the server. This is a sanity bound, NOT a hit test.
    /// </summary>
    private const float FlameDistanceMargin = 2f;

    /// <summary>Server-only: time of the last accepted hit report, used to cap burn exposure per report.</summary>
    private float _lastBurnReportTime = float.NegativeInfinity;

    /// <summary>Name given to the local fire VFX attached to burning gore, so it's only attached once.</summary>
    private const string GoreFireVfxName = "Flamethrower_BurnFire";

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
        // Server safety net: a flame can never keep burning on a flamethrower nobody holds
        // (e.g. holder disconnected or dropped it before their stop RPC arrived).
        if (IsServer && _isFiring.Value && !IsHeld)
            _isFiring.Value = false;

        if (!isUsing || !IsLocalHolder || _fuel.Value <= 0f) return;

        // Drain fuel every frame.
        DrainFuelServerRpc(_fuelDrainRate * Time.deltaTime);

        // Throttled enemy hit check — avoids resolving/sending every frame.
        _hitCheckTimer -= Time.deltaTime;
        if (_hitCheckTimer <= 0f)
        {
            _hitCheckTimer = HitCheckInterval;
            Camera cam = Camera.main;
            if (cam != null)
                ResolveAndReportFlameHits(cam.transform.position, cam.transform.forward);
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
        if (IsLocalHolder) return;

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
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (firing)
        {
            if (!TryGetHolder(senderId, out _)) return;
            if (_fuel.Value <= 0f) return;
        }
        else
        {
            // Stopping is always safe. Accept it from the holder, or when nobody else holds the
            // item — a stop sent right as the player drops it would otherwise be rejected
            // (HeldObjectRef already cleared) and leave the flame stuck on for everyone else.
            if (IsHeldByOtherPlayerOnServer(senderId)) return;
        }

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
    /// Client-authoritative flame hit check: the SphereCast runs here, against the world exactly
    /// as this player sees it, and the resolved target lists are reported to the server. The
    /// server used to re-cast the sphere itself from the reported origin/direction — a stream
    /// that visibly engulfed a moving mutant or fellow player on the shooter's own screen could
    /// sweep past an already-moved target on the server and land nothing. See
    /// <see cref="FireHitCheckServerRpc"/>.
    /// </summary>
    private void ResolveAndReportFlameHits(Vector3 origin, Vector3 direction)
    {
        float fuelRatio       = Mathf.Clamp01(_fuel.Value / MaxFuel);
        float effectiveRange  = Mathf.Lerp(_minVelocityRatio * _maxFlameRange, _maxFlameRange, fuelRatio);

        List<NetworkObjectReference> firePitsToIgnite = new();
        List<NetworkObjectReference> flammables       = new();
        List<NetworkObjectReference> corpsesToBurn    = new();
        List<NetworkObjectReference> playersToDamage  = new();

        // A ragdoll/corpse has many colliders — report each NetworkObject only once per tick so
        // burn progress and ignition aren't multiplied by the collider count.
        HashSet<ulong> reported = new();

        RaycastHit[] hits = Physics.SphereCastAll(origin, _flameWidth, direction, effectiveRange, ~0, QueryTriggerInteraction.Collide);
        foreach (RaycastHit hit in hits)
        {
            // ── Fire pits ─────────────────────────────────────────────────────
            FirePit firePit = hit.collider.GetComponentInParent<FirePit>();
            if (firePit != null)
            {
                if (!firePit.IsLit && firePit.NetworkObject != null && reported.Add(firePit.NetworkObjectId))
                    firePitsToIgnite.Add(new NetworkObjectReference(firePit.NetworkObject));
                continue;
            }

            // ── Dead player corpses / resurrected mutants / living fellow players ──
            // Must be checked BEFORE the generic MutantEnemy branch below: the Player prefab
            // carries a dormant MutantEnemy, so GetComponentInParent<MutantEnemy>() would
            // otherwise match every player (dead or alive) and never reach this branch.
            CorpseResurrectionController corpse = hit.collider.GetComponentInParent<CorpseResurrectionController>();
            if (corpse != null)
            {
                // Never report hits against the shooter's own player. IsLocalPlayer (not an
                // OwnerClientId comparison) so the host can still burn a corpse after its former
                // owner revived and the body was handed to server ownership.
                NetworkObject corpseNetObj = corpse.NetworkObject;
                if (corpseNetObj == null || corpseNetObj.IsLocalPlayer || !reported.Add(corpseNetObj.NetworkObjectId))
                    continue;

                PlayerHealth corpsePlayerHealth = corpse.GetComponent<PlayerHealth>();
                if (corpsePlayerHealth != null && corpsePlayerHealth.IsDead)
                {
                    // Always report — the server tracks burn progress even once the fire VFX
                    // is at its emitter cap.
                    if (!corpse.IsBurnedAway)
                        corpsesToBurn.Add(new NetworkObjectReference(corpseNetObj));
                }
                else if (corpsePlayerHealth != null)
                {
                    // Living fellow player caught in the flame — friendly-fire damage.
                    playersToDamage.Add(new NetworkObjectReference(corpseNetObj));
                }

                // Never fall through to the MutantEnemy check below for players.
                continue;
            }

            // ── Mutants (living or dead) and burnable junk remains ────────────
            // Death state (MutantEnemy.IsDead / DiedPermanently) is server-only, so the client
            // can't tell a living mutant from its corpse — report both and let the server decide
            // whether to ignite it or burn it away. Burnable junk (gore, suspect bodies) is
            // reported whenever it is collectible.
            MutantEnemy enemy = hit.collider.GetComponentInParent<MutantEnemy>();
            if (enemy != null && enemy.NetworkObject != null)
            {
                if (reported.Add(enemy.NetworkObjectId))
                    flammables.Add(new NetworkObjectReference(enemy.NetworkObject));
                continue;
            }

            JunkItem junk = hit.collider.GetComponentInParent<JunkItem>();
            if (junk != null && junk.NetworkObject != null && junk.IsBurnable && junk.CanBeCollected &&
                reported.Add(junk.NetworkObjectId))
            {
                flammables.Add(new NetworkObjectReference(junk.NetworkObject));
            }
        }

        if (firePitsToIgnite.Count == 0 && flammables.Count == 0 &&
            corpsesToBurn.Count == 0 && playersToDamage.Count == 0)
            return;

        FireHitCheckServerRpc(origin, firePitsToIgnite.ToArray(), flammables.ToArray(),
            corpsesToBurn.ToArray(), playersToDamage.ToArray());
    }

    /// <summary>
    /// Server-side: validates the shooter, then applies the flame effects the CLIENT resolved in
    /// <see cref="ResolveAndReportFlameHits"/> — igniting fire pits/enemies, burning corpses, and
    /// damaging fellow players caught in the stream. The server no longer sphere-casts itself: it
    /// trusts what the shooter's own machine is engulfing, so a stream that visibly connects
    /// always lands. Ignition/health state remain server-owned, and <paramref name="origin"/> is
    /// used only for the distance sanity bound below (NOT a hit test) — it rejects a report that
    /// could only come from a bug or a modified client, it does not re-decide what was hit.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void FireHitCheckServerRpc(Vector3 origin,
        NetworkObjectReference[] firePitsToIgnite, NetworkObjectReference[] flammables,
        NetworkObjectReference[] corpsesToBurn, NetworkObjectReference[] playersToDamage,
        ServerRpcParams rpcParams = default)
    {
        if (!TryGetHolder(rpcParams.Receive.SenderClientId, out _)) return;

        ulong shooterClientId = rpcParams.Receive.SenderClientId;
        float maxReportedDistance = _maxFlameRange + _flameWidth + FlameDistanceMargin;
        float burnExposure = ConsumeBurnExposure();

        foreach (NetworkObjectReference r in firePitsToIgnite)
        {
            if (!r.TryGet(out NetworkObject obj)) continue;
            if (Vector3.Distance(origin, obj.transform.position) > maxReportedDistance) continue;

            FirePit firePit = obj.GetComponent<FirePit>();
            if (firePit != null && !firePit.IsLit)
                firePit.Ignite();
        }

        // ── Mutants (living → ignite) and remains (dead mutants, dead suspects, gore → burn away) ──
        foreach (NetworkObjectReference r in flammables)
        {
            if (!r.TryGet(out NetworkObject obj)) continue;
            if (!IsWithinReportedRange(origin, obj, maxReportedDistance)) continue;

            MutantEnemy enemy = obj.GetComponent<MutantEnemy>();
            JunkItem junk     = obj.GetComponent<JunkItem>();

            bool isDeadMutant    = enemy != null && enemy.IsDead && enemy.DiedPermanently;
            bool isBurnableJunk  = junk != null && junk.IsBurnable && junk.CanBeCollected;

            if (isDeadMutant || isBurnableJunk)
            {
                ApplyRemainsBurn(obj, burnExposure, () => BurnAwayRemains(obj, junk, isDeadMutant));
                continue;
            }

            // Living (or dormant) mutant — ignite; SetOnFire's damage-over-time kills it.
            if (enemy == null || enemy.IsDead) continue;

            SetOnFire setOnFire = enemy.GetComponent<SetOnFire>();
            if (setOnFire != null && !setOnFire.IsAtMaxFire)
                IgniteEnemyClientRpc(new NetworkObjectReference(obj));
        }

        // ── Dead player corpses ───────────────────────────────────────────────
        foreach (NetworkObjectReference r in corpsesToBurn)
        {
            if (!r.TryGet(out NetworkObject obj)) continue;
            if (IsShootersOwnPlayer(obj, shooterClientId)) continue; // never let a report burn the shooter
            if (!IsWithinReportedRange(origin, obj, maxReportedDistance)) continue;

            CorpseResurrectionController corpse = obj.GetComponent<CorpseResurrectionController>();
            PlayerHealth corpsePlayerHealth      = obj.GetComponent<PlayerHealth>();
            if (corpse == null || corpsePlayerHealth == null || !corpsePlayerHealth.IsDead) continue;
            if (corpse.IsBurnedAway) continue;

            // Cancel any pending resurrection (no-op once already resurrected).
            corpse.BurnCorpse();

            if (corpse.IsLivingMutant)
            {
                // A resurrected corpse must be killed first: SetOnFire ticks damage into the same
                // MutantEnemy that drives it — fire is the only thing that finishes it for good.
                SetOnFire mutantFire = corpse.GetComponent<SetOnFire>();
                if (mutantFire != null && !mutantFire.IsAtMaxFire)
                    IgniteEnemyClientRpc(new NetworkObjectReference(obj));
                continue;
            }

            ApplyRemainsBurn(obj, burnExposure, () =>
            {
                BurnAwayVfxClientRpc(GetVisualCenter(obj));
                corpse.BurnAwayServer();
            });
        }

        foreach (NetworkObjectReference r in playersToDamage)
        {
            if (!r.TryGet(out NetworkObject obj)) continue;
            if (IsShootersOwnPlayer(obj, shooterClientId)) continue; // never let a report hurt the shooter
            if (Vector3.Distance(origin, obj.transform.position) > maxReportedDistance) continue;

            PlayerHealth playerHealth = obj.GetComponent<PlayerHealth>();
            if (playerHealth != null && !playerHealth.IsDead)
                playerHealth.TakeDamage(_playerDamagePerTick, EffectKeys.FriendlyFlamethrowerDamage);
        }
    }

    // ── Burning remains (server) ──────────────────────────────────────────────

    /// <summary>
    /// Seconds of burn exposure this report is worth. Normally one <see cref="HitCheckInterval"/>;
    /// reports arriving much faster than the client's throttle (bunched packets or a modified
    /// client) only earn the time actually elapsed, so spamming can't burn things instantly.
    /// </summary>
    private float ConsumeBurnExposure()
    {
        float now = Time.time;
        float elapsed = now - _lastBurnReportTime;
        _lastBurnReportTime = now;
        return elapsed >= HitCheckInterval * 0.5f ? HitCheckInterval : Mathf.Max(0f, elapsed);
    }

    /// <summary>
    /// Shows fire on <paramref name="obj"/> and adds burn exposure; runs
    /// <paramref name="onBurnedAway"/> once it has been in the flame for <see cref="_remainsBurnSeconds"/>.
    /// </summary>
    private void ApplyRemainsBurn(NetworkObject obj, float exposure, Action onBurnedAway)
    {
        bool completed = FlameBurnTracker.AddExposure(obj, exposure, _remainsBurnSeconds,
            _remainsBurnResetSeconds, out bool isFirstHit);

        if (completed)
        {
            onBurnedAway?.Invoke();
            return;
        }

        SetOnFire setOnFire = obj.GetComponent<SetOnFire>();
        if (setOnFire != null)
        {
            if (!setOnFire.IsAtMaxFire)
                IgniteEnemyClientRpc(new NetworkObjectReference(obj));
        }
        else if (isFirstHit)
        {
            // Gore has no bones for SetOnFire — attach a single local fire effect instead.
            IgniteRemainsClientRpc(new NetworkObjectReference(obj));
        }
    }

    /// <summary>
    /// Removes a completely burned dead mutant / suspect body / gore piece. Junk goes through
    /// <see cref="JunkItem.BurnAwayServer"/> (which credits the cleanup task and respects reusable
    /// bodies). A dead mutant whose corpse pickup isn't enabled yet (still settling) is despawned
    /// directly — it hasn't been registered with any task at that point.
    /// </summary>
    private void BurnAwayRemains(NetworkObject obj, JunkItem junk, bool isDeadMutant)
    {
        if (obj == null || !obj.IsSpawned) return;

        BurnAwayVfxClientRpc(GetVisualCenter(obj));

        if (junk != null && junk.BurnAwayServer())
            return;

        if (isDeadMutant)
        {
            TakeOutTrashTask.Instance?.CreditBurnedJunkItem(obj);
            obj.Despawn(destroy: true);
        }
    }

    /// <summary>
    /// Distance sanity bound for remains. Ragdolls can slide their visible body away from the
    /// root transform, so the closer of the root and the rendered centre is used.
    /// </summary>
    private static bool IsWithinReportedRange(Vector3 origin, NetworkObject obj, float maxDistance)
    {
        if (Vector3.Distance(origin, obj.transform.position) <= maxDistance) return true;
        return Vector3.Distance(origin, GetVisualCenter(obj)) <= maxDistance;
    }

    /// <summary>
    /// True when <paramref name="obj"/> is the shooter's own current PlayerObject. A detached
    /// corpse owned by the server is NOT the host's player, even though its owner id matches.
    /// </summary>
    private static bool IsShootersOwnPlayer(NetworkObject obj, ulong shooterClientId)
        => obj.IsPlayerObject && obj.OwnerClientId == shooterClientId;

    /// <summary>World-space centre of the object's enabled renderers (falls back to its root position).</summary>
    private static Vector3 GetVisualCenter(NetworkObject obj)
    {
        bool hasBounds = false;
        Bounds bounds = default;

        foreach (Renderer r in obj.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
            else bounds.Encapsulate(r.bounds);
        }

        return hasBounds ? bounds.center : obj.transform.position;
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

    /// <summary>
    /// Received on all clients. Attaches one local <see cref="_burnFireVfxPrefab"/> to burning
    /// remains that have no <see cref="SetOnFire"/> (gore). It is destroyed with the piece when it
    /// burns away, so it needs no networking of its own.
    /// </summary>
    [ClientRpc]
    private void IgniteRemainsClientRpc(NetworkObjectReference remainsRef)
    {
        if (_burnFireVfxPrefab == null || !remainsRef.TryGet(out NetworkObject obj)) return;
        if (obj.transform.Find(GoreFireVfxName) != null) return;

        GameObject fire = Instantiate(_burnFireVfxPrefab, GetVisualCenter(obj), Quaternion.identity, obj.transform);
        fire.name = GoreFireVfxName;

        // Keep a consistent world size regardless of the piece's own scale.
        Vector3 lossy = obj.transform.lossyScale;
        float s = _goreFireVfxScale;
        fire.transform.localScale = new Vector3(
            lossy.x != 0f ? s / lossy.x : s,
            lossy.y != 0f ? s / lossy.y : s,
            lossy.z != 0f ? s / lossy.z : s);
    }

    /// <summary>Received on all clients. Short local fire burst where remains just burned away.</summary>
    [ClientRpc]
    private void BurnAwayVfxClientRpc(Vector3 position)
    {
        if (_burnFireVfxPrefab == null) return;

        GameObject burst = Instantiate(_burnFireVfxPrefab, position, Quaternion.identity);
        Destroy(burst, _burnAwayVfxLifetime);
    }

    // ── Refuelling (KeyCode.R, from PlayerAmmoReserve) ─────────────────────────

    public AmmoType ReserveAmmoType => AmmoType.Fuel;

    public bool NeedsReload => _fuel.Value < MaxFuel;

    /// <summary>
    /// Called by <see cref="PlayerInventory"/> when the local player presses R with this
    /// flamethrower equipped. Skips the round-trip when the tank is full or the reserve is empty.
    /// </summary>
    public void RequestReloadFromReserve()
    {
        if (_fuel.Value >= MaxFuel) return;

        PlayerAmmoReserve reserve = PlayerAmmoReserve.Local;
        if (reserve != null && reserve.Get(ReserveAmmoType) <= 0) return;

        RefuelFromReserveServerRpc();
    }

    /// <summary>
    /// Server: validates the sender is holding this flamethrower, then tops the tank up to
    /// <see cref="MaxFuel"/> using fuel from their <see cref="PlayerAmmoReserve"/>.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RefuelFromReserveServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!PlayerAmmoReserve.TryGetForHolder(clientId, NetworkObject, out PlayerAmmoReserve reserve))
        {
            Debug.LogWarning($"[Flamethrower] RefuelFromReserveServerRpc: client {clientId} is not holding this flamethrower.");
            return;
        }

        int needed = Mathf.CeilToInt(MaxFuel - _fuel.Value);
        if (needed <= 0) return;

        int taken = reserve.Take(ReserveAmmoType, needed);
        if (taken <= 0) return;

        _fuel.Value = Mathf.Min(MaxFuel, _fuel.Value + taken);
        PlayReloadSoundClientRpc();
    }

    /// <summary>Plays the refuel sound at the flamethrower on every client after a successful refuel.</summary>
    [ClientRpc]
    private void PlayReloadSoundClientRpc()
    {
        if (_reloadSound != null)
            SFXController.Instance.PlayAtPosition(_reloadSound, transform.position);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> and outputs the <see cref="PlayerPickupController"/>
    /// when <paramref name="clientId"/> is connected and currently holding this flamethrower.
    /// </summary>
    /// <remarks>
    /// Uses the replicated <see cref="PlayerPickupController.HeldObjectRef"/> rather than
    /// <see cref="PlayerPickupController.HeldObject"/>: the latter is a plain field only ever set on
    /// the holder's own machine, so on the server it reads null for every non-host client and would
    /// silently reject all of their firing/drain/hit RPCs (same fix as <c>Pistol.FireServerRpc</c>).
    /// </remarks>
    private bool TryGetHolder(ulong clientId, out PlayerPickupController ppc)
    {
        ppc = null;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            return false;

        ppc = client.PlayerObject?.GetComponent<PlayerPickupController>();
        if (ppc == null) return false;

        return ppc.HeldObjectRef.TryGet(out NetworkObject heldNetObj) && heldNetObj == NetworkObject;
    }

    /// <summary>
    /// Server-only: true when some player OTHER than <paramref name="clientId"/> is holding this item.
    /// </summary>
    private bool IsHeldByOtherPlayerOnServer(ulong clientId)
        => IsHeld && !TryGetHolder(clientId, out _);

    /// <summary>
    /// True on the machine of the player currently holding and driving this flamethrower.
    /// Deliberately independent of NetworkObject ownership (which may still be in flight right
    /// after pickup), mirroring how <c>Pistol</c> fires purely from the local holder's input.
    /// </summary>
    private bool IsLocalHolder =>
        playerPickupController != null &&
        playerPickupController.IsOwner &&
        playerPickupController.HeldObject == this;
}
