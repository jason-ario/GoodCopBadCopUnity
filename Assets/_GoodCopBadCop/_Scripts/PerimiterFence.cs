using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using DG.Tweening;

/// <summary>
/// A single perimeter fence segment that can be damaged by mutants and repaired by players.
///
/// Health drives four visual states via <see cref="_damageStateMeshRoots"/>:
///   Index 0: Healthy  (≥ 75 % health)
///   Index 1: Slightly damaged  (≥ 50 %)
///   Index 2: Mostly damaged  (≥ 25 %)
///   Index 3: Critical — NavMeshObstacle disabled, mutants can pass through  (&lt; 25 %)
///
/// ── Networking contract ───────────────────────────────────────────────────────
/// <see cref="_health"/> is the ONE source of truth for every peer. Every derived
/// question — the visible mesh, "does this need repair?", "can a mutant walk through?",
/// and <see cref="FenceRepairTask"/>'s progress counter — is computed from it, so the
/// host and every client always agree.
///
/// Two things guarantee that agreement:
///   1. <see cref="_health"/> starts at <see cref="UninitializedHealth"/> (-1) rather than 0.
///      Previously it defaulted to 0, which reads as "totally destroyed" — any client that
///      rendered before the replicated value landed showed every fence in its most broken
///      state while the host showed them pristine. -1 is treated as "healthy until told
///      otherwise" (see <see cref="CurrentHealth"/>), so an unsynchronised fence can never
///      render the wrong state.
///   2. Clients explicitly pull the authoritative value on spawn via
///      <see cref="RequestStateSyncServerRpc"/>, so a missed/late NetworkVariable snapshot
///      still self-corrects instead of leaving the segment stuck at the wrong visual.
///
/// "Needs repair" is deliberately defined as <em>damage state > 0</em>
/// (see <see cref="IsBroken"/>) and NOT as <c>health &lt; maxHealth</c>. A fence chipped to
/// 80 % health still renders the pristine index-0 mesh, so counting it as broken produced
/// objectives that could never be finished ("14/15 repaired" with no visibly broken fence
/// left to hit). Repair snaps health back to exactly <see cref="_maxHealth"/> the moment the
/// fence re-enters state 0, keeping "state 0" and "full health" the same thing.
///
/// Prefab setup:
///   - NetworkObject on this GameObject.
///   - NavMeshObstacle on this GameObject (carving disabled — this obstacle only pushes
///     agents away via runtime avoidance rather than cutting a hole in the baked navmesh).
///     Hit-feedback shakes the active child mesh root instead of this GameObject's own
///     transform, so the obstacle itself never moves.
///   - Four child GameObjects (one per visual state) assigned to DamageStateMeshRoots.
///   - AudioSource on this GameObject.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PerimiterFence : NetworkBehaviour
{
    /// <summary>Sentinel meaning "the server has not written a health value yet".</summary>
    private const float UninitializedHealth = -1f;

    // ── Configuration ─────────────────────────────────────────────────────────

    [Header("Damage State Meshes")]
    [Tooltip("One root GameObject per visual damage state. Index 0 = healthy, higher indices = more broken. " +
             "Each root should have its own LOD Group component.")]
    [SerializeField] private GameObject[] _damageStateMeshRoots;

    [Header("Health")]
    [Tooltip("Maximum health of this fence segment.")]
    [SerializeField] private float _maxHealth = 100f;

    [Tooltip("Health restored per hammer hit during player repair.")]
    [SerializeField] private float _hammerRepairAmount = 34f;

    [Tooltip("Health-percentage thresholds (descending) at which the visual damage state advances. " +
             "Must contain one fewer entry than DamageStateMeshRoots. " +
             "Default {75, 50, 25}: state 1 below 75 %, state 2 below 50 %, passable below 25 %.")]
    [SerializeField] private float[] _damageThresholds = { 75f, 50f, 25f };

    [Header("Audio")]
    [SerializeField] private AudioClip _hammerHitSound;
    [SerializeField] private AudioClip _repairCompleteSound;
    [SerializeField] private AudioClip _mutantHitSound;
    [SerializeField] private AudioSource _audioSource;

    [Header("VFX")]
    [Tooltip("Particle system prefab spawned at the contact point when a mutant hits this fence.")]
    [SerializeField] private ParticleSystem _mutantHitParticlePrefab;

    // ── Networked state ────────────────────────────────────────────────────────

    /// <summary>
    /// Current health. Authoritative on server, replicated to all clients.
    /// Starts at <see cref="UninitializedHealth"/> and is set to <see cref="_maxHealth"/> in
    /// <see cref="OnNetworkSpawn"/>. See the class summary for why the sentinel matters.
    /// </summary>
    private readonly NetworkVariable<float> _health = new NetworkVariable<float>(
        UninitializedHealth,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Local state ────────────────────────────────────────────────────────────

    private NavMeshObstacle _navMeshObstacle;

    /// <summary>
    /// Value delivered by <see cref="SyncStateClientRpc"/>. Only consulted while
    /// <see cref="_health"/> is still un-replicated, as a safety net against a
    /// NetworkVariable snapshot that arrives after this client's OnNetworkSpawn.
    /// </summary>
    private float _fallbackHealth = UninitializedHealth;

    /// <summary>
    /// The currently active entry from <see cref="_damageStateMeshRoots"/>, kept up to date by
    /// <see cref="ApplyDamageVisuals"/>. Hit-feedback shakes this instead of <c>transform</c> so
    /// the NavMeshObstacle (on this GameObject) never itself moves — see
    /// <see cref="PlayMutantHitFeedbackClientRpc"/> and <see cref="ApplyNavMeshObstacleState"/>.
    /// </summary>
    private GameObject _activeMeshRoot;

    /// <summary>Last damage state actually pushed to the meshes; -1 = nothing applied yet.</summary>
    private int _appliedState = -1;

    /// <summary>Prevents audio from playing during initial spawn synchronisation.</summary>
    private bool _initialized;

    // ── Properties ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Health as every peer should read it. An un-replicated fence reports full health rather
    /// than 0 so it can never briefly render as destroyed on a client (see class summary).
    /// </summary>
    public float CurrentHealth
    {
        get
        {
            if (_health.Value >= 0f) return _health.Value;
            if (_fallbackHealth >= 0f) return _fallbackHealth;
            return _maxHealth;
        }
    }

    /// <summary>Maximum health of this segment.</summary>
    public float MaxHealth => _maxHealth;

    /// <summary>Current visual damage state index (0 = pristine, <see cref="MaxDamageLevel"/> = ruined).</summary>
    public int DamageState => GetDamageState(CurrentHealth);

    /// <summary>
    /// True when this fence is <em>visibly</em> damaged and therefore worth hitting with a hammer.
    /// Derived from the damage state (not raw health) so what the objective counts is exactly what
    /// the player can see and repair on every client. See class summary.
    /// </summary>
    public bool IsBroken => DamageState > 0;

    /// <summary>True when this fence is in its pristine visual state.</summary>
    public bool IsRepaired => DamageState == 0;

    /// <summary>
    /// True when this fence is in its most-damaged state and mutants can walk through it.
    /// By default, mutants can pass through once health drops below 25 %.
    /// </summary>
    public bool IsPassableByMutant => DamageState >= MaxDamageLevel;

    /// <summary>
    /// The highest valid integer damage level, derived from the number of mesh root entries.
    /// A fence with four mesh roots supports levels 0–3.
    /// </summary>
    public int MaxDamageLevel => _damageStateMeshRoots != null
        ? Mathf.Max(0, _damageStateMeshRoots.Length - 1)
        : 0;

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the server when a player's hammer hits restore this fence to its pristine state.
    /// </summary>
    public event Action<PerimiterFence> OnFullyRepaired;

    /// <summary>
    /// Raised on the server after <em>any</em> authoritative health change (repair, mutant hit, or
    /// a scripted <see cref="SetDamageLevelServer"/> call). <see cref="FenceRepairTask"/> and
    /// <see cref="FenceThreat"/> listen to this and recompute their counters from live fence state
    /// instead of incrementing a counter off the one-shot <see cref="OnFullyRepaired"/> event —
    /// an increment that drifts permanently out of sync the moment a single event is missed.
    /// </summary>
    public event Action<PerimiterFence> OnDamageStateChangedServer;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _navMeshObstacle = GetComponent<NavMeshObstacle>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _health.OnValueChanged += OnHealthChanged;

        // Server initialises health to full the first time this segment ever spawns. The sentinel
        // check means a fence that legitimately sits at 0 health is no longer silently healed
        // back to full on re-spawn (the old `_health.Value == 0f` test could not tell
        // "destroyed" apart from "never initialised").
        if (IsServer && _health.Value < 0f)
            _health.Value = _maxHealth;

        // Apply the current state immediately. OnValueChanged does not fire for initial values
        // on clients that join after the fence has already been synchronised.
        ApplyHealthState(CurrentHealth, force: true);

        // Belt-and-braces: explicitly pull the authoritative health so a client whose
        // NetworkVariable snapshot was late or dropped still converges on the host's state.
        if (IsClient && !IsServer)
            RequestStateSyncServerRpc();

        _initialized = true;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _health.OnValueChanged -= OnHealthChanged;
        _initialized = false;
        _fallbackHealth = UninitializedHealth;
    }

    // ── Explicit client resync ─────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void RequestStateSyncServerRpc(ServerRpcParams rpcParams = default)
    {
        SyncStateClientRpc(_health.Value, new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { rpcParams.Receive.SenderClientId }
            }
        });
    }

    [ClientRpc]
    private void SyncStateClientRpc(float health, ClientRpcParams rpcParams = default)
    {
        _fallbackHealth = health;

        // Deliberately bypasses OnHealthChanged so this corrective resync never plays
        // repair/hit audio on a client that simply joined late.
        ApplyHealthState(CurrentHealth, force: true);
    }

    // ── Health state ───────────────────────────────────────────────────────────

    private void OnHealthChanged(float previous, float current)
    {
        // Once the real value replicates, the fallback is obsolete.
        _fallbackHealth = UninitializedHealth;

        ApplyHealthState(current, force: true);

        if (!_initialized || _audioSource == null) return;

        // Ignore the initial sentinel → full-health write; it is initialisation, not a repair.
        if (previous < 0f) return;

        if (current >= _maxHealth && _repairCompleteSound != null)
            _audioSource.PlayOneShot(_repairCompleteSound);
        else if (current > previous && _hammerHitSound != null)
            _audioSource.PlayOneShot(_hammerHitSound);
        // Mutant hit sound is broadcast separately via PlayMutantHitFeedbackClientRpc.
    }

    /// <summary>
    /// Derives the visual damage state from the current health value, then applies mesh visuals
    /// accordingly. Physical (non-trigger) colliders are intentionally left untouched — they stay
    /// enabled at every damage state so the fence keeps physically blocking players even when
    /// fully broken, and so the hammer's hit collider (used to detect repair hits) keeps
    /// registering hits at the most-damaged state.
    /// </summary>
    private void ApplyHealthState(float health, bool force = false)
    {
        int state = GetDamageState(health);

        if (!force && state == _appliedState) return;

        _appliedState = state;
        ApplyDamageVisuals(state);
        ApplyNavMeshObstacleState(state);
    }

    /// <summary>
    /// Maps a health value to a visual damage state index.
    /// State 0 = healthy; each higher index corresponds to a lower health band.
    /// The last state (index == <see cref="MaxDamageLevel"/>) is entered when health falls
    /// below the lowest <see cref="_damageThresholds"/> entry and disables the NavMeshObstacle.
    /// </summary>
    private int GetDamageState(float health)
    {
        if (_maxHealth <= 0f || _damageThresholds == null || _damageThresholds.Length == 0)
            return 0;

        float pct = (Mathf.Max(0f, health) / _maxHealth) * 100f;

        for (int i = 0; i < _damageThresholds.Length; i++)
        {
            if (pct >= _damageThresholds[i])
                return i;
        }

        // Below all thresholds → worst (passable) state.
        return _damageThresholds.Length;
    }

    /// <summary>
    /// Lowest health value that still maps to damage state <paramref name="level"/>.
    /// Used by <see cref="SetDamageLevelServer"/> so a requested level always lands squarely
    /// inside its visual band rather than on a threshold boundary.
    /// </summary>
    private float HealthForDamageLevel(int level)
    {
        if (_damageThresholds == null || _damageThresholds.Length == 0 || level <= 0)
            return _maxHealth;

        int index = Mathf.Clamp(level, 0, _damageThresholds.Length);

        // Level i is the band [thresholds[i], thresholds[i-1]); sit in the middle of it.
        float upper = index - 1 >= 0 && index - 1 < _damageThresholds.Length
            ? _damageThresholds[index - 1]
            : 100f;
        float lower = index < _damageThresholds.Length ? _damageThresholds[index] : 0f;

        float pct = index >= _damageThresholds.Length ? 0f : (lower + upper) * 0.5f;
        return Mathf.Clamp(_maxHealth * pct * 0.01f, 0f, _maxHealth);
    }

    private void ApplyDamageVisuals(int state)
    {
        if (_damageStateMeshRoots == null) return;

        for (int i = 0; i < _damageStateMeshRoots.Length; i++)
        {
            if (_damageStateMeshRoots[i] != null)
                _damageStateMeshRoots[i].SetActive(i == state);
        }

        // Track the currently visible mesh root so hit-feedback can shake it instead of the
        // root transform (which carries the NavMeshObstacle — see PlayMutantHitFeedbackClientRpc).
        _activeMeshRoot = (state >= 0 && state < _damageStateMeshRoots.Length)
            ? _damageStateMeshRoots[state]
            : null;
    }

    /// <summary>
    /// Enables/disables the NavMeshObstacle based on damage state so mutants can pathfind
    /// straight through this fence once it reaches its most-damaged (passable) state.
    /// Carving is intentionally left OFF at all times — even with the shake moved off this
    /// transform (see <see cref="PlayMutantHitFeedbackClientRpc"/>), carving still caused
    /// mutant navigation problems, so this obstacle now only pushes agents away at runtime via
    /// NavMeshObstacle's built-in avoidance rather than cutting a hole in the baked navmesh.
    /// The physical BoxCollider is never touched here, so the player can never walk through the
    /// fence regardless of its damage state.
    /// </summary>
    private void ApplyNavMeshObstacleState(int state)
    {
        if (_navMeshObstacle == null) return;

        // Only the worst damage state (index == MaxDamageLevel) is passable by mutants.
        bool passable = state >= MaxDamageLevel;
        _navMeshObstacle.carving = false;
        _navMeshObstacle.enabled = !passable;
    }

    // ── Public server API ──────────────────────────────────────────────────────

    /// <summary>
    /// Sets this fence to a specific integer damage level. 0 = fully repaired; higher = more broken.
    /// Server-only. Health is placed in the middle of the target level's visual band so the
    /// requested level and the rendered mesh always agree.
    /// </summary>
    /// <param name="level">Target damage level. Clamped to 0–<see cref="MaxDamageLevel"/>.</param>
    public void SetDamageLevelServer(int level)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PerimiterFence] SetDamageLevelServer must be called on the server.", this);
            return;
        }

        int clamped = Mathf.Clamp(level, 0, MaxDamageLevel);
        SetHealthServer(HealthForDamageLevel(clamped));
    }

    /// <summary>
    /// Damages this fence to at least <paramref name="level"/>, never healing it.
    /// <see cref="FenceRepairTask"/>/<see cref="FenceThreat"/> use this when breaking a batch of
    /// fences: the previous <see cref="SetDamageLevelServer"/> call wrote an absolute health value,
    /// so a segment a mutant had already smashed to rubble was silently <em>healed</em> back up to
    /// the randomly rolled level. Server-only.
    /// </summary>
    public void EnsureMinimumDamageLevelServer(int level)
    {
        if (!IsServer) return;

        int clamped = Mathf.Clamp(level, 0, MaxDamageLevel);
        if (clamped <= DamageState) return;

        SetHealthServer(HealthForDamageLevel(clamped));
    }

    /// <summary>Writes health authoritatively and notifies server-side listeners. Server-only.</summary>
    private void SetHealthServer(float health)
    {
        float clamped = Mathf.Clamp(health, 0f, _maxHealth);
        if (Mathf.Approximately(_health.Value, clamped)) return;

        bool wasBroken = IsBroken;
        _health.Value = clamped;

        OnDamageStateChangedServer?.Invoke(this);

        if (wasBroken && IsRepaired)
            OnFullyRepaired?.Invoke(this);
    }

    /// <summary>
    /// Registers a single hammer hit, restoring health by <see cref="_hammerRepairAmount"/>.
    /// Raises <see cref="OnFullyRepaired"/> when the fence returns to its pristine state.
    /// Safe to call from any client — ownership is not required, and the client does NOT need to
    /// have an up-to-date view of the fence's health: the server is the sole arbiter of whether
    /// the hit does anything. That is what stopped repair from working for non-host players
    /// whose replicated health was stale.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void HitWithHammerServerRpc()
    {
        if (!IsBroken) return;

        float target = Mathf.Min(_maxHealth, CurrentHealth + Mathf.Max(1f, _hammerRepairAmount));

        // Keep the invariant "damage state 0 == exactly full health". Without this a hit that
        // lands anywhere in the 75–99 % band renders the pristine mesh while still reporting
        // health < max, which is how the objective ended up stuck one fence short with nothing
        // visibly broken left to hit.
        if (GetDamageState(target) == 0)
            target = _maxHealth;

        SetHealthServer(target);
    }

    /// <summary>
    /// Applies mutant melee damage, reducing health by <paramref name="damage"/>.
    /// Always triggers the mutant-hit feedback (sound + shake + particle) on all clients.
    /// Must be called on the server.
    /// </summary>
    /// <param name="damage">Damage amount to apply.</param>
    /// <param name="hitPosition">World-space contact point used to place the hit particle.</param>
    public void TakeMutantHitServer(float damage, Vector3 hitPosition)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PerimiterFence] TakeMutantHitServer must be called on the server.", this);
            return;
        }

        PlayMutantHitFeedbackClientRpc(hitPosition);

        if (CurrentHealth <= 0f) return;

        SetHealthServer(CurrentHealth - damage);
    }

    [ClientRpc]
    private void PlayMutantHitFeedbackClientRpc(Vector3 hitPosition)
    {
        // Sound
        if (_audioSource != null && _mutantHitSound != null)
            _audioSource.PlayOneShot(_mutantHitSound);

        // Particle
        if (_mutantHitParticlePrefab != null)
        {
            ParticleSystem instance = Instantiate(_mutantHitParticlePrefab, hitPosition, Quaternion.identity);
            Destroy(instance.gameObject, instance.main.duration + instance.main.startLifetime.constantMax);
        }

        // Shake
        // Shake the currently visible mesh root, NOT this GameObject's own transform — this
        // transform carries the NavMeshObstacle, and moving it (even briefly) causes carving
        // (with CarveOnlyStationary) to intermittently drop, breaking mutant navigation.
        Transform shakeTarget = _activeMeshRoot != null ? _activeMeshRoot.transform : transform;
        shakeTarget.DOComplete();
        shakeTarget.DOShakePosition(0.5f, strength: 0.10f, vibrato: 30, randomness: 90f, snapping: false, fadeOut: true);
    }
}
