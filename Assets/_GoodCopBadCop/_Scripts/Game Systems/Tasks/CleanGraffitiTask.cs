using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Daily graffiti-cleaning task. Day 2+ adds a fresh batch on every <see cref="ShiftManager.OnDayStart"/>
/// on top of any unscrubbed leftovers — graffiti piles up across days (see <see cref="OnDayStart"/>);
/// Day 1 pre-spawns via <see cref="SpawnGraffitiEarly"/>. A later
/// <see cref="TriggerDailyTask"/> (from <see cref="DailyTaskScheduler"/> or day scripts) reuses any
/// pieces still on the walls. Placements + scrub progress are saved in the day-start checkpoint.
///
/// Implements both <see cref="ISystemicThreat"/> (HUD / performance scoring) and
/// <see cref="IDailyTask"/> (compatible with <see cref="DailyTaskScheduler"/>).
///
/// Scene setup:
///   - Add a NetworkObject component to this GameObject.
///   - Assign <see cref="_graffitiPrefabs"/>: one or more prefabs, each a registered Network Prefab.
///   - Assign <see cref="_spawnPoints"/>: Transforms placed on the checkpoint walls.
///   - Set <see cref="_minGraffitiCount"/> / <see cref="_maxGraffitiCount"/> for difficulty range.
///   - Add this task to <see cref="DailyTaskScheduler"/>'s pool in the Inspector.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class CleanGraffitiTask : NetworkBehaviour, ISystemicThreat, IDailyTask
{
    public static CleanGraffitiTask Instance { get; private set; }

    [Header("Task Properties")]
    [SerializeField] private string _taskName     = "Clean Graffiti";
    [SerializeField] private int    _couponReward  = 10;

    [Header("Daily Task")]
    [Tooltip("Stable identifier used by DailyTaskScheduler and SaveDataManager. Must match the TaskId entry in DailyTaskScheduler's pool.")]
    [SerializeField] private string _dailyTaskId = "CleanGraffiti";

    [Header("Item Success Feedback")]
    [Tooltip("2D success cue played on every client each time a scrubbed graffiti piece advances this " +
             "task (same cue as a task row's sub-task progress ding). Deduplicated via TaskSuccessCue.")]
    [SerializeField] private AudioClip _itemSuccessSfxClip;
    [Tooltip("Volume for _itemSuccessSfxClip.")]
    [SerializeField] private float _itemSuccessSfxVolume = 0.6f;

    [Header("Spawning")]
    [Tooltip("Minimum number of graffiti pieces to spawn when triggered (inclusive).")]
    [SerializeField] private int _minGraffitiCount = 2;
    [Tooltip("Maximum number of graffiti pieces to spawn when triggered (inclusive).")]
    [SerializeField] private int _maxGraffitiCount = 6;
    [Tooltip("Pool of graffiti prefabs to pick from at random. Each must be a registered Network Prefab.")]
    [SerializeField] private GameObject[] _graffitiPrefabs;
    [Tooltip("Transforms on the checkpoint walls where graffiti can appear. A point is picked at random for each piece.")]
    [SerializeField] private Transform[]  _spawnPoints;

    [Header("Day Start (Day 2+)")]
    [Tooltip("Every day after Day 1, add a fresh batch of graffiti on free spawn points the moment the " +
             "day starts (unscrubbed pieces from earlier days stay — graffiti piles up) and activate " +
             "the task (compass pips + Checkpoint Integrity), without blocking clock-out. " +
             "The pieces are captured by the day-start checkpoint save, so unscrubbed graffiti is " +
             "rebuilt on load. Day 1 is excluded — Day_01 pre-spawns its own via SpawnGraffitiEarly.")]
    [SerializeField] private bool _spawnOnDayStart = true;
    [Tooltip("Scales the min/max graffiti range for the Day 2+ day-start batch (0.5 = half). " +
             "Day 1 (SpawnGraffitiEarly / TriggerDailyTask) always uses the full range.")]
    [Range(0f, 1f)]
    [SerializeField] private float _dayStartCountMultiplier = 0.5f;

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<int> _scrubbed = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Total graffiti pieces spawned for this task cycle.</summary>
    private readonly NetworkVariable<int> _totalCount = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Whether this task is currently active and should appear in the HUD task list.
    /// Drives TaskRegistry registration on all clients, including late joiners.
    /// </summary>
    private readonly NetworkVariable<bool> _isActive = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Whether this task is currently active on every client (mirrors <see cref="_isActive"/>).
    /// Used by <see cref="CompassController"/> to gate the compass graffiti pips — an unscrubbed
    /// piece should only show on the compass while this task is actually asking the player to
    /// clean it (e.g. not during <see cref="SpawnGraffitiEarly"/>'s pre-spawn window).
    /// </summary>
    public bool IsActive => _isActive.Value;

    // ── Local state ───────────────────────────────────────────────────────────

    private readonly List<NetworkObject> _spawnedGraffiti = new();
    private readonly Dictionary<NetworkObject, GraffitiPlacementSaveData> _graffitiPlacements = new();
    private bool _isComplete;

    /// <summary>
    /// Server-only. Campaign day whose day-start handling (carry-over + fresh batch) has already
    /// run, or whose graffiti was rebuilt from a save. <see cref="ShiftManager.OnDayStart"/> can fire
    /// more than once per day across transition paths; without this a second firing would wipe and
    /// re-roll graffiti the player has already seen (or that a save load just restored).
    /// </summary>
    private int _dayStartHandledForDay = -1;

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    /// <summary>
    /// Set by a day script (e.g. Day_01, in <c>DayActivated</c>/<c>DayDeactivated</c>) for the
    /// entire duration it will show its OWN hand-scripted TutorialObjectiveList row for this
    /// task — e.g. Day 1's tutorial-choreographed graffiti objective added in
    /// <c>Day_01.OnTrashTaskReadySync</c>. Set it well before <see cref="TriggerDailyTask"/> can
    /// possibly run (activation time, not trigger time) so <see cref="HUDTaskList"/> never has
    /// a chance to add its own generic row first — that race would leave a stale duplicate row
    /// behind even after this flag is later set. While true, HUDTaskList skips this threat
    /// entirely (same pattern as <c>ProcessResidentsTask</c>'s exclusion for the automatic
    /// subject counter). Days that don't hand-manage this task (e.g. Day 2+) leave this false
    /// and get the task's row purely from the generic HUDTaskList/TaskRegistry bridge.
    /// </summary>
    public bool HasCustomTutorialRow { get; set; }

    public string ThreatName  => _taskName;
    public float  ScoreWeight => 1f;

    public float ThreatLevel => _totalCount.Value > 0
        ? 1f - Mathf.Clamp01((float)_scrubbed.Value / _totalCount.Value)
        : 0f;

    /// <summary>Dynamic description reflects current scrub progress.</summary>
    public string ThreatDescription =>
        _isComplete
            ? $"All {_totalCount.Value} pieces scrubbed!"
            : _totalCount.Value > 0
                ? $"Scrub graffiti: {_scrubbed.Value}/{_totalCount.Value}"
                : string.Empty;

    /// <summary>
    /// No-op — graffiti is exclusively spawned at day start via <see cref="TriggerDailyTask"/>.
    /// If graffiti was triggered that day it is already in place; the HUD entry stays
    /// visible through the night phase via <see cref="OnTaskListChanged"/>.
    /// </summary>
    public void BeginNightPhase() { }

    /// <summary>No-op.</summary>
    public void EndNightPhase() { }

    // ── IDailyTask ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string DailyTaskId => _dailyTaskId;

    /// <inheritdoc/>
    public event Action OnDailyTaskCompleted;

    /// <summary>
    /// Spawns a random number of graffiti pieces and activates the task — shows the HUD threat
    /// entry and registers as a pending daily task with <see cref="ShiftManager"/>. Despawns any
    /// leftovers from a previous cycle first, unless <see cref="SpawnGraffitiEarly"/> already
    /// pre-spawned graffiti for this cycle, in which case those pieces are reused instead of
    /// spawning a duplicate set.
    /// Server-only; safe to call from <see cref="DailyTaskScheduler"/> or day scripts.
    /// </summary>
    public void TriggerDailyTask()
    {
        if (!IsServer) return;

        _isComplete = false;

        // Drop pieces already scrubbed (and despawned) so only graffiti still on the walls counts
        // as "already spawned this cycle".
        PruneDespawnedGraffiti();

        if (_spawnedGraffiti.Count > 0)
        {
            // Graffiti was already placed this cycle (Day 1's SpawnGraffitiEarly, or the Day 2+
            // day-start spawn) — reuse it instead of despawning and re-rolling. Keep the existing
            // scrubbed count: zeroing it while scrubbed pieces are already gone would leave the
            // task permanently short (e.g. 2/3 with nothing left to scrub).
            Debug.Log($"[CleanGraffitiTask] TriggerDailyTask — reusing {_spawnedGraffiti.Count} " +
                      "already-spawned graffiti piece(s).");
        }
        else
        {
            _scrubbed.Value = 0;
            DespawnExistingGraffiti();

            int count = RollGraffitiCount();

            int spawnedCount = SpawnGraffiti(count);
            _totalCount.Value = spawnedCount;

            Debug.Log($"[CleanGraffitiTask] TriggerDailyTask — spawning {spawnedCount} graffiti piece(s).");
        }

        // Flip the active flag — OnIsActiveChanged fires on all clients (and late joiners
        // read the initial value in OnNetworkSpawn) to register this task in TaskRegistry.
        _isActive.Value = true;

        // See CleanupTaskGating — only mandatory (Day 1) graffiti blocks clock-out. Day 2+ runs
        // still spawn, still show on the compass, and still feed Checkpoint Integrity, but no
        // longer gate the timecard machine.
        if (CleanupTaskGating.IsMandatoryDay)
            ShiftManager.Instance?.RegisterPendingDailyTask(this);
    }

    /// <summary>
    /// Day-1-only helper: spawns graffiti immediately (e.g. right at game/day start) purely for
    /// visual presence, WITHOUT activating the task — no HUD threat entry, no
    /// <see cref="ShiftManager"/> registration, no tutorial objective. This lets the player see
    /// the graffiti on the checkpoint walls well before they're actually given the "clean
    /// graffiti" task at end of shift. A later call to <see cref="TriggerDailyTask"/> in the same
    /// day cycle detects the pre-spawned pieces and reuses them instead of spawning a duplicate
    /// set. Server-only. No-op if graffiti has already been spawned this cycle.
    /// </summary>
    public void SpawnGraffitiEarly()
    {
        if (!IsServer) return;

        PruneDespawnedGraffiti();

        if (_spawnedGraffiti.Count > 0)
        {
            Debug.LogWarning("[CleanGraffitiTask] SpawnGraffitiEarly: graffiti already spawned this cycle — skipping.");
            return;
        }

        int count = RollGraffitiCount();

        int spawnedCount = SpawnGraffiti(count);
        _totalCount.Value = spawnedCount;

        Debug.Log($"[CleanGraffitiTask] SpawnGraffitiEarly — pre-spawned {spawnedCount} graffiti " +
                  "piece(s) for early visibility; task remains inactive until TriggerDailyTask.");
    }

    /// <summary>
    /// Rolls a graffiti spawn count between <see cref="_minGraffitiCount"/> and
    /// <see cref="_maxGraffitiCount"/>, halved (minimum 1) when only a single player is
    /// connected — the full range is tuned for 2 players and is excessive solo.
    /// <paramref name="rangeScale"/> further scales the range (min floored, max ceiled, minimum 1).
    /// </summary>
    private int RollGraffitiCount(float rangeScale = 1f)
    {
        bool isSinglePlayer = NetworkManager.Singleton == null
            || DevSpectatorRegistry.PlayerClientCount(NetworkManager.Singleton) <= 1;

        int minCount = _minGraffitiCount;
        int maxCount = _maxGraffitiCount;

        if (isSinglePlayer)
        {
            minCount = Mathf.Max(1, minCount / 2);
            maxCount = Mathf.Max(minCount, maxCount / 2);
        }

        if (rangeScale < 1f)
        {
            minCount = Mathf.Max(1, Mathf.FloorToInt(minCount * rangeScale));
            maxCount = Mathf.Max(minCount, Mathf.CeilToInt(maxCount * rangeScale));
        }

        return Random.Range(minCount, maxCount + 1);
    }

    /// <summary>Captures the active graffiti world objects and their partial scrub progress.</summary>
    public GraffitiTaskSaveState CaptureSaveState()
    {
        var placements = new List<GraffitiPlacementSaveData>();
        foreach (NetworkObject netObj in _spawnedGraffiti)
        {
            if (netObj == null || !netObj.IsSpawned || !_graffitiPlacements.TryGetValue(netObj, out GraffitiPlacementSaveData placement))
                continue;

            GraffitiInteractable interactable = netObj.GetComponent<GraffitiInteractable>();
            placements.Add(new GraffitiPlacementSaveData
            {
                PrefabIndex = placement.PrefabIndex,
                SpawnPointIndex = placement.SpawnPointIndex,
                ScrubProgress = interactable != null ? interactable.ScrubProgress : 0f
            });
        }

        return new GraffitiTaskSaveState
        {
            IsActive = _isActive.Value,
            IsComplete = _isComplete,
            ScrubbedCount = _scrubbed.Value,
            TotalCount = _totalCount.Value,
            Placements = placements.ToArray()
        };
    }

    /// <summary>Rebuilds saved graffiti on the host before NGO replicates the objects to clients.</summary>
    public void RestoreSaveState(GraffitiTaskSaveState state)
    {
        if (!IsServer || state == null) return;

        DespawnExistingGraffiti();
        _scrubbed.Value = Mathf.Max(0, state.ScrubbedCount);
        _totalCount.Value = Mathf.Max(_scrubbed.Value, state.TotalCount);
        _isComplete = state.IsComplete;

        foreach (GraffitiPlacementSaveData placement in state.Placements ?? Array.Empty<GraffitiPlacementSaveData>())
            SpawnSavedGraffiti(placement);

        // The saved set IS this day's graffiti — a later OnDayStart firing for the same day must
        // not wipe it and roll a new one.
        _dayStartHandledForDay = CurrentCampaignDay;

        _isActive.Value = state.IsActive && !_isComplete;
        if (_isActive.Value && CleanupTaskGating.IsMandatoryDay)
            ShiftManager.Instance?.RegisterPendingDailyTask(this);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CleanGraffitiTask] Duplicate instance detected — destroying self.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _scrubbed.OnValueChanged   += OnScrubbedChanged;
        _totalCount.OnValueChanged += OnTotalCountChanged;
        _isActive.OnValueChanged   += OnIsActiveChanged;

        // Handle the initial value for late-joining clients.
        // Note: this only registers the HUD threat entry — showing/managing a
        // TutorialObjectiveList row for this task is the caller's responsibility
        // (see Day_01/Day_02), since each day script controls exactly when the
        // graffiti objective should first become visible to the player.
        if (_isActive.Value)
            TaskRegistry.Instance?.AddThreat(this);

        // Re-register whenever SetThreats clears the registry (e.g. at night-phase start),
        // ensuring graffiti stays visible in the HUD throughout the night phase.
        TaskRegistry.OnTaskListChanged += OnTaskListChanged;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDayStart;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _scrubbed.OnValueChanged   -= OnScrubbedChanged;
        _totalCount.OnValueChanged -= OnTotalCountChanged;
        _isActive.OnValueChanged   -= OnIsActiveChanged;

        TaskRegistry.OnTaskListChanged -= OnTaskListChanged;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        TaskRegistry.OnTaskListChanged -= OnTaskListChanged;
        OnDailyTaskCompleted = null;
        OnProgressChanged    = null;
    }

    // ── Scrub callback (called by GraffitiInteractable on the server) ─────────

    /// <summary>
    /// Called by <see cref="GraffitiInteractable"/> on the server once a piece has been
    /// fully scrubbed. Increments the progress counter and completes the task when
    /// all pieces are done.
    /// </summary>
    public void OnGraffitiScrubbed()
    {
        if (!IsServer || _isComplete) return;

        int previousScrubbed = _scrubbed.Value;
        _scrubbed.Value = Mathf.Clamp(_scrubbed.Value + 1, 0, _totalCount.Value);

        bool completesTask = _scrubbed.Value >= _totalCount.Value;
        if (_scrubbed.Value > previousScrubbed)
            PlayItemSuccessSfxClientRpc(completesTask);

        if (!completesTask) return;

        _isComplete = true;

        // Tasks no longer pay coupons — players are only paid for processing suspects (see SuspectController.PayOutResults).
        // ATM.Instance?.SpawnCoupons(_couponReward);

        MarkCompleteClientRpc();

        // Hide from HUD once all pieces are clean.
        _isActive.Value = false;

        Debug.Log("[CleanGraffitiTask] All graffiti scrubbed — task complete.");
    }

    /// <summary>Per-piece success cue on every client; see <see cref="TaskSuccessCue.PlayCleanupItemCue"/>.</summary>
    [ClientRpc]
    private void PlayItemSuccessSfxClientRpc(bool completesTask) =>
        TaskSuccessCue.PlayCleanupItemCue(this, _itemSuccessSfxClip, _itemSuccessSfxVolume, completesTask);

    [ClientRpc]
    private void MarkCompleteClientRpc()
    {
        _isComplete = true;
        TaskRegistry.Instance?.NotifyTaskStateChanged();

        // Fired here (rather than inline in OnGraffitiScrubbed) so every client — not just
        // the server/host process — receives the completion notification. Day_01/Day_02
        // subscribe to this per-client to complete/clear whichever TutorialObjectiveList
        // row they created for this task run; previously this only ever invoked locally
        // wherever OnGraffitiScrubbed's IsServer-gated code ran, so remote (non-host)
        // clients never saw it.
        OnDailyTaskCompleted?.Invoke();
    }

    // ── Day start ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Day 2+: graffiti PILES UP. Unscrubbed pieces from previous days stay on the walls and a fresh
    /// batch is added on free spawn points, so ignoring graffiti keeps costing Checkpoint Integrity.
    /// The day's progress counter restarts at 0 / (carried-over + new). The number of spawn points
    /// is the natural cap — once every point is tagged, nothing new appears until some are scrubbed.
    /// Runs once per campaign day (see <see cref="_dayStartHandledForDay"/>).
    ///
    /// Persistence: the day-start checkpoint is committed one frame after
    /// <see cref="ShiftManager.OnDayStart"/>, so the whole accumulated set is captured by
    /// <see cref="CaptureSaveState"/> and rebuilt by <see cref="RestoreSaveState"/> on load. When a
    /// load is pending, nothing is changed here — the saved set replaces it one frame later.
    /// </summary>
    private void OnDayStart()
    {
        _isComplete = false;

        if (!IsServer) return;

        int day = CurrentCampaignDay;
        if (day == _dayStartHandledForDay) return;

        // A save load will rebuild the authoritative set one frame from now — leave everything as-is
        // (and leave the day unhandled; RestoreSaveState marks it).
        if (CampaignManager.Instance != null && CampaignManager.Instance.HasPendingWorkdayRestore) return;
        if (ShiftManager.Instance != null && ShiftManager.Instance.IsRestoringWorkdayState) return;

        _dayStartHandledForDay = day;

        // Keep only pieces still on the walls; they carry over into today's count.
        PruneDespawnedGraffiti();
        int carriedOver = _spawnedGraffiti.Count;

        int spawned = 0;
        // Day 1 owns its graffiti via Day_01 → SpawnGraffitiEarly (subscribed after this handler).
        if (_spawnOnDayStart && !CleanupTaskGating.IsMandatoryDay)
            spawned = SpawnGraffiti(RollGraffitiCount(_dayStartCountMultiplier));

        _scrubbed.Value   = 0;
        _totalCount.Value = carriedOver + spawned;

        // Optional on Day 2+ (see CleanupTaskGating): activating only drives compass pips and the
        // TaskRegistry entry — HUDTaskList skips this task and it never blocks clock-out.
        _isActive.Value = _totalCount.Value > 0 && !CleanupTaskGating.IsMandatoryDay;

        if (_totalCount.Value > 0)
            Debug.Log($"[CleanGraffitiTask] Day {day} start — {carriedOver} carried over + {spawned} new " +
                      $"= {_totalCount.Value} graffiti piece(s).");
    }

    /// <summary>Current campaign day, or 1 before <see cref="CampaignManager"/> reports one.</summary>
    private static int CurrentCampaignDay =>
        CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : 1;

    /// <summary>Removes scrubbed/destroyed pieces from server tracking.</summary>
    private void PruneDespawnedGraffiti()
    {
        for (int i = _spawnedGraffiti.Count - 1; i >= 0; i--)
        {
            NetworkObject netObj = _spawnedGraffiti[i];
            if (netObj != null && netObj.IsSpawned) continue;

            if (netObj != null) _graffitiPlacements.Remove(netObj);
            _spawnedGraffiti.RemoveAt(i);
        }
    }

    // ── Spawning (server only) ────────────────────────────────────────────────

    private int SpawnGraffiti(int count)
    {
        if (_graffitiPrefabs == null || _graffitiPrefabs.Length == 0)
        {
            Debug.LogError("[CleanGraffitiTask] _graffitiPrefabs is empty — assign at least one prefab.");
            return 0;
        }

        if (_spawnPoints == null || _spawnPoints.Length == 0)
        {
            Debug.LogError("[CleanGraffitiTask] _spawnPoints is empty — assign at least one spawn point.");
            return 0;
        }

        // Pick spawn points without replacement so no two pieces land on the same spot, and skip
        // points still occupied by carried-over graffiti (pieces pile up across days). Clamp to the
        // number of free points since we can't place more unique pieces than there are spots.
        PruneDespawnedGraffiti();
        HashSet<int> occupied = new();
        foreach (GraffitiPlacementSaveData placement in _graffitiPlacements.Values)
            occupied.Add(placement.SpawnPointIndex);

        List<int> availableIndices = new(_spawnPoints.Length);
        for (int i = 0; i < _spawnPoints.Length; i++)
            if (_spawnPoints[i] != null && !occupied.Contains(i))
                availableIndices.Add(i);

        int usableCount = Mathf.Min(count, availableIndices.Count);
        if (usableCount < count)
        {
            Debug.Log($"[CleanGraffitiTask] Requested {count} graffiti pieces but only " +
                      $"{availableIndices.Count} free spawn point(s) — spawning {usableCount}.");
        }

        int spawnedCount = 0;

        for (int i = 0; i < usableCount; i++)
        {
            int listIndex  = Random.Range(0, availableIndices.Count);
            int pointIndex = availableIndices[listIndex];
            availableIndices.RemoveAt(listIndex);

            Transform point = _spawnPoints[pointIndex];
            int prefabIndex = Random.Range(0, _graffitiPrefabs.Length);
            GameObject prefab = _graffitiPrefabs[prefabIndex];

            GameObject go = Instantiate(prefab, point.position, point.rotation);
            NetworkObject netObj = go.GetComponent<NetworkObject>();

            if (netObj == null)
            {
                Debug.LogError($"[CleanGraffitiTask] Graffiti prefab '{prefab.name}' has no NetworkObject component.");
                Destroy(go);
                continue;
            }

            // Route the scrub-completion callback to this task instead of the default
            // GraffitiThreat fallback so clearing graffiti during this task actually
            // registers progress (see GraffitiInteractable.ProgressRoutine).
            GraffitiInteractable interactable = go.GetComponent<GraffitiInteractable>();
            if (interactable != null)
                interactable.OnScrubCompleted = OnGraffitiScrubbed;

            netObj.Spawn(destroyWithScene: true);
            _spawnedGraffiti.Add(netObj);
            _graffitiPlacements[netObj] = new GraffitiPlacementSaveData
            {
                PrefabIndex = prefabIndex,
                SpawnPointIndex = pointIndex
            };
            spawnedCount++;
        }

        return spawnedCount;
    }

    private void SpawnSavedGraffiti(GraffitiPlacementSaveData placement)
    {
        if (placement == null || _graffitiPrefabs == null || _spawnPoints == null ||
            placement.PrefabIndex < 0 || placement.PrefabIndex >= _graffitiPrefabs.Length ||
            placement.SpawnPointIndex < 0 || placement.SpawnPointIndex >= _spawnPoints.Length)
            return;

        GameObject prefab = _graffitiPrefabs[placement.PrefabIndex];
        Transform point = _spawnPoints[placement.SpawnPointIndex];
        if (prefab == null || point == null) return;

        GameObject go = Instantiate(prefab, point.position, point.rotation);
        NetworkObject netObj = go.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Destroy(go);
            return;
        }

        GraffitiInteractable interactable = go.GetComponent<GraffitiInteractable>();
        if (interactable != null)
            interactable.OnScrubCompleted = OnGraffitiScrubbed;

        netObj.Spawn(destroyWithScene: true);
        if (interactable != null)
            interactable.RestoreScrubProgress(placement.ScrubProgress);
        _spawnedGraffiti.Add(netObj);
        _graffitiPlacements[netObj] = placement;
    }

    private void DespawnExistingGraffiti()
    {
        foreach (NetworkObject netObj in _spawnedGraffiti)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _spawnedGraffiti.Clear();
        _graffitiPlacements.Clear();
    }

    // ── Registry management ───────────────────────────────────────────────────

    private void OnIsActiveChanged(bool previous, bool current)
    {
        if (current)
            TaskRegistry.Instance?.AddThreat(this);
        else
            TaskRegistry.Instance?.RemoveThreat(this);
    }

    /// <summary>
    /// Re-registers this task when <see cref="TaskRegistry.SetThreats"/> replaces the
    /// registry list (e.g. at night-phase start), so graffiti stays in the HUD for the
    /// duration of the night phase if it was triggered at day start.
    /// </summary>
    private void OnTaskListChanged()
    {
        if (!_isActive.Value || TaskRegistry.Instance == null) return;

        IReadOnlyList<ISystemicThreat> threats = TaskRegistry.Instance.Threats;
        for (int i = 0; i < threats.Count; i++)
            if (threats[i] == this) return;

        TaskRegistry.Instance.AddThreat(this);
    }

    // ── Progress sync ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fired on every client whenever the scrubbed or total count changes.
    /// Subscribe in day scripts to drive live count updates in tutorial UI.
    /// </summary>
    public static event Action OnProgressChanged;

    /// <summary>Graffiti pieces scrubbed so far this task cycle.</summary>
    public int ScrubbedCount => _scrubbed.Value;

    /// <summary>Total graffiti pieces spawned for this task cycle.</summary>
    public int TotalGraffitiCount => _totalCount.Value;

    private void OnScrubbedChanged(int previous, int current)
    {
        TaskRegistry.Instance?.NotifyTaskStateChanged();
        OnProgressChanged?.Invoke();
    }

    private void OnTotalCountChanged(int previous, int current)
    {
        TaskRegistry.Instance?.NotifyTaskStateChanged();
        OnProgressChanged?.Invoke();
    }

    /// <summary>
    /// Builds the tutorial-overlay objective label, e.g. "Clean graffiti 1/10".
    /// Public so day scripts (which now own their own <see cref="TutorialObjectiveItem"/>
    /// for this task — see <see cref="OnDailyTaskCompleted"/>) can reuse the same format.
    /// </summary>
    public string GetTutorialObjectiveText() =>
        $"Clean graffiti {Mathf.Min(_scrubbed.Value, _totalCount.Value)}/{_totalCount.Value}";

    // ── Editor gizmos ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_spawnPoints == null) return;

        Gizmos.color = new Color(0.8f, 0.2f, 0.9f, 0.9f);

        for (int i = 0; i < _spawnPoints.Length; i++)
        {
            if (_spawnPoints[i] == null) continue;

            Vector3 pos = _spawnPoints[i].position;

            Gizmos.DrawWireSphere(pos, 0.15f);
            Gizmos.DrawLine(pos, pos + _spawnPoints[i].forward * 0.4f);

            UnityEditor.Handles.Label(pos + Vector3.up * 0.3f, $"Graffiti {i}");
        }
    }
#endif
}
