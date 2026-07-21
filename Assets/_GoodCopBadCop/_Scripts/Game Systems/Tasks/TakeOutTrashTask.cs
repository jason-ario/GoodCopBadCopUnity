using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// One-shot trash-collection task. Call <see cref="TriggerTask"/> on the server to
/// immediately spawn a random number of trash items across the configured spawn zones.
///
/// Pre-existing <see cref="JunkItem"/> instances in the scene (e.g. the dead soldier body)
/// are automatically counted toward the total so the HUD denominator is always accurate.
///
/// Progress is reflected via <see cref="ThreatDescription"/>: "deposited/total".
/// <see cref="ThreatLevel"/> retains the fraction of spawned items still uncollected for
/// backwards compatibility with ISystemicThreat, but does NOT drive HUD refreshes.
///
/// Scene setup:
///   - NetworkObject on this GameObject.
///   - Assign _trashPrefabs (all registered in NetworkManager's prefab list).
///   - Assign _spawnZones with centre Transforms and half-extents.
///   - Set _groundLayer to match your environment layer.
///   - Register this component in TaskRegistry via AlexeiController.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class TakeOutTrashTask : NetworkBehaviour, ISystemicThreat, IDailyTask
{
    public static TakeOutTrashTask Instance { get; private set; }

    [Header("Threat Properties")]
    [SerializeField] private string _threatName = "Take out trash";
    [Tooltip("Number of coupons the ATM dispenses when all trash has been deposited.")]
    [SerializeField] private int _couponReward = 10;

    [Header("Daily Task")]
    [Tooltip("Stable identifier used by DailyTaskScheduler and SaveDataManager. Must match the TaskId entry in DailyTaskScheduler's pool.")]
    [SerializeField] private string _dailyTaskId = "TakeOutTrash";

    [Header("Spawning")]
    [Tooltip("Minimum number of trash items to spawn when TriggerTask is called (inclusive).")]
    [SerializeField] private int _minSpawnCount = 8;

    [Tooltip("Maximum number of trash items to spawn when TriggerTask is called (inclusive).")]
    [SerializeField] private int _maxSpawnCount = 12;

    [Tooltip("Pool of trash prefabs to pick from. All must be registered as Network Prefabs in the NetworkManager.")]
    [SerializeField] private GameObject[] _trashPrefabs;

    [Tooltip("One or more zones in which items are randomly placed.")]
    [SerializeField] private SpawnZone[] _spawnZones;

    [Tooltip("Layer(s) the downward raycast hits to land items on the ground.")]
    [SerializeField] private LayerMask _groundLayer;

    [Tooltip("Extra height added above the raycast hit point so items sit on the surface rather than clipping into it.")]
    [SerializeField] private float _spawnHeightOffset = 0.05f;

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<float> _networkThreatLevel = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Total junk items for this task run: spawned items + pre-existing scene JunkItems.
    /// Set once on the server when TriggerTask runs; clients read it for HUD display.
    /// </summary>
    private readonly NetworkVariable<int> _totalCount = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Running count of junk items deposited in the dumpster this task run.</summary>
    private readonly NetworkVariable<int> _depositedCount = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Whether this task is currently active and should appear in the HUD task list.
    /// Drives TaskRegistry registration on all clients — including late joiners —
    /// without requiring one-shot ClientRpc calls.
    /// </summary>
    private readonly NetworkVariable<bool> _isActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Local state (server-only) ─────────────────────────────────────────────

    private readonly List<NetworkObject> _spawnedItems = new();
    private bool _taskActive;

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public string ThreatName  => _threatName;
    public float  ScoreWeight => 1f;
    public float  ThreatLevel => _networkThreatLevel.Value;

    /// <summary>
    /// Items deposited in the dumpster so far out of the total task count.
    /// Shown as "X/Total" in the HUD. Returns an empty string until the task starts.
    /// </summary>
    public string ThreatDescription =>
        _totalCount.Value > 0
            ? $"{Mathf.Min(_depositedCount.Value, _totalCount.Value)}/{_totalCount.Value}"
            : string.Empty;

    // ── IDailyTask ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string DailyTaskId => _dailyTaskId;

    /// <summary>
    /// Triggers this task as the randomly-selected daily task. Delegates to <see cref="TriggerTask"/>.
    /// Server-only; <see cref="TriggerTask"/> enforces the IsServer guard internally.
    /// </summary>
    public void TriggerDailyTask() => TriggerTask();

    /// <inheritdoc/>
    public event Action OnDailyTaskCompleted;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[TakeOutTrashTask] Duplicate instance detected — destroying self.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // Only _depositedCount drives HUD refreshes — collecting items into a bag
        // must not update the task display, only depositing in the dumpster should.
        _depositedCount.OnValueChanged += OnNetworkValueChanged;
        _totalCount.OnValueChanged     += OnNetworkValueChanged;
        _isActive.OnValueChanged       += OnIsActiveChanged;

        // Handle the initial value for late-joining clients: if the task was already
        // active before this client connected, register it in TaskRegistry immediately.
        if (_isActive.Value)
            TaskRegistry.Instance?.AddThreat(this);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _depositedCount.OnValueChanged -= OnNetworkValueChanged;
        _totalCount.OnValueChanged     -= OnNetworkValueChanged;
        _isActive.OnValueChanged       -= OnIsActiveChanged;
        DumpsterInteractable.OnTrashBagDeposited -= OnTrashBagDeposited;
    }

    private void OnNetworkValueChanged<T>(T previous, T current)
    {
        TaskRegistry.Instance?.NotifyTaskStateChanged();
        OnProgressChanged?.Invoke();
    }

    /// <summary>
    /// Fires on all clients when <see cref="_isActive"/> changes.
    /// Adds or removes this task from <see cref="TaskRegistry"/> so every client's HUD
    /// stays in sync without relying on one-shot ClientRpc calls.
    /// </summary>
    private void OnIsActiveChanged(bool previous, bool current)
    {
        if (current)
            TaskRegistry.Instance?.AddThreat(this);
        else
            TaskRegistry.Instance?.RemoveThreat(this);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        JunkItem.OnAnyJunkItemCollected          -= OnJunkItemCollected;
        DumpsterInteractable.OnTrashBagDeposited -= OnTrashBagDeposited;
        OnAllItemsDeposited                       = null;
        OnProgressChanged                         = null;
    }

    // ── ISystemicThreat stubs ────────────────────────────────────────────────

    /// <summary>No-op — this threat is triggered explicitly, not by the night phase.</summary>
    public void BeginNightPhase() { }

    /// <summary>No-op.</summary>
    public void EndNightPhase() { }

    // ── Events ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired on the server when every junk item has been deposited in the dumpster.
    /// Subscribe server-side (e.g. AlexeiController) to trigger clock-out.
    /// </summary>
    public static event Action OnAllItemsDeposited;

    /// <summary>
    /// Fired on every client whenever <see cref="DepositedCount"/> or <see cref="TotalCount"/>
    /// changes. Subscribe to drive live count updates in tutorial UI.
    /// </summary>
    public static event Action OnProgressChanged;

    /// <summary>Items deposited in the dumpster so far this task run.</summary>
    public int DepositedCount => _depositedCount.Value;

    /// <summary>Total junk items for this task run (spawned + pre-existing).</summary>
    public int TotalCount => _totalCount.Value;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Activates the trash task for any <see cref="JunkItem"/>s already present in the
    /// scene WITHOUT spawning new items. If no active JunkItems exist the call is a no-op.
    ///
    /// Use this at the start of the night phase (or at any scripted moment) when items may
    /// already be in the world — e.g. a soldier body left at the end of Day 1's shift.
    /// Server-only.
    /// </summary>
    public void ActivateForExistingItems()
    {
        if (!IsServer) return;
        if (_taskActive) return;

        var existingJunk = FindObjectsByType<JunkItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0;
        foreach (JunkItem j in existingJunk)
        {
            if (j.enabled && j.gameObject.activeInHierarchy)
                count++;
        }

        if (count == 0)
        {
            Debug.Log("[TakeOutTrashTask] ActivateForExistingItems — no active JunkItems found, task not activated.");
            return;
        }

        _taskActive = true;
        _depositedCount.Value = 0;
        _totalCount.Value = count;

        UpdateThreatLevel();

        JunkItem.OnAnyJunkItemCollected          += OnJunkItemCollected;
        DumpsterInteractable.OnTrashBagDeposited += OnTrashBagDeposited;

        _isActive.Value = true;

        Debug.Log($"[TakeOutTrashTask] ActivateForExistingItems — activated for {count} existing JunkItem(s) (no new items spawned).");
    }

    /// <summary>
    /// Spawns a random number of trash items (between <see cref="_minSpawnCount"/> and
    /// <see cref="_maxSpawnCount"/>), counts any pre-existing <see cref="JunkItem"/>s
    /// already active in the scene (e.g. the dead soldier body), and registers this task
    /// in <see cref="TaskRegistry"/> on all clients. Server-only.
    /// </summary>
    public void TriggerTask()
    {
        if (!IsServer) return;

        DespawnExistingItems();
        _taskActive = true;
        _depositedCount.Value = 0;

        // Count pre-existing JunkItems in the scene BEFORE spawning (e.g. soldier body).
        // Uses FindObjectsInactive.Include so disabled-component JunkItems on active GameObjects
        // are found, then filters to those that are actually enabled and active in the hierarchy.
        var existingJunk = FindObjectsByType<JunkItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int preExistingCount = 0;
        foreach (JunkItem j in existingJunk)
        {
            if (j.enabled && j.gameObject.activeInHierarchy)
                preExistingCount++;
        }

        int spawnCount = Random.Range(_minSpawnCount, _maxSpawnCount + 1);
        for (int i = 0; i < spawnCount; i++)
            SpawnSingleItem();

        // Total = actually spawned (may be less than spawnCount on error) + pre-existing.
        _totalCount.Value = _spawnedItems.Count + preExistingCount;

        UpdateThreatLevel();

        JunkItem.OnAnyJunkItemCollected          += OnJunkItemCollected;
        DumpsterInteractable.OnTrashBagDeposited += OnTrashBagDeposited;

        // Flip the active flag — OnIsActiveChanged fires on all clients (and late joiners
        // read the initial value in OnNetworkSpawn) to register this task in TaskRegistry.
        _isActive.Value = true;

        Debug.Log($"[TakeOutTrashTask] Task triggered — spawned {_spawnedItems.Count}, " +
                  $"pre-existing {preExistingCount}, total {_totalCount.Value}.");
    }

    // ── Private ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called on the server when a TrashBag is deposited in a dumpster.
    /// Increments the deposited count by the number of junk items the bag contained.
    /// When all items are deposited, fires <see cref="OnAllItemsDeposited"/> and removes
    /// the task from the HUD on all clients.
    /// </summary>
    private void OnTrashBagDeposited(int junkCount)
    {
        _depositedCount.Value = Mathf.Min(_depositedCount.Value + junkCount, _totalCount.Value);
        Debug.Log($"[TakeOutTrashTask] {junkCount} item(s) deposited. " +
                  $"Total deposited: {_depositedCount.Value}/{_totalCount.Value}");

        if (_depositedCount.Value < _totalCount.Value) return;

        // All items deposited — complete the task.
        _taskActive = false;
        JunkItem.OnAnyJunkItemCollected          -= OnJunkItemCollected;
        DumpsterInteractable.OnTrashBagDeposited -= OnTrashBagDeposited;

        Debug.Log("[TakeOutTrashTask] All items deposited — task complete.");
        ATM.Instance?.SpawnCoupons(_couponReward);
        OnAllItemsDeposited?.Invoke();
        OnDailyTaskCompleted?.Invoke();

        // Flip the active flag — OnIsActiveChanged fires on all clients to remove the task.
        _isActive.Value = false;
    }

    private void OnJunkItemCollected()
    {
        if (!IsServer) return;

        PruneCollectedItems();
        UpdateThreatLevel();

        Debug.Log($"[TakeOutTrashTask] Item collected into bag — remaining spawned items: {_spawnedItems.Count}");
    }

    private void SpawnSingleItem()
    {
        if (_trashPrefabs == null || _trashPrefabs.Length == 0)
        {
            Debug.LogError("[TakeOutTrashTask] _trashPrefabs is empty or not assigned.");
            return;
        }

        GameObject prefab = _trashPrefabs[Random.Range(0, _trashPrefabs.Length)];
        if (prefab == null) return;

        Vector3    spawnPos = GetRandomSpawnPosition();
        Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        GameObject    itemGo = Instantiate(prefab, spawnPos, spawnRot);
        NetworkObject netObj = itemGo.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[TakeOutTrashTask] Trash prefab is missing a NetworkObject component.");
            Destroy(itemGo);
            return;
        }

        netObj.Spawn(destroyWithScene: true);
        _spawnedItems.Add(netObj);
    }

    private void PruneCollectedItems()
    {
        _spawnedItems.RemoveAll(n => n == null || !n.IsSpawned);
    }

    private void UpdateThreatLevel()
    {
        int total = _totalCount.Value > 0 ? _totalCount.Value : (_minSpawnCount + _maxSpawnCount) / 2;
        _networkThreatLevel.Value = total > 0
            ? (float)_spawnedItems.Count / total
            : 0f;
    }

    private void DespawnExistingItems()
    {
        foreach (NetworkObject netObj in _spawnedItems)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _spawnedItems.Clear();
        _networkThreatLevel.Value = 0f;
    }

    private Vector3 GetRandomSpawnPosition()
    {
        if (_spawnZones == null || _spawnZones.Length == 0)
        {
            Debug.LogWarning("[TakeOutTrashTask] No spawn zones assigned — spawning at origin.");
            return Vector3.zero;
        }

        SpawnZone zone = _spawnZones[Random.Range(0, _spawnZones.Length)];

        if (zone == null)
        {
            Debug.LogWarning("[TakeOutTrashTask] A spawn zone is null — spawning at origin.");
            return Vector3.zero;
        }

        Vector3 castOrigin = zone.GetRandomPosition() + Vector3.up * 5f;

        if (Physics.Raycast(castOrigin, Vector3.down, out RaycastHit hit, 20f, _groundLayer, QueryTriggerInteraction.Ignore))
            return hit.point + Vector3.up * _spawnHeightOffset;

        return new Vector3(castOrigin.x, zone.transform.position.y + _spawnHeightOffset, castOrigin.z);
    }
}
