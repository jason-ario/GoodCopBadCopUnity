using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Systemic threat: "Follow the Trail".
/// A grotesque corpse appears at the start of the day, leading to an invisible trail
/// of particles only visible under UV light. Investigating the final destination
/// resolves the threat and spawns an enemy pack if one is configured on the location.
///
/// Also acts as the central network-sync hub for the post-shift Vlad Out-Back sequence.
/// Three NetworkVariables drive TaskRegistry registration on ALL clients so the HUD
/// stays in sync without requiring Day_02 to be a NetworkBehaviour itself:
///   _meetVladActive      — "Meet Vlad out back"
///   _followTrailActive   — "Follow the trail" (this threat)
///   _killMutantCount     — "Kill the mutants" (0 = inactive, N = kill N enemies)
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class FollowTrailThreat : NetworkBehaviour, ISystemicThreat, IDailyTask
{
    public static FollowTrailThreat Instance { get; private set; }

    [Serializable]
    public struct FollowTrailLocation
    {
        public Transform       CorpsePoint;
        public Transform       DestinationPoint;
        /// <summary>Spline-based trail. Spawn count and jitter are configured on the TrailController itself.</summary>
        public TrailController Trail;
        /// <summary>Spawner used to create the enemy pack at the destination. Leave null for no pack.</summary>
        public MutantSpawner   PackSpawner;
        /// <summary>Number of enemies to spawn. 0 = no pack.</summary>
        public int             PackSize;
    }

    [Header("Threat Properties")]
    [SerializeField] private string _threatName = "Use UV light to follow the trail of irradiated blood.";
    [Tooltip("Number of coupons the ATM dispenses when the trail destination is discovered.")]
    [SerializeField] private int _couponReward = 10;

    [Header("Daily Task")]
    [Tooltip("Stable identifier used by DailyTaskScheduler and SaveDataManager. Must match the TaskId entry in DailyTaskScheduler's pool.")]
    [SerializeField] private string _dailyTaskId = "FollowTrail";

    [Header("Prefabs")]
    [SerializeField] private GameObject _corpsePrefab;
    [SerializeField] private GameObject _trailParticlesPrefab;
    [SerializeField] private GameObject _destinationPrefab;

    [Header("Spawn Locations")]
    [SerializeField] private List<FollowTrailLocation> _possibleLocations;

    [Header("Trail Placement")]
    [Tooltip("Layers to hit when snapping particles to the terrain. Set to your terrain/ground layer for best results.")]
    [SerializeField] private LayerMask _terrainLayerMask = Physics.AllLayers;

    [Tooltip("Y offset above the terrain surface applied to every spawned blood particle.")]
    [SerializeField] private float _trailGroundOffset = 0.05f;

    [Header("Destination Detection")]
    [Tooltip("Any player within this radius of the destination point completes the follow trail task.")]
    [SerializeField] private float _destinationRadius = 10f;

    [Header("Pack Hold-In-Place")]
    [Tooltip("Pack mutants spawned at the destination are held frozen in place (no patrol/wander/chase) " +
             "until a player comes within this distance of the destination point — keeps them clustered " +
             "at the end of the trail instead of wandering off before the player arrives.")]
    [SerializeField] private float _packHoldReleaseRadius = 20f;

    [Header("End-of-Trail Blood Splatters")]
    [Tooltip("Cosmetic ground blood-splatter decal prefabs scattered around the destination point when " +
             "the trail event spawns. One is chosen at random per spawned splatter. Leave empty to disable.")]
    [SerializeField] private GameObject[] _endBloodSplatterPrefabs;

    [Tooltip("Number of blood splatter decals scattered around the destination point.")]
    [SerializeField] private int _endBloodSplatterCount = 8;

    [Tooltip("Radius around the destination point within which splatters are scattered.")]
    [SerializeField] private float _endBloodSplatterRadius = 4f;

    [Tooltip("Y offset above the terrain surface applied to every spawned blood splatter.")]
    [SerializeField] private float _endBloodSplatterGroundOffset = 0.02f;

    [Header("Off-Trail Radiation")]
    [Tooltip("When assigned, the active location's Trail is dynamically registered as a safe " +
             "corridor on this OffTrailRadiation zone for the duration of the event, then removed " +
             "the following day (on the next OnDayStart/Cleanup). Leave null to skip this integration.")]
    [SerializeField] private OffTrailRadiation _offTrailRadiation;

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<float> _networkThreatLevel = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _isDiscovered = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _meetVladActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _followTrailActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// 0 = kill task inactive. N > 0 = kill task active; players must eliminate N enemies.
    /// Decremented by the server via <see cref="DecrementKillMutantCount"/> as each enemy dies.
    /// </summary>
    private readonly NetworkVariable<int> _killMutantCount = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Replicated world positions of the trail's UV-visible blood particles, written by the server
    /// in <see cref="SpawnEvent"/> and mirrored into local <see cref="_spawnedTrailParticles"/>
    /// instances by every peer.
    ///
    /// The particles themselves are deliberately NOT NetworkObjects (<c>Invisible Blood.prefab</c>
    /// carries no NetworkObject — it is pure VFX, and spawning ~dozens of NetworkObjects per trail
    /// would be wasteful), so their existence has to be replicated as data instead. This used to be
    /// a plain <c>ClientRpc</c>, which only ever reaches the clients connected at the moment it is
    /// sent: a player who joined after the trail spawned saw no trail at all under the UV light and
    /// could not complete the task. A server-write NetworkList is delivered in full to late joiners
    /// as part of ordinary spawn synchronization, which an RPC can never guarantee — the same
    /// reasoning behind <c>NetworkedDrawableLine</c>'s stroke list.
    /// </summary>
    private readonly NetworkList<Vector3> _trailParticlePositions = new(
        new List<Vector3>(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Local state ──────────────────────────────────────────────────────────

    private GameObject _spawnedCorpse;
    private GameObject _spawnedDestination;

    /// <summary>Local (non-networked) VFX instances mirroring <see cref="_trailParticlePositions"/>. Exists on every peer.</summary>
    private readonly List<GameObject> _spawnedTrailParticles = new();

    /// <summary>
    /// End-of-trail blood decals spawned by the server. Server-only bookkeeping: these ARE
    /// NetworkObjects, so clients receive and destroy their copies through replication.
    /// </summary>
    private readonly List<NetworkObject> _spawnedBloodSplatters = new();

    /// <summary>Pack mutants currently held frozen at the destination — see <see cref="_packHoldReleaseRadius"/>.</summary>
    private readonly List<MutantEnemy> _heldPackMutants = new();
    private Coroutine _packHoldReleaseCoroutine;

    /// <summary>
    /// Per-mutant death handlers for the currently-spawned pack, keyed by mutant so
    /// <see cref="Cleanup"/> can unsubscribe stragglers if the day ends before the pack is
    /// fully killed. Scopes kill tracking to exactly the mutants THIS threat spawned — see
    /// <see cref="HandlePackMutantRemoved"/> — instead of the global
    /// <see cref="MutantEnemy.OnAnyMutantKilled"/> event, which fires for every mutant in the
    /// world (including the ambient population spawner) and would otherwise over-count.
    /// </summary>
    private readonly Dictionary<MutantEnemy, Action> _packKillHandlers = new();

    /// <summary>Number of currently-tracked pack mutants still alive. Server only.</summary>
    private int _packMutantsRemaining;

    /// <summary>
    /// Server only. True once <see cref="OnPackSpawned"/> has run for the current pack, i.e.
    /// <see cref="_packMutantsRemaining"/> is meaningful. The pack spawns staggered over several
    /// seconds (see <c>MutantSpawner.SpawnPackAtCoroutine</c>), so the destination can be reached
    /// before the callback lands.
    /// </summary>
    private bool _packSpawnComplete;

    /// <summary>
    /// Server only. True once <see cref="ActivateKillTask"/> has published the kill count to all
    /// clients. Kills that land before this (the pack is spawned at trail START, but the HUD task
    /// only activates on destination discovery / first encounter) used to call
    /// <see cref="DecrementKillMutantCount"/> against a count of 0 — clamped away and then
    /// overwritten with the full PackSize, so those kills were permanently lost from the HUD and
    /// the count could never reach 0.
    /// </summary>
    private bool _killTaskActivated;

    /// <summary>
    /// Server only. Set when the pack is wiped out BEFORE the kill task activates, so
    /// <see cref="KillMutantTask.RaiseCompleted"/> can be deferred until after the day script has
    /// had its chance to subscribe (Day_02 subscribes from inside
    /// <see cref="OnDestinationDiscoveredOverride"/>, which runs after <see cref="ActivateKillTask"/>).
    /// Raising it early meant nobody was listening and the night phase never advanced.
    /// </summary>
    private bool _packKillCompletionPending;

    /// <summary>The location that was last passed to SpawnEvent — used at destination discovery time.</summary>
    private FollowTrailLocation _currentLocation;

    /// <summary>
    /// The trail currently registered as a safe corridor on <see cref="_offTrailRadiation"/>, if any.
    /// Tracked separately from <see cref="_currentLocation"/> so <see cref="Cleanup"/> can remove the
    /// exact trail it added, even after <see cref="_currentLocation"/> has been overwritten.
    /// </summary>
    private TrailController _registeredSafeTrail;

    private Coroutine _proximityCoroutine;

    // True once the "Follow the trail" tutorial overlay has ever been shown — guards
    // ShowFollowTrailTutorial so it only ever plays the first time the task becomes active,
    // not on subsequent days that reuse this same threat. Local (non-networked); each client
    // sets its own copy since OnFollowTrailActiveChanged already fires on every peer.
    private bool _hasShownFollowTrailTutorial;

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public string ThreatName        => _threatName;
    public float  ScoreWeight       => 1f;
    public float  ThreatLevel       => _networkThreatLevel.Value;

    public string ThreatDescription => _isDiscovered.Value
        ? "Trail investigated."
        : (_networkThreatLevel.Value > 0 ? string.Empty : "No active trail.");

    // ── IDailyTask ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string DailyTaskId => _dailyTaskId;

    /// <summary>
    /// Activates the follow-trail task as the randomly-selected daily task.
    /// Registers it in the HUD via <see cref="SetFollowTrailTaskActive"/> then spawns the
    /// trail event at a random location. Server-only.
    /// </summary>
    public void TriggerDailyTask()
    {
        if (!IsServer) return;
        SetFollowTrailTaskActive(true);
        TriggerTrailEvent();
        ShiftManager.Instance?.RegisterPendingDailyTask(this);
    }

    /// <inheritdoc/>
    public event Action OnDailyTaskCompleted;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _meetVladActive.OnValueChanged    += OnMeetVladActiveChanged;
        _followTrailActive.OnValueChanged += OnFollowTrailActiveChanged;
        _killMutantCount.OnValueChanged   += OnKillMutantCountChanged;
        _trailParticlePositions.OnListChanged += OnTrailParticlePositionsChanged;

        // Apply initial values for late-joining clients so they see any already-active tasks.
        if (_meetVladActive.Value)         MeetVladOutBackTask.CreateAndRegister();
        if (_followTrailActive.Value)      TaskRegistry.Instance?.AddThreat(this);

        // KillMutantTask.Current is a static that survives leaving/rejoining a session, so an
        // inactive count must actively clear it — otherwise a stale instance from a previous
        // session blocks the next one from ever registering a row.
        if (_killMutantCount.Value > 0)    KillMutantTask.CreateAndRegister(_killMutantCount.Value);
        else                               KillMutantTask.CompleteAndRemove();

        // Build the trail VFX for whatever is already on the list. On the server/host this is
        // empty at spawn time; on a late-joining client it is the full trail of an event that
        // spawned before the client connected, which is exactly the case the old ClientRpc missed.
        RebuildTrailParticles();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        _meetVladActive.OnValueChanged    -= OnMeetVladActiveChanged;
        _followTrailActive.OnValueChanged -= OnFollowTrailActiveChanged;
        _killMutantCount.OnValueChanged   -= OnKillMutantCountChanged;
        _trailParticlePositions.OnListChanged -= OnTrailParticlePositionsChanged;

        // The replicated list is gone, so the local VFX mirroring it must go too — otherwise
        // a client that disconnects mid-event leaves the trail floating in its scene.
        DestroyLocalTrailParticles();

        // Same reasoning for the kill task: its static Current would otherwise outlive this
        // session and keep the next one from registering a fresh row (see KillMutantTask).
        KillMutantTask.CompleteAndRemove();
        _killTaskActivated = false;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── NetworkVariable callbacks — fire on ALL clients ───────────────────────

    private void OnMeetVladActiveChanged(bool previous, bool current)
    {
        if (current) MeetVladOutBackTask.CreateAndRegister();
        else         MeetVladOutBackTask.CompleteAndRemove();
    }

    private void OnFollowTrailActiveChanged(bool previous, bool current)
    {
        if (current)
        {
            // Registering here is the ONLY place this task's HUD/objective-list row gets
            // created — HUDTaskList bridges every TaskRegistry-registered ISystemicThreat into
            // the tutorial objective list generically. Do NOT also call
            // TutorialObjectiveList.AddObjective directly here — that previously created a
            // second, duplicate row for the same task alongside HUDTaskList's row.
            TaskRegistry.Instance?.AddThreat(this);

            // Fires locally on every client (this NetworkVariable callback runs on ALL peers),
            // so the tutorial overlay pops up for everyone the very first time the "Follow the
            // trail" task ever becomes active — never again on subsequent days/triggers.
            if (!_hasShownFollowTrailTutorial)
            {
                _hasShownFollowTrailTutorial = true;
                TutorialOverlay.Instance?.ShowFollowTrailTutorial();
            }
        }
        else
        {
            TaskRegistry.Instance?.RemoveThreat(this);
        }
    }

    private void OnKillMutantCountChanged(int previous, int current)
    {
        // Deliberately NOT branching on `previous` any more: a peer that misses the 0 -> N edge
        // (late joiner, or a TaskRegistry that wasn't ready yet) would take the UpdateCount branch
        // forever and never get a row, making every kill look like it wasn't counted.
        // CreateAndRegister both creates and updates, and is idempotent.
        if (current > 0)
            KillMutantTask.CreateAndRegister(current);
        else
            KillMutantTask.CompleteAndRemove();           // all dead — remove task from HUD
    }

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public void BeginNightPhase() { }
    public void EndNightPhase()   { }

    // ── Public API — task visibility setters (server only) ────────────────────

    /// <summary>Shows or hides the "Meet Vlad out back" HUD task on all clients. Server only.</summary>
    public void SetMeetVladActive(bool active)
    {
        if (!IsServer) return;
        _meetVladActive.Value = active;
    }

    /// <summary>Shows or hides the "Follow the trail" HUD task on all clients. Server only.</summary>
    public void SetFollowTrailTaskActive(bool active)
    {
        if (!IsServer) return;
        _followTrailActive.Value = active;
    }

    /// <summary>
    /// Resets the kill-mutant task on all clients. Pass false to clear it.
    /// To activate with a specific count, use <see cref="SetKillMutantCount"/>.
    /// Server only.
    /// </summary>
    public void SetKillMutantActive(bool active)
    {
        if (!IsServer) return;
        if (!active)
        {
            _killTaskActivated     = false;
            _killMutantCount.Value = 0;
        }
    }

    /// <summary>
    /// Sets the number of enemies players must kill. Activates the kill task on all clients.
    /// Server only.
    /// </summary>
    public void SetKillMutantCount(int count)
    {
        if (!IsServer) return;
        _killMutantCount.Value = Mathf.Max(0, count);
        _killTaskActivated     = _killMutantCount.Value > 0;
    }

    /// <summary>
    /// Republishes the replicated kill count from the server's authoritative
    /// <see cref="_packMutantsRemaining"/>. Preferred over a blind decrement: the two used to be
    /// tracked independently, so any kill the count missed (one landing before the task activated,
    /// or a pack that spawned fewer mutants than PackSize because a prefab was misconfigured) left
    /// the HUD permanently out of step with reality and unable to reach 0.
    /// Server only; no-op until <see cref="ActivateKillTask"/> has run.
    /// </summary>
    private void PublishPackKillCount()
    {
        if (!IsServer || !_killTaskActivated) return;
        _killMutantCount.Value = Mathf.Max(0, _packMutantsRemaining);
    }

    /// <summary>
    /// Decrements the kill count by one. Kept for external callers; internal pack tracking uses
    /// <see cref="PublishPackKillCount"/> instead so the replicated count can never drift from the
    /// server's own remaining-mutant tally. Server only.
    /// </summary>
    public void DecrementKillMutantCount()
    {
        if (!IsServer) return;
        _killMutantCount.Value = Mathf.Max(0, _killMutantCount.Value - 1);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Optional server-side callback injected by a day script to intercept trail resolution.
    /// When assigned, called after the pack is spawned and the kill count is set.
    /// The day script is responsible for advancing the night phase once the kill task completes.
    /// When null, <see cref="FollowTrailThreat"/> advances the night phase automatically after
    /// all enemies are killed (or immediately if no pack is configured).
    /// </summary>
    public Action OnDestinationDiscoveredOverride;

    /// <summary>
    /// Spawns the trail event at a random location. Server only.
    /// </summary>
    public void TriggerTrailEvent()
    {
        if (!IsServer) return;
        Cleanup();
        SpawnEvent();
    }

    /// <summary>
    /// Spawns the trail event at a specific location index. Server only.
    /// </summary>
    public void TriggerTrailEvent(int locationIndex)
    {
        if (!IsServer) return;

        if (_possibleLocations == null || locationIndex < 0 || locationIndex >= _possibleLocations.Count)
        {
            Debug.LogWarning($"[FollowTrailThreat] Location index {locationIndex} out of range — falling back to random.", this);
            TriggerTrailEvent();
            return;
        }

        Cleanup();
        SpawnEvent(_possibleLocations[locationIndex]);
    }

    /// <summary>
    /// Called by TrailDestinationInteractable or automatically via proximity check when a player
    /// reaches the end of the trail. Deactivates the follow task, then delegates to the override
    /// or advances the night phase automatically.
    /// </summary>
    public void OnDestinationDiscovered()
    {
        if (!IsServer)
        {
            OnDestinationDiscoveredServerRpc();
            return;
        }

        if (_isDiscovered.Value) return;

        // Stop the proximity check — destination has been reached.
        if (_proximityCoroutine != null)
        {
            StopCoroutine(_proximityCoroutine);
            _proximityCoroutine = null;
        }

        // Destination reached — no longer need the early-encounter shortcut.
        MutantEnemy.OnAnyMutantSpottedPlayer -= OnPackMutantEncountered;

        _isDiscovered.Value       = true;
        _networkThreatLevel.Value = 0f;
        _followTrailActive.Value  = false;

        // Register the kill task now that the destination has been reached.
        // The pack was physically spawned at trail start; only the HUD task activates here.
        ActivateKillTask();
        int packSize = _currentLocation.PackSize;

        if (OnDestinationDiscoveredOverride != null)
        {
            OnDestinationDiscoveredOverride.Invoke();
        }
        else if (packSize == 0)
        {
            // No enemies configured — advance the night phase immediately.
            BetweenShiftTaskManager.Instance?.HandleNightPhaseReady();
        }
        else
        {
            // Advance the night phase once all enemies are killed.
            KillMutantTask.OnKillMutantTaskCompleted += OnDefaultPackKillComplete;
        }

        // Notify DailyTaskScheduler that this task has been completed so it can
        // unlock the task for future days (when UnlockOnFirstCompletion is enabled).
        // Tasks no longer pay coupons — players are only paid for processing suspects (see SuspectController.PayOutResults).
        // ATM.Instance?.SpawnCoupons(_couponReward);
        OnDailyTaskCompleted?.Invoke();

        // Only now that the day script / default handler is subscribed is it safe to raise a
        // completion that already happened (whole pack killed before the destination was reached).
        FlushPendingPackKillCompletion();

        Debug.Log($"[FollowTrailThreat] Destination discovered. Pack size: {packSize}.");
    }

    [ServerRpc(RequireOwnership = false)]
    private void OnDestinationDiscoveredServerRpc()
    {
        OnDestinationDiscovered();
    }

    private void OnDefaultPackKillComplete()
    {
        KillMutantTask.OnKillMutantTaskCompleted -= OnDefaultPackKillComplete;
        BetweenShiftTaskManager.Instance?.HandleNightPhaseReady();
    }

    // ── Proximity detection ───────────────────────────────────────────────────

    /// <summary>
    /// Server-only coroutine. Polls connected player positions each frame and calls
    /// <see cref="OnDestinationDiscovered"/> as soon as any player enters <see cref="_destinationRadius"/>.
    /// </summary>
    private System.Collections.IEnumerator DestinationProximityRoutine(Transform destination)
    {
        float sqrRadius = _destinationRadius * _destinationRadius;

        while (true)
        {
            Vector3 destPos = destination.position;

            foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            {
                NetworkObject playerObj = client.PlayerObject;
                if (playerObj == null) continue;

                float sqrDist = (playerObj.transform.position - destPos).sqrMagnitude;
                if (sqrDist <= sqrRadius)
                {
                    Debug.Log($"[FollowTrailThreat] Player '{playerObj.name}' reached destination (dist: {Mathf.Sqrt(sqrDist):F1}m). Triggering discovery.", this);
                    OnDestinationDiscovered();
                    yield break;
                }
            }

            yield return null;
        }
    }

    // ── Day start ─────────────────────────────────────────────────────────────

    private void OnDayStart()
    {
        if (!IsServer) return;

        Cleanup();

        _meetVladActive.Value    = false;
        _followTrailActive.Value = false;
        _killMutantCount.Value   = 0;
        _killTaskActivated       = false;

        if (CampaignManager.Instance?.ActiveDay == null || !CampaignManager.Instance.ActiveDay.CanFollowTrailEvent)
        {
            _networkThreatLevel.Value = 0f;
            _isDiscovered.Value       = false;
            return;
        }

        SpawnEvent();
    }

    // ── Spawning ──────────────────────────────────────────────────────────────

    private void SpawnEvent()
    {
        if (_possibleLocations == null || _possibleLocations.Count == 0)
        {
            Debug.LogWarning("[FollowTrailThreat] No possible locations assigned.", this);
            return;
        }

        SpawnEvent(_possibleLocations[UnityEngine.Random.Range(0, _possibleLocations.Count)]);
    }

    private void SpawnEvent(FollowTrailLocation location)
    {
        _currentLocation = location;

        if (_corpsePrefab != null && location.CorpsePoint != null)
        {
            _spawnedCorpse = Instantiate(_corpsePrefab, location.CorpsePoint.position, location.CorpsePoint.rotation);
            _spawnedCorpse.GetComponent<NetworkObject>().Spawn(true);
        }

        if (_destinationPrefab != null && location.DestinationPoint != null)
        {
            _spawnedDestination = Instantiate(_destinationPrefab, location.DestinationPoint.position, location.DestinationPoint.rotation);
            _spawnedDestination.GetComponent<NetworkObject>().Spawn(true);
        }

        SpawnEndBloodSplatters(location.DestinationPoint);

        if (_trailParticlesPrefab == null)
        {
            Debug.LogWarning("[FollowTrailThreat] _trailParticlesPrefab is not assigned — no blood trail will spawn.", this);
        }
        else if (location.Trail == null)
        {
            Debug.LogWarning("[FollowTrailThreat] location.Trail (TrailController) is not assigned — no blood trail will spawn.", this);
        }
        else
        {
            List<Vector3> spawnPositions = location.Trail.GetSpawnPositions();
            for (int i = 0; i < spawnPositions.Count; i++)
                spawnPositions[i] = SnapToTerrain(spawnPositions[i], _trailGroundOffset);
            Debug.Log($"[FollowTrailThreat] Spawning {spawnPositions.Count} trail particles.", this);

            // Publish to the replicated list rather than firing a ClientRpc: every connected peer
            // builds its VFX from the OnListChanged callback, and any client that joins later
            // receives the whole list at spawn time and builds the trail then. See
            // _trailParticlePositions.
            _trailParticlePositions.Clear();
            foreach (Vector3 position in spawnPositions)
                _trailParticlePositions.Add(position);

            // Mark this trail as a temporary safe corridor so players following it don't take
            // off-trail radiation. Removed the following day (or on the next re-trigger) via Cleanup().
            if (_offTrailRadiation != null)
            {
                _offTrailRadiation.AddSafeTrail(location.Trail);
                _registeredSafeTrail = location.Trail;
            }
        }

        // Begin server-side proximity check so the task auto-completes when a player reaches the end.
        if (_proximityCoroutine != null) StopCoroutine(_proximityCoroutine);
        if (location.DestinationPoint != null)
            _proximityCoroutine = StartCoroutine(DestinationProximityRoutine(location.DestinationPoint));

        _networkThreatLevel.Value = 1.0f;
        _isDiscovered.Value       = false;

        // Spawn the enemy pack immediately so mutants are present from the moment the trail starts.
        SpawnPack();

        // If a pack was configured, listen for the first encounter between a PACK mutant (not
        // any ambient mutant anywhere) and a player, so we can skip straight to the
        // kill-mutants task without requiring the trail to be fully followed.
        if (_currentLocation.PackSize > 0)
            MutantEnemy.OnAnyMutantSpottedPlayer += OnPackMutantEncountered;

        Debug.Log("[FollowTrailThreat] Trail event spawned.", this);
    }

    /// <summary>
    /// Physically spawns the enemy pack defined on <see cref="_currentLocation"/> at the destination point.
    /// Does NOT register the kill task — call <see cref="ActivateKillTask"/> for that. Spawned mutants
    /// are held in place (see <see cref="MutantEnemy.SetHeld"/>) until a player comes within
    /// <see cref="_packHoldReleaseRadius"/> of the destination — see <see cref="HoldPackUntilPlayerNear"/>.
    /// Returns the configured PackSize, or 0 if no pack is configured.
    /// </summary>
    private int SpawnPack()
    {
        if (_currentLocation.PackSpawner == null || _currentLocation.PackSize <= 0)
            return 0;

        Transform center = _currentLocation.DestinationPoint;
        Vector3 spawnCenter = center != null ? center.position : transform.position;

        _currentLocation.PackSpawner.SpawnPackAt(spawnCenter, _currentLocation.PackSize,
            onSpawned: OnPackSpawned);

        Debug.Log($"[FollowTrailThreat] Spawned pack of {_currentLocation.PackSize} at {spawnCenter}.", this);
        return _currentLocation.PackSize;
    }

    /// <summary>
    /// Called once every mutant in the freshly-spawned pack exists. Tracks them and starts the
    /// coroutine that releases their hold once a player gets close enough to the destination.
    /// Server only.
    /// </summary>
    private void OnPackSpawned(List<MutantEnemy> mutants)
    {
        _heldPackMutants.Clear();
        _heldPackMutants.AddRange(mutants);

        // Scope kill tracking to exactly these mutants — see _packKillHandlers.
        ClearPackKillTracking();
        _packSpawnComplete = true;

        foreach (MutantEnemy mutant in mutants)
        {
            if (mutant == null) continue;

            // A mutant that already died during the staggered spawn has ALREADY fired
            // OnRemovedFromPlay, so subscribing now would never fire again and the task would hang
            // forever waiting on a corpse. Don't count it as remaining either.
            if (mutant.IsDead)
            {
                Debug.Log($"[FollowTrailThreat] Pack mutant '{mutant.name}' was already dead when the pack finished spawning — not tracked.", this);
                continue;
            }

            MutantEnemy capturedMutant = mutant;
            Action handler = null;
            handler = () => HandlePackMutantRemoved(capturedMutant, handler);

            _packKillHandlers[capturedMutant] = handler;
            capturedMutant.OnRemovedFromPlay += handler;
        }

        _packMutantsRemaining = _packKillHandlers.Count;

        // Keep the HUD in step if the task activated before the pack finished spawning, and resolve
        // the pathological case where the whole pack is already gone by now.
        PublishPackKillCount();
        if (_packMutantsRemaining <= 0)
            CompletePackKills();

        if (_packHoldReleaseCoroutine != null) StopCoroutine(_packHoldReleaseCoroutine);
        _packHoldReleaseCoroutine = StartCoroutine(HoldPackUntilPlayerNear(_currentLocation.DestinationPoint));
    }

    /// <summary>
    /// Called when a tracked pack mutant is removed from play (death or flee-despawn). Both count
    /// as resolved: a flee-despawned mutant is gone from the scene for good, and refusing to count
    /// it (as this used to, via <see cref="MutantEnemy.DiedPermanently"/>) left the objective stuck
    /// on a mutant that no longer existed, so it could never complete. Mutants NOT spawned by this
    /// pack (e.g. the ambient world population spawner) are never tracked here in the first place.
    /// Server only.
    /// </summary>
    private void HandlePackMutantRemoved(MutantEnemy mutant, Action handler)
    {
        if (mutant != null && handler != null)
            mutant.OnRemovedFromPlay -= handler;

        // Only a mutant this pack was actually tracking may decrement the count — guards against a
        // duplicate invocation double-counting one kill.
        bool wasTracked = mutant != null && _packKillHandlers.Remove(mutant);
        if (!IsServer || !wasTracked) return;

        if (mutant != null && !mutant.DiedPermanently)
            Debug.Log($"[FollowTrailThreat] Pack mutant '{mutant.name}' left play by fleeing — counting it as resolved so the kill task can complete.", this);

        _packMutantsRemaining = Mathf.Max(0, _packMutantsRemaining - 1);
        PublishPackKillCount();

        if (_packMutantsRemaining <= 0)
            CompletePackKills();
    }

    /// <summary>
    /// Raises <see cref="KillMutantTask.RaiseCompleted"/>, or defers it when the kill task hasn't
    /// been activated yet — see <see cref="_packKillCompletionPending"/>. Server only.
    /// </summary>
    private void CompletePackKills()
    {
        if (!IsServer) return;

        if (!_killTaskActivated)
        {
            _packKillCompletionPending = true;
            Debug.Log("[FollowTrailThreat] Whole pack was wiped out before the kill task activated — deferring completion until listeners are subscribed.", this);
            return;
        }

        _packKillCompletionPending = false;
        KillMutantTask.RaiseCompleted();
    }

    /// <summary>
    /// Raises a completion that was deferred by <see cref="CompletePackKills"/>. Called at the end
    /// of destination discovery, i.e. after the day script (or this threat's own default handler)
    /// has subscribed to <see cref="KillMutantTask.OnKillMutantTaskCompleted"/>. Server only.
    /// </summary>
    private void FlushPendingPackKillCompletion()
    {
        if (!IsServer || !_packKillCompletionPending) return;

        _packKillCompletionPending = false;
        KillMutantTask.RaiseCompleted();
    }

    /// <summary>Unsubscribes every still-pending pack kill handler. Safe to call repeatedly.</summary>
    private void ClearPackKillTracking()
    {
        foreach (var pair in _packKillHandlers)
        {
            if (pair.Key != null)
                pair.Key.OnRemovedFromPlay -= pair.Value;
        }
        _packKillHandlers.Clear();
        _packMutantsRemaining      = 0;
        _packSpawnComplete         = false;
        _packKillCompletionPending = false;
    }

    /// <summary>
    /// Waits until any connected player is within <see cref="_packHoldReleaseRadius"/> of
    /// <paramref name="destination"/>, then releases every held pack mutant so they resume normal
    /// AI (patrol/aggro/chase). No-ops (releases immediately) if the destination is unassigned.
    /// Server only.
    /// </summary>
    private IEnumerator HoldPackUntilPlayerNear(Transform destination)
    {
        if (destination == null)
        {
            ReleaseHeldPack();
            yield break;
        }

        float sqrRadius = _packHoldReleaseRadius * _packHoldReleaseRadius;

        while (true)
        {
            Vector3 destPos = destination.position;
            bool playerNear = false;

            foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            {
                NetworkObject playerObj = client.PlayerObject;
                if (playerObj == null) continue;

                if ((playerObj.transform.position - destPos).sqrMagnitude <= sqrRadius)
                {
                    playerNear = true;
                    break;
                }
            }

            if (playerNear) break;

            yield return null;
        }

        ReleaseHeldPack();
    }

    /// <summary>Releases every currently-held pack mutant and clears tracking state. Server only.</summary>
    private void ReleaseHeldPack()
    {
        foreach (MutantEnemy mutant in _heldPackMutants)
        {
            if (mutant != null) mutant.SetHeld(false);
        }

        _heldPackMutants.Clear();
        _packHoldReleaseCoroutine = null;
    }

    /// <summary>
    /// Activates the kill-mutant task on all clients by setting <see cref="_killMutantCount"/>.
    /// Called once the destination is discovered so the HUD task registers at trail completion.
    ///
    /// Publishes the server's live <see cref="_packMutantsRemaining"/> rather than the configured
    /// PackSize whenever the pack has finished spawning, so mutants killed between trail start and
    /// destination discovery are already accounted for instead of being resurrected in the HUD.
    /// Server only.
    /// </summary>
    private void ActivateKillTask()
    {
        if (!IsServer || _currentLocation.PackSize <= 0) return;

        _killTaskActivated = true;
        _killMutantCount.Value = _packSpawnComplete
            ? Mathf.Max(0, _packMutantsRemaining)
            : _currentLocation.PackSize;
    }

    /// <summary>
    /// Server-only. Called when any mutant first spots a player while the follow-trail task is
    /// still active. Only reacts if <paramref name="spotter"/> is one of THIS pack's mutants —
    /// see <see cref="_packKillHandlers"/> — so an unrelated ambient mutant elsewhere on the map
    /// spotting a player never short-circuits the trail task. Ends the trail task immediately
    /// and activates the kill-mutants task, exactly as if the destination had been reached — but
    /// without the coupon reward since the destination itself was never investigated.
    /// </summary>
    private void OnPackMutantEncountered(MutantEnemy spotter)
    {
        if (!IsServer || _isDiscovered.Value || !_followTrailActive.Value) return;
        if (spotter == null || !_packKillHandlers.ContainsKey(spotter)) return;

        // Unsubscribe immediately — only the first encounter matters.
        MutantEnemy.OnAnyMutantSpottedPlayer -= OnPackMutantEncountered;

        // Stop the proximity check — we're skipping the destination entirely.
        if (_proximityCoroutine != null)
        {
            StopCoroutine(_proximityCoroutine);
            _proximityCoroutine = null;
        }

        _isDiscovered.Value       = true;
        _networkThreatLevel.Value = 0f;
        _followTrailActive.Value  = false;

        ActivateKillTask();

        if (OnDestinationDiscoveredOverride != null)
        {
            OnDestinationDiscoveredOverride.Invoke();
        }
        else if (_currentLocation.PackSize > 0)
        {
            KillMutantTask.OnKillMutantTaskCompleted += OnDefaultPackKillComplete;
        }
        else
        {
            BetweenShiftTaskManager.Instance?.HandleNightPhaseReady();
        }

        // Mark the daily task complete so DailyTaskScheduler can unlock it for future days.
        OnDailyTaskCompleted?.Invoke();

        // See OnDestinationDiscovered — raise any completion that landed before listeners existed.
        FlushPendingPackKillCompletion();

        Debug.Log("[FollowTrailThreat] Pack mutant encountered before destination — skipping to kill-mutants task.", this);
    }

    private void Cleanup()
    {
        if (_proximityCoroutine != null)
        {
            StopCoroutine(_proximityCoroutine);
            _proximityCoroutine = null;
        }

        // Stop watching for a player to approach and release any still-held pack mutants so
        // they don't stay frozen forever if the day ends before the player reaches the trail.
        if (_packHoldReleaseCoroutine != null)
        {
            StopCoroutine(_packHoldReleaseCoroutine);
            _packHoldReleaseCoroutine = null;
        }
        ReleaseHeldPack();

        // Unsubscribe any still-pending pack kill handlers so mutants that outlive the event
        // (day ends before the pack is fully killed) don't leak stale delegates.
        ClearPackKillTracking();
        _killTaskActivated = false;

        // Always clear the encounter listener — it may have been subscribed by SpawnEvent.
        MutantEnemy.OnAnyMutantSpottedPlayer -= OnPackMutantEncountered;

        // Un-register yesterday's trail as a safe corridor now that its event window has ended.
        if (_offTrailRadiation != null && _registeredSafeTrail != null)
        {
            _offTrailRadiation.RemoveSafeTrail(_registeredSafeTrail);
            _registeredSafeTrail = null;
        }

        // Reset discovery state so the next SpawnEvent can be discovered normally.
        if (IsServer)
        {
            _isDiscovered.Value       = false;
            _networkThreatLevel.Value = 0f;

            // Drop any leftover kill count so the previous event's row can't linger into the next
            // one (Cleanup runs at the head of every TriggerTrailEvent, not just at day start).
            _killMutantCount.Value    = 0;
        }

        if (_spawnedCorpse != null)
        {
            _spawnedCorpse.GetComponent<NetworkObject>().Despawn(true);
            _spawnedCorpse = null;
        }

        if (_spawnedDestination != null)
        {
            _spawnedDestination.GetComponent<NetworkObject>().Despawn(true);
            _spawnedDestination = null;
        }

        // Trail particles are local VFX mirroring _trailParticlePositions — clearing the replicated
        // list tears them down on every peer (including anyone who joins afterward, who now receives
        // an empty list instead of a stale trail). The end-of-trail splatters are real spawned
        // NetworkObjects, so despawning them on the server removes each client's copy too.
        if (IsServer)
        {
            if (IsSpawned) _trailParticlePositions.Clear();

            foreach (NetworkObject splatter in _spawnedBloodSplatters)
                if (splatter != null && splatter.IsSpawned) splatter.Despawn(true);

            _spawnedBloodSplatters.Clear();
        }

        // A clear on an unspawned list raises no callback, and a client running Cleanup locally never
        // writes to the list at all — so tear the local VFX down directly in both cases.
        if (!IsServer || !IsSpawned)
            DestroyLocalTrailParticles();
    }

    private Vector3 SnapToTerrain(Vector3 position, float yOffset)
    {
        const float castOriginHeight = 50f;
        const float castDistance     = 100f;

        Vector3 origin = new Vector3(position.x, position.y + castOriginHeight, position.z);

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, castDistance, _terrainLayerMask))
            return hit.point + Vector3.up * yOffset;

        return new Vector3(position.x, position.y + yOffset, position.z);
    }

    /// <summary>
    /// Scatters <see cref="_endBloodSplatterCount"/> cosmetic ground blood-splatter decals within
    /// <see cref="_endBloodSplatterRadius"/> of <paramref name="destination"/>, snapped to terrain.
    /// No-op if no prefabs are assigned or the destination is unassigned. Server only.
    ///
    /// The splatter prefabs carry a NetworkObject (they are the same decals
    /// <see cref="TakeOutTrashTask"/> spawns), so they are spawned server-side and replicated like
    /// the corpse and destination props above. They used to be plain-<c>Instantiate</c>d on every
    /// peer from a ClientRpc, which produced an *unspawned* NetworkObject on each client — inert as
    /// far as Netcode is concerned, invisible to late joiners, and a second, conflicting authority
    /// path for a prefab the server already knows how to spawn.
    /// </summary>
    private void SpawnEndBloodSplatters(Transform destination)
    {
        if (!IsServer) return;
        if (_endBloodSplatterPrefabs == null || _endBloodSplatterPrefabs.Length == 0) return;
        if (destination == null || _endBloodSplatterCount <= 0) return;

        for (int i = 0; i < _endBloodSplatterCount; i++)
        {
            GameObject prefab = _endBloodSplatterPrefabs[UnityEngine.Random.Range(0, _endBloodSplatterPrefabs.Length)];
            if (prefab == null) continue;

            Vector2 offset2D = UnityEngine.Random.insideUnitCircle * _endBloodSplatterRadius;
            Vector3 rawPos   = destination.position + new Vector3(offset2D.x, 0f, offset2D.y);
            Vector3 position = SnapToTerrain(rawPos, _endBloodSplatterGroundOffset);
            Quaternion rot   = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

            GameObject splatter = Instantiate(prefab, position, rot);

            if (!splatter.TryGetComponent(out NetworkObject netObj))
            {
                Debug.LogWarning($"[FollowTrailThreat] End-of-trail splatter prefab '{prefab.name}' has no " +
                                 "NetworkObject — it cannot be replicated. Destroying the local instance.", this);
                Destroy(splatter);
                continue;
            }

            netObj.Spawn(true);
            _spawnedBloodSplatters.Add(netObj);
        }
    }

    // ── Trail particle replication ────────────────────────────────────────────

    /// <summary>
    /// Mirrors <see cref="_trailParticlePositions"/> into local VFX instances. Runs on every peer.
    /// Appends for a single Add so the common case (the server writing one position per entry) stays
    /// linear, and falls back to a full rebuild for bulk/structural changes.
    /// </summary>
    private void OnTrailParticlePositionsChanged(NetworkListEvent<Vector3> changeEvent)
    {
        switch (changeEvent.Type)
        {
            case NetworkListEvent<Vector3>.EventType.Add:
                SpawnLocalTrailParticle(changeEvent.Value);
                break;

            case NetworkListEvent<Vector3>.EventType.Clear:
                DestroyLocalTrailParticles();
                break;

            default:
                RebuildTrailParticles();
                break;
        }
    }

    private void RebuildTrailParticles()
    {
        DestroyLocalTrailParticles();

        foreach (Vector3 position in _trailParticlePositions)
            SpawnLocalTrailParticle(position);
    }

    private void SpawnLocalTrailParticle(Vector3 position)
    {
        if (_trailParticlesPrefab == null) return;

        _spawnedTrailParticles.Add(Instantiate(_trailParticlesPrefab, position, Quaternion.identity));
    }

    private void DestroyLocalTrailParticles()
    {
        foreach (GameObject trail in _spawnedTrailParticles)
            if (trail != null) Destroy(trail);

        _spawnedTrailParticles.Clear();
    }
}
