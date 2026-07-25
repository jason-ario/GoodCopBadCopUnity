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
    [SerializeField] private string _threatName = "Follow the Trail";
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

    // ── Local state ──────────────────────────────────────────────────────────

    private GameObject _spawnedCorpse;
    private GameObject _spawnedDestination;
    private readonly List<GameObject> _spawnedTrailParticles = new();

    /// <summary>The location that was last passed to SpawnEvent — used at destination discovery time.</summary>
    private FollowTrailLocation _currentLocation;

    /// <summary>
    /// The trail currently registered as a safe corridor on <see cref="_offTrailRadiation"/>, if any.
    /// Tracked separately from <see cref="_currentLocation"/> so <see cref="Cleanup"/> can remove the
    /// exact trail it added, even after <see cref="_currentLocation"/> has been overwritten.
    /// </summary>
    private TrailController _registeredSafeTrail;

    private Coroutine _proximityCoroutine;

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public string ThreatName        => _threatName;
    public float  ScoreWeight       => 1f;
    public float  ThreatLevel       => _networkThreatLevel.Value;

    public string ThreatDescription => _isDiscovered.Value
        ? "Trail investigated."
        : (_networkThreatLevel.Value > 0 ? "Grotesque corpse found. Follow the residue." : "No active trail.");

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

        // Apply initial values for late-joining clients so they see any already-active tasks.
        if (_meetVladActive.Value)         MeetVladOutBackTask.CreateAndRegister();
        if (_followTrailActive.Value)      TaskRegistry.Instance?.AddThreat(this);
        if (_killMutantCount.Value > 0)    KillMutantTask.CreateAndRegister(_killMutantCount.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        _meetVladActive.OnValueChanged    -= OnMeetVladActiveChanged;
        _followTrailActive.OnValueChanged -= OnFollowTrailActiveChanged;
        _killMutantCount.OnValueChanged   -= OnKillMutantCountChanged;
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
        if (current) TaskRegistry.Instance?.AddThreat(this);
        else         TaskRegistry.Instance?.RemoveThreat(this);
    }

    private void OnKillMutantCountChanged(int previous, int current)
    {
        if (current > 0 && previous == 0)
            KillMutantTask.CreateAndRegister(current);   // task becomes active
        else if (current > 0)
            KillMutantTask.UpdateCount(current);          // mid-combat count update for description
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
        if (!active) _killMutantCount.Value = 0;
    }

    /// <summary>
    /// Sets the number of enemies players must kill. Activates the kill task on all clients.
    /// Server only.
    /// </summary>
    public void SetKillMutantCount(int count)
    {
        if (!IsServer) return;
        _killMutantCount.Value = Mathf.Max(0, count);
    }

    /// <summary>
    /// Decrements the kill count by one. Called by <see cref="KillMutantTask"/> on the server
    /// each time an enemy is killed. When the count reaches zero the kill task is removed on
    /// all clients automatically via the NetworkVariable callback.
    /// Server only.
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
        ATM.Instance?.SpawnCoupons(_couponReward);
        OnDailyTaskCompleted?.Invoke();

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
            SpawnTrailParticlesClientRpc(spawnPositions.ToArray());

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

        // If a pack was configured, listen for the first player encounter so we can skip
        // straight to the kill-mutants task without requiring the trail to be fully followed.
        if (_currentLocation.PackSize > 0)
            MutantEnemy.OnAnyMutantSpottedPlayer += OnPackMutantEncountered;

        Debug.Log("[FollowTrailThreat] Trail event spawned.", this);
    }

    /// <summary>
    /// Physically spawns the enemy pack defined on <see cref="_currentLocation"/> at the destination point.
    /// Does NOT register the kill task — call <see cref="ActivateKillTask"/> for that.
    /// Returns the configured PackSize, or 0 if no pack is configured.
    /// </summary>
    private int SpawnPack()
    {
        if (_currentLocation.PackSpawner == null || _currentLocation.PackSize <= 0)
            return 0;

        Transform center = _currentLocation.DestinationPoint;
        Vector3 spawnCenter = center != null ? center.position : transform.position;

        _currentLocation.PackSpawner.SpawnPackAt(spawnCenter, _currentLocation.PackSize);

        Debug.Log($"[FollowTrailThreat] Spawned pack of {_currentLocation.PackSize} at {spawnCenter}.", this);
        return _currentLocation.PackSize;
    }

    /// <summary>
    /// Activates the kill-mutant task on all clients by setting <see cref="_killMutantCount"/>.
    /// Called once the destination is discovered so the HUD task registers at trail completion.
    /// Server only.
    /// </summary>
    private void ActivateKillTask()
    {
        if (_currentLocation.PackSize <= 0) return;
        _killMutantCount.Value = _currentLocation.PackSize;
    }

    /// <summary>
    /// Server-only. Called when any mutant first spots a player while the follow-trail task is
    /// still active. Ends the trail task immediately and activates the kill-mutants task, exactly
    /// as if the destination had been reached — but without the coupon reward since the destination
    /// itself was never investigated.
    /// </summary>
    private void OnPackMutantEncountered()
    {
        if (!IsServer || _isDiscovered.Value || !_followTrailActive.Value) return;

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

        Debug.Log("[FollowTrailThreat] Pack mutant encountered before destination — skipping to kill-mutants task.", this);
    }

    private void Cleanup()
    {
        if (_proximityCoroutine != null)
        {
            StopCoroutine(_proximityCoroutine);
            _proximityCoroutine = null;
        }

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

        // Trail particles are local instances on each client — clean up via ClientRpc.
        if (IsSpawned)
            CleanupTrailParticlesClientRpc();
        else
        {
            foreach (GameObject trail in _spawnedTrailParticles)
                if (trail != null) Destroy(trail);
            _spawnedTrailParticles.Clear();
        }
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

    [ClientRpc]
    private void SpawnTrailParticlesClientRpc(Vector3[] positions)
    {
        foreach (Vector3 pos in positions)
        {
            GameObject trail = Instantiate(_trailParticlesPrefab, pos, Quaternion.identity);
            _spawnedTrailParticles.Add(trail);
        }
    }

    [ClientRpc]
    private void CleanupTrailParticlesClientRpc()
    {
        foreach (GameObject trail in _spawnedTrailParticles)
            if (trail != null) Destroy(trail);
        _spawnedTrailParticles.Clear();
    }
}
