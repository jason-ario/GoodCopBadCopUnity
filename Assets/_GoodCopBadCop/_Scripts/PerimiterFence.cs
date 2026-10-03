using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using DG.Tweening;
using HighlightPlus;

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
///   - NavMeshObstacle on this GameObject. It carves the fence out of the NavMesh at runtime and
///     is disabled during breaches (see ApplyNavMeshObstacleState).
///   - NavMeshModifier with Ignore From Build, so the collider is NOT baked into the NavMesh.
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

    [Tooltip("One-shot effect (smoke plume + sparks) spawned at the base of the fence when a player's hammer " +
             "fully repairs it. Spawned so its local X runs along the fence and +Y points up. Any Box-shaped " +
             "ParticleSystem in it has its shape X scale stretched to the segment's width.")]
    [SerializeField] private GameObject _repairCompleteVfxPrefab;

    [Tooltip("Vertical offset (metres) applied to the repair VFX spawn point above the collider's base.")]
    [SerializeField] private float _repairVfxHeightOffset = 0.1f;

    [Header("Repair Punch")]
    [Tooltip("Local-scale punch applied to the repaired (state 0) mesh root when the fence is fixed. " +
             "Relative to its current scale; Y-heavy so the fence 'pops' upward from its base.")]
    [SerializeField] private Vector3 _repairPunchScale = new Vector3(0.06f, 0.18f, 0.06f);
    [SerializeField] private float _repairPunchDuration = 0.45f;
    [SerializeField] private int _repairPunchVibrato = 7;
    [Range(0f, 1f)]
    [SerializeField] private float _repairPunchElasticity = 0.6f;


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

    /// <summary>Physical fence collider on this GameObject; used to locate the fence base for repair VFX.</summary>
    private BoxCollider _boxCollider;

    /// <summary>
    /// Set when <see cref="PlayRepairCompleteFeedbackClientRpc"/> arrives before the replicated health
    /// has switched this client to the repaired mesh, so the punch plays on the right mesh once it does.
    /// </summary>
    private bool _pendingRepairPunch;

    /// <summary>
    /// Optional HighlightPlus outline shown while this segment is broken and
    /// <see cref="FenceRepairTask.ShowRepairHighlights"/> is enabled — see
    /// <see cref="RefreshRepairHighlight"/>. Null-safe throughout: a fence prefab without this
    /// component simply never glows.
    /// </summary>
    private HighlightEffect _highlightEffect;

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
        _boxCollider = GetComponent<BoxCollider>();
        ApplyNavMeshObstacleState(0);

        _highlightEffect = GetComponent<HighlightEffect>();
        if (_highlightEffect != null)
        {
            // Force enabled so Start()/SetupMaterial() always run — a prefab saved with the
            // component disabled would otherwise silently never render on first activation.
            // Visibility is driven solely through 'highlighted' (see RefreshRepairHighlight).
            _highlightEffect.enabled = true;
            _highlightEffect.highlighted = false;
        }
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
        CompassMarkerRegistry.Unregister(transform);
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

        // Repair feedback RPC beat the health replication here — punch now that the fixed mesh is live.
        if (_pendingRepairPunch)
        {
            _pendingRepairPunch = false;
            if (IsRepaired) PlayRepairPunch();
        }

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

        // Compass pip: mirrors IsBroken (damage state > 0), independent of the force/early-out
        // below so it stays correct even on the ApplyHealthState(force:true) calls used purely
        // for a late-joining client's initial sync.
        if (state > 0)
            CompassMarkerRegistry.Register(transform, CompassMarkerCategory.Fence);
        else
            CompassMarkerRegistry.Unregister(transform);

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

        // The active mesh root just changed — rebuild HighlightPlus's cached renderer list so the
        // outline follows the newly visible mesh instead of staying targeted at the previous
        // (now-inactive) one.
        if (_highlightEffect != null)
            _highlightEffect.Refresh(true);

        RefreshRepairHighlight();
    }

    /// <summary>
    /// Syncs this fence's HighlightPlus outline to whether it currently needs repair. No-ops if
    /// this prefab has no <see cref="HighlightEffect"/> component.
    ///
    /// Called after every damage-state change (this segment breaking or getting repaired) and by
    /// <see cref="FenceRepairTask"/> whenever its active state flips, so:
    ///   - a segment broken between a mutant breach and the repair task actually being triggered
    ///     stays dark, matching the compass pip's "only glow while the task is live" rule, and
    ///   - every currently-broken tracked segment lights up the instant the task starts, and goes
    ///     dark the instant it's repaired or the task completes.
    ///
    /// Independent of the compass pip (<see cref="CompassMarkerRegistry"/>/<see cref="CompassController"/>)
    /// — <see cref="FenceRepairTask.ShowRepairHighlights"/> only gates this in-world glow.
    /// </summary>
    public void RefreshRepairHighlight()
    {
        if (_highlightEffect == null) return;

        bool taskActive = FenceRepairTask.Instance != null && FenceRepairTask.Instance.IsActive;
        bool highlightsEnabled = FenceRepairTask.Instance == null || FenceRepairTask.Instance.ShowRepairHighlights;

        _highlightEffect.highlighted = IsBroken && taskActive && highlightsEnabled;
    }

    // ── Breach pass-through ────────────────────────────────────────────────────

    private static readonly System.Collections.Generic.List<PerimiterFence> s_fences = new();
    private static bool s_breachPassThrough;

    /// <summary>
    /// Toggles fence carving for every fence. While true (a breach is running) fence obstacles stop
    /// carving the NavMesh so mutants path straight through fence lines and smash whatever fence
    /// is on their route. Called by <see cref="MutantBreachManager"/> on every peer.
    /// </summary>
    public static void SetBreachPassThrough(bool passThrough)
    {
        s_breachPassThrough = passThrough;
        foreach (var fence in s_fences)
            if (fence != null) fence.ApplyNavMeshObstacleState(fence._appliedState < 0 ? 0 : fence._appliedState);
    }

    private void OnEnable()  { if (!s_fences.Contains(this)) s_fences.Add(this); }
    private void OnDisable() => s_fences.Remove(this);

    /// <summary>
    /// The fence's NavMeshObstacle is the only thing that makes it a NavMesh wall. The fence is
    /// excluded from the NavMesh bake (NavMeshModifier → Ignore From Build), and the obstacle carves
    /// it out at runtime instead:
    ///   - Normal play: carving on, so NPCs and mutants path around the fence.
    ///   - Breach running, or fence broken through (worst damage state): obstacle disabled, so
    ///     mutants path through. It is disabled rather than just non-carving, because a
    ///     non-carving obstacle still applies local avoidance and pushes agents sideways.
    /// The physical BoxCollider is never touched, so players can never walk through the fence.
    /// </summary>
    private void ApplyNavMeshObstacleState(int state)
    {
        if (_navMeshObstacle == null) return;

        bool passable = s_breachPassThrough || (MaxDamageLevel > 0 && state >= MaxDamageLevel);
        _navMeshObstacle.carving = true;
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

        // Hammer-only celebration. Deliberately NOT driven from OnHealthChanged: save/day restores
        // (FenceRepairTask → SetDamageLevelServer) also heal fences and must not puff smoke everywhere.
        if (IsRepaired)
            PlayRepairCompleteFeedbackClientRpc();
    }

    // ── Repair feedback ────────────────────────────────────────────────────────

    [ClientRpc]
    private void PlayRepairCompleteFeedbackClientRpc()
    {
        SpawnRepairVfx();

        // The RPC and the health NetworkVariable can land in either order on clients. If the fixed
        // mesh isn't active yet, defer the punch to OnHealthChanged so it plays on the visible mesh.
        if (IsRepaired) PlayRepairPunch();
        else _pendingRepairPunch = true;
    }

    private void PlayRepairPunch()
    {
        if (_damageStateMeshRoots == null || _damageStateMeshRoots.Length == 0 || _damageStateMeshRoots[0] == null)
            return;

        // Punch the pristine mesh root, never this transform (it carries the NavMeshObstacle).
        Transform target = _damageStateMeshRoots[0].transform;
        target.DOComplete();
        target.DOPunchScale(Vector3.Scale(target.localScale, _repairPunchScale),
            _repairPunchDuration, _repairPunchVibrato, _repairPunchElasticity);
    }

    private void SpawnRepairVfx()
    {
        if (_repairCompleteVfxPrefab == null) return;

        Vector3 basePoint = transform.position;
        Vector3 along = transform.right;
        float width = 0f;

        if (_boxCollider != null)
        {
            Vector3 c = _boxCollider.center;
            Vector3 s = _boxCollider.size;
            basePoint = transform.TransformPoint(new Vector3(c.x, c.y - s.y * 0.5f, c.z));

            // The fence runs along whichever horizontal collider axis is longer.
            Vector3 scale = transform.lossyScale;
            float xLen = Mathf.Abs(s.x * scale.x);
            float zLen = Mathf.Abs(s.z * scale.z);
            if (zLen > xLen) { along = transform.forward; width = zLen; }
            else width = xLen;
        }

        basePoint.y += _repairVfxHeightOffset;
        along = Vector3.ProjectOnPlane(along, Vector3.up);
        if (along.sqrMagnitude < 0.0001f) along = Vector3.right;

        // LookRotation(cross(along, up), up) yields local X == along, local Y == world up.
        Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(along.normalized, Vector3.up), Vector3.up);
        GameObject instance = Instantiate(_repairCompleteVfxPrefab, basePoint, rotation);

        float maxLifetime = 0f;
        foreach (ParticleSystem ps in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (width > 0f)
            {
                ParticleSystem.ShapeModule shape = ps.shape;
                if (shape.enabled && shape.shapeType == ParticleSystemShapeType.Box)
                    shape.scale = new Vector3(width, shape.scale.y, shape.scale.z);
            }

            ParticleSystem.MainModule main = ps.main;
            maxLifetime = Mathf.Max(maxLifetime, main.startDelay.constantMax + main.duration + main.startLifetime.constantMax);
        }

        Destroy(instance, Mathf.Max(0.5f, maxLifetime));
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
