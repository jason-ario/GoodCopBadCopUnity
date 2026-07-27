using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Daily graffiti-cleaning task. Graffiti is spawned at day start via
/// <see cref="TriggerDailyTask"/> (called by <see cref="DailyTaskScheduler"/> or day scripts),
/// then persists through the shift so players can scrub it during the night phase.
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

    [Header("Spawning")]
    [Tooltip("Minimum number of graffiti pieces to spawn when triggered (inclusive).")]
    [SerializeField] private int _minGraffitiCount = 2;
    [Tooltip("Maximum number of graffiti pieces to spawn when triggered (inclusive).")]
    [SerializeField] private int _maxGraffitiCount = 6;
    [Tooltip("Pool of graffiti prefabs to pick from at random. Each must be a registered Network Prefab.")]
    [SerializeField] private GameObject[] _graffitiPrefabs;
    [Tooltip("Transforms on the checkpoint walls where graffiti can appear. A point is picked at random for each piece.")]
    [SerializeField] private Transform[]  _spawnPoints;

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

    // ── Local state ───────────────────────────────────────────────────────────

    private readonly List<NetworkObject> _spawnedGraffiti = new();
    private bool _isComplete;

    // ── ISystemicThreat ──────────────────────────────────────────────────────

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
    /// Spawns a random number of graffiti pieces at day start.
    /// Despawns any leftovers from a previous cycle first.
    /// Server-only; safe to call from <see cref="DailyTaskScheduler"/> or day scripts.
    /// </summary>
    public void TriggerDailyTask()
    {
        if (!IsServer) return;

        _isComplete = false;
        _scrubbed.Value = 0;
        DespawnExistingGraffiti();

        int count = Random.Range(_minGraffitiCount, _maxGraffitiCount + 1);

        int spawnedCount = SpawnGraffiti(count);
        _totalCount.Value = spawnedCount;

        // Flip the active flag — OnIsActiveChanged fires on all clients (and late joiners
        // read the initial value in OnNetworkSpawn) to register this task in TaskRegistry.
        _isActive.Value = true;

        ShiftManager.Instance?.RegisterPendingDailyTask(this);

        Debug.Log($"[CleanGraffitiTask] TriggerDailyTask — spawning {spawnedCount} graffiti piece(s).");
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

        _scrubbed.Value = Mathf.Clamp(_scrubbed.Value + 1, 0, _totalCount.Value);

        if (_scrubbed.Value < _totalCount.Value) return;

        _isComplete = true;

        ATM.Instance?.SpawnCoupons(_couponReward);

        MarkCompleteClientRpc();

        // Hide from HUD once all pieces are clean.
        _isActive.Value = false;

        Debug.Log("[CleanGraffitiTask] All graffiti scrubbed — task complete.");
    }

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
    /// Cleans up any remaining graffiti from the previous day.
    /// Called before <see cref="TriggerDailyTask"/> so the spawn list is always fresh.
    /// </summary>
    private void OnDayStart()
    {
        _isComplete = false;

        if (!IsServer) return;

        if (_spawnedGraffiti.Count > 0)
        {
            // Deactivate first so OnIsActiveChanged removes the stale HUD threat entry
            // (e.g. graffiti left unscrubbed from the previous day) before the counts
            // below are reset.
            _isActive.Value = false;

            _scrubbed.Value   = 0;
            _totalCount.Value = 0;
            DespawnExistingGraffiti();
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

        // Pick spawn points without replacement so no two pieces land on the same spot
        // in a single spawn cycle. Clamp to the number of available points since we
        // can't place more unique pieces than there are spots.
        int usableCount = Mathf.Min(count, _spawnPoints.Length);
        if (usableCount < count)
        {
            Debug.LogWarning($"[CleanGraffitiTask] Requested {count} graffiti pieces but only " +
                              $"{_spawnPoints.Length} spawn point(s) are assigned — spawning {usableCount}.");
        }

        List<int> availableIndices = new(_spawnPoints.Length);
        for (int i = 0; i < _spawnPoints.Length; i++)
            availableIndices.Add(i);

        int spawnedCount = 0;

        for (int i = 0; i < usableCount; i++)
        {
            int listIndex  = Random.Range(0, availableIndices.Count);
            int pointIndex = availableIndices[listIndex];
            availableIndices.RemoveAt(listIndex);

            Transform  point  = _spawnPoints[pointIndex];
            GameObject prefab = _graffitiPrefabs[Random.Range(0, _graffitiPrefabs.Length)];

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
            spawnedCount++;
        }

        return spawnedCount;
    }

    private void DespawnExistingGraffiti()
    {
        foreach (NetworkObject netObj in _spawnedGraffiti)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _spawnedGraffiti.Clear();
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
