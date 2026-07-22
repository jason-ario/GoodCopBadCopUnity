using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// One-shot mail-sorting task. Every campaign day after Day 1, a delivery of 10-30 packages
/// spawns inside the delivery crate. Each package is addressed to a resident drawn from
/// <see cref="SuspectRunRecords"/> and labelled with a goods category drawn from that day's
/// allowed or prohibited pool (see below).
///
/// The player must physically carry each package and drop it into the correct bin:
///   - Confiscate bin  — goods category is on the prohibited list.
///   - Quarantine bin  — the addressee is currently serving quarantine.
///   - Delivery bin    — the addressee is alive, not quarantined, and the goods are allowed.
///
/// Sorting is detected by <see cref="MailSortBin"/> trigger volumes, which call
/// <see cref="EvaluateSort"/> via <see cref="MailPackageItem.RequestSortServerRpc"/>.
///
/// Each day, <see cref="_prohibitedCountPerDay"/> categories are drawn at random from
/// <see cref="_goodsTypePool"/> to be that day's contraband; the remaining categories in the pool
/// are allowed. The chosen categories are replicated via <see cref="ProhibitedGoodsToday"/> so UI
/// such as the prohibited-goods sign can display them for all clients.
///
/// Unlike <see cref="TakeOutTrashTask"/>, this task is NOT drawn from the
/// <see cref="DailyTaskScheduler"/> pool — it fires automatically and unconditionally every day
/// after Day 1, directly off <see cref="CampaignManager.OnDayChanged"/>, so it never competes
/// with other daily tasks for that day's single scheduler slot.
///
/// Scene setup:
///   - NetworkObject on this GameObject (place under "---Task Manager" alongside other tasks).
///   - Assign _packagePrefab (a MailPackageItem prefab, registered as a Network Prefab).
///   - Assign _crateSpawnPoint (the Delivery Crate's Transform) and tune _spawnRadius.
///   - Assign _groundLayer to match whatever layer packages should land on inside the crate.
///   - Assign _goodsTypePool / _prohibitedCountPerDay to taste.
///   - Optionally assign _deliveryTruck (a <see cref="DeliveryTruckController"/>) so the delivery
///     is preceded by a drive-in cutscene instead of packages appearing instantly.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class SortMailTask : NetworkBehaviour, ISystemicThreat, IDailyTask
{
    public static SortMailTask Instance { get; private set; }

    [Header("Threat Properties")]
    [SerializeField] private string _threatName = "Sort mail";
    [Tooltip("Coupons awarded when every package has been correctly sorted.")]
    [SerializeField] private int _couponReward = 10;

    [Header("Daily Task")]
    [Tooltip("Stable identifier — kept for IDailyTask compatibility even though this task is not driven by DailyTaskScheduler.")]
    [SerializeField] private string _dailyTaskId = "SortMail";

    [Header("Spawning")]
    [Tooltip("Minimum number of packages per delivery (inclusive).")]
    [SerializeField] private int _minPackageCount = 10;
    [Tooltip("Maximum number of packages per delivery (inclusive).")]
    [SerializeField] private int _maxPackageCount = 30;
    [Tooltip("MailPackageItem prefab to spawn. Must be registered as a Network Prefab in the NetworkManager.")]
    [SerializeField] private GameObject _packagePrefab;
    [Tooltip("Centre point packages spawn around — assign the Delivery Crate's Transform.")]
    [SerializeField] private Transform _crateSpawnPoint;
    [Tooltip("Horizontal radius around _crateSpawnPoint that packages may land within.")]
    [SerializeField] private float _spawnRadius = 0.6f;
    [Tooltip("Layer(s) the downward raycast hits to land packages on the crate floor / ground.")]
    [SerializeField] private LayerMask _groundLayer;
    [Tooltip("Extra height added above the raycast hit point so packages sit on the surface rather than clipping into it.")]
    [SerializeField] private float _spawnHeightOffset = 0.05f;

    [Header("Delivery Truck")]
    [Tooltip("Optional. If assigned, each day's delivery is preceded by this truck driving in, " +
             "spawning packages on arrival, idling, then driving off — instead of packages " +
             "appearing immediately on day change.")]
    [SerializeField] private DeliveryTruckController _deliveryTruck;

    [Header("Goods Categories")]
    [Tooltip("The full pool of goods categories that can appear on packages. Every delivery, " +
             "_prohibitedCountPerDay of these are drawn at random to be today's contraband; the " +
             "rest are allowed that day. Changeable at any time in the Inspector.")]
    [SerializeField] private string[] _goodsTypePool =
    {
        "Clothing", "Books", "Toiletries", "Food", "Toys", "Letters",
        "Medicine", "Weapons", "Radio Equipment"
    };
    [Tooltip("How many categories from _goodsTypePool are chosen as prohibited each day.")]
    [SerializeField] private int _prohibitedCountPerDay = 3;

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<float> _networkThreatLevel = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _totalCount = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _sortedCount = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _isActive = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Today's randomly-chosen prohibited goods categories, replicated to all clients so
    /// the prohibited-goods sign (and the delivery alert) can display them.</summary>
    private readonly NetworkList<FixedString64Bytes> _prohibitedGoodsToday = new(
        writePerm: NetworkVariableWritePermission.Server);

    /// <summary>Today's prohibited goods categories. Read-only view for UI such as the sign display.</summary>
    public NetworkList<FixedString64Bytes> ProhibitedGoodsToday => _prohibitedGoodsToday;

    // ── Local state (server-only) ─────────────────────────────────────────────

    private readonly List<NetworkObject> _spawnedPackages = new();
    private readonly List<string> _todaysAllowedGoods = new();
    private readonly List<string> _todaysProhibitedGoods = new();
    private bool _taskActive;
    private int _lastTriggeredDay = -1;

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public string ThreatName  => _threatName;
    public float  ScoreWeight => 1f;
    public float  ThreatLevel => _networkThreatLevel.Value;

    /// <summary>Shown in the HUD as "sorted/total", e.g. "Packages sorted 1/30".</summary>
    public string ThreatDescription =>
        _totalCount.Value > 0
            ? $"Packages sorted {Mathf.Min(_sortedCount.Value, _totalCount.Value)}/{_totalCount.Value}"
            : string.Empty;

    // ── IDailyTask ───────────────────────────────────────────────────────────

    public string DailyTaskId => _dailyTaskId;
    public void TriggerDailyTask()
    {
        if (_deliveryTruck != null)
            _deliveryTruck.BeginDeliverySequence();
        else
            TriggerTask();
    }
    public event Action OnDailyTaskCompleted;

    // ── Public events ────────────────────────────────────────────────────────

    /// <summary>Fired on the server when every package has been correctly sorted.</summary>
    public static event Action OnAllPackagesSorted;

    /// <summary>Fired on every client whenever the sorted/total counts change.</summary>
    public static event Action OnProgressChanged;

    public int SortedCount => _sortedCount.Value;
    public int TotalCount  => _totalCount.Value;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[SortMailTask] Duplicate instance detected — destroying self.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        CampaignManager.OnDayChanged += OnDayChanged;
    }

    private void OnDisable()
    {
        CampaignManager.OnDayChanged -= OnDayChanged;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _sortedCount.OnValueChanged += OnNetworkValueChanged;
        _totalCount.OnValueChanged  += OnNetworkValueChanged;
        _isActive.OnValueChanged    += OnIsActiveChanged;

        if (_isActive.Value)
            TaskRegistry.Instance?.AddThreat(this);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _sortedCount.OnValueChanged -= OnNetworkValueChanged;
        _totalCount.OnValueChanged  -= OnNetworkValueChanged;
        _isActive.OnValueChanged    -= OnIsActiveChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        OnAllPackagesSorted = null;
        OnProgressChanged   = null;
        _prohibitedGoodsToday.Dispose();
    }

    private void OnNetworkValueChanged<T>(T previous, T current)
    {
        TaskRegistry.Instance?.NotifyTaskStateChanged();
        OnProgressChanged?.Invoke();
    }

    private void OnIsActiveChanged(bool previous, bool current)
    {
        if (current)
            TaskRegistry.Instance?.AddThreat(this);
        else
            TaskRegistry.Instance?.RemoveThreat(this);
    }

    // ── ISystemicThreat stubs ────────────────────────────────────────────────

    public void BeginNightPhase() { }
    public void EndNightPhase() { }

    // ── Day trigger ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fires on every day change. Triggers the mail delivery once per day, for every day after
    /// Day 1 — independent of DailyTaskScheduler, so it never competes with other daily tasks.
    /// If a <see cref="_deliveryTruck"/> is assigned, the truck's drive-in cutscene decides when
    /// packages actually spawn (on arrival); otherwise packages spawn immediately.
    /// </summary>
    private void OnDayChanged(int day)
    {
        if (!IsServer) return;
        if (day <= 1) return;
        if (day == _lastTriggeredDay) return;

        _lastTriggeredDay = day;

        if (_deliveryTruck != null)
            _deliveryTruck.BeginDeliverySequence();
        else
            TriggerTask();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Despawns any leftover packages, spawns a fresh delivery, and registers this task in the
    /// HUD. Server-only.
    /// </summary>
    public void TriggerTask()
    {
        if (!IsServer) return;

        DespawnExistingPackages();
        _taskActive = true;
        _sortedCount.Value = 0;

        ChooseTodaysProhibitedGoods();

        List<SuspectRecord> addressPool = BuildAddressablePool();
        if (addressPool.Count == 0)
        {
            Debug.LogWarning("[SortMailTask] No eligible residents to address packages to — mail delivery skipped.");
            return;
        }

        int packageCount = Random.Range(_minPackageCount, _maxPackageCount + 1);
        for (int i = 0; i < packageCount; i++)
            SpawnSinglePackage(addressPool);

        _totalCount.Value = _spawnedPackages.Count;
        UpdateThreatLevel();

        _isActive.Value = true;

        NotifyDeliveryAlertClientRpc();

        Debug.Log($"[SortMailTask] Delivery triggered — spawned {_spawnedPackages.Count} package(s). " +
                  $"Prohibited today: {string.Join(", ", _todaysProhibitedGoods)}");
    }

    /// <summary>
    /// Called by <see cref="MailPackageItem.RequestSortServerRpc"/> when a package is dropped
    /// into a bin. Server-only; validates the placement and either despawns the package (correct)
    /// or bounces it back out (incorrect).
    /// </summary>
    public void EvaluateSort(MailPackageItem package, MailSortBinType binType)
    {
        if (!IsServer) return;
        if (package == null || package.IsResolved) return;

        if (package.CorrectBin == binType)
        {
            package.MarkResolved();
            _spawnedPackages.Remove(package.NetworkObject);
            package.NetworkObject.Despawn(destroy: true);

            _sortedCount.Value = Mathf.Min(_sortedCount.Value + 1, _totalCount.Value);
            Debug.Log($"[SortMailTask] Correctly sorted '{package.ResidentName}' ({package.GoodsLabel}) into {binType}. " +
                      $"{_sortedCount.Value}/{_totalCount.Value}");

            if (_sortedCount.Value >= _totalCount.Value)
                CompleteTask();
        }
        else
        {
            Vector3 away = package.transform.position - (_crateSpawnPoint != null ? _crateSpawnPoint.position : package.transform.position);
            if (away.sqrMagnitude < 0.01f) away = UnityEngine.Random.insideUnitSphere;
            package.RejectFromBin(away);

            Debug.Log($"[SortMailTask] Wrong bin for '{package.ResidentName}' ({package.GoodsLabel}) — " +
                      $"dropped in {binType}, belongs in {package.CorrectBin}.");
        }
    }

    // ── Private ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the pool of residents packages can be addressed to. Excludes killed suspects —
    /// dead residents are not one of the three sortable outcomes (contraband / deliverable /
    /// quarantined), so they are simply never used as addressees.
    /// </summary>
    private List<SuspectRecord> BuildAddressablePool()
    {
        var pool = new List<SuspectRecord>();
        if (SuspectRunRecords.Instance == null) return pool;

        foreach (SuspectRecord record in SuspectRunRecords.Instance.Records)
        {
            if (record == null || record.SuspectData == null) continue;
            if (record.isKilled) continue;
            pool.Add(record);
        }

        return pool;
    }

    private void SpawnSinglePackage(List<SuspectRecord> addressPool)
    {
        if (_packagePrefab == null)
        {
            Debug.LogError("[SortMailTask] _packagePrefab is not assigned.");
            return;
        }

        SuspectRecord resident = addressPool[Random.Range(0, addressPool.Count)];
        string residentName = $"{resident.SuspectData.FirstName} {resident.SuspectData.LastName}".Trim();

        bool isProhibited = Random.Range(0, _todaysAllowedGoods.Count + _todaysProhibitedGoods.Count) >= _todaysAllowedGoods.Count;
        string goodsLabel = isProhibited
            ? _todaysProhibitedGoods[Random.Range(0, _todaysProhibitedGoods.Count)]
            : _todaysAllowedGoods[Random.Range(0, _todaysAllowedGoods.Count)];

        int currentDay = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : -1;
        bool isQuarantined = SuspectRunRecords.Instance != null &&
                              SuspectRunRecords.Instance.GetRemainingQuarantineDays(resident, currentDay) > 0;

        MailSortBinType correctBin = isProhibited
            ? MailSortBinType.Confiscate
            : isQuarantined
                ? MailSortBinType.Quarantine
                : MailSortBinType.Delivery;

        Vector3    spawnPos = GetRandomSpawnPosition();
        Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        GameObject itemGo = Instantiate(_packagePrefab, spawnPos, spawnRot);
        NetworkObject netObj = itemGo.GetComponent<NetworkObject>();
        MailPackageItem package = itemGo.GetComponent<MailPackageItem>();

        if (netObj == null || package == null)
        {
            Debug.LogError("[SortMailTask] Package prefab is missing a NetworkObject or MailPackageItem component.");
            Destroy(itemGo);
            return;
        }

        netObj.Spawn(destroyWithScene: true);
        package.ServerInitialize(residentName, goodsLabel, correctBin);
        _spawnedPackages.Add(netObj);
    }

    /// <summary>
    /// Server-only. Draws <see cref="_prohibitedCountPerDay"/> distinct categories at random from
    /// <see cref="_goodsTypePool"/> to be today's contraband, replicates them via
    /// <see cref="_prohibitedGoodsToday"/>, and rebuilds the local allowed/prohibited caches used
    /// when labelling packages.
    /// </summary>
    private void ChooseTodaysProhibitedGoods()
    {
        _todaysProhibitedGoods.Clear();
        _todaysAllowedGoods.Clear();
        _prohibitedGoodsToday.Clear();

        if (_goodsTypePool == null || _goodsTypePool.Length == 0)
        {
            Debug.LogWarning("[SortMailTask] _goodsTypePool is empty — no goods categories available.");
            return;
        }

        var shuffled = new List<string>(_goodsTypePool);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        int prohibitedCount = Mathf.Clamp(_prohibitedCountPerDay, 0, shuffled.Count);
        for (int i = 0; i < shuffled.Count; i++)
        {
            if (i < prohibitedCount)
            {
                _todaysProhibitedGoods.Add(shuffled[i]);
                _prohibitedGoodsToday.Add(shuffled[i]);
            }
            else
            {
                _todaysAllowedGoods.Add(shuffled[i]);
            }
        }

        // Fall back to at least one allowed category so packages always have something to
        // address if the pool is smaller than _prohibitedCountPerDay.
        if (_todaysAllowedGoods.Count == 0 && _todaysProhibitedGoods.Count > 0)
            _todaysAllowedGoods.Add(_todaysProhibitedGoods[0]);
    }

    private void CompleteTask()
    {
        _taskActive = false;

        Debug.Log("[SortMailTask] All packages sorted — task complete.");
        if (ATM.Instance != null)
            ATM.Instance.SpawnCoupons(_couponReward);

        OnAllPackagesSorted?.Invoke();
        OnDailyTaskCompleted?.Invoke();

        _isActive.Value = false;
    }

    private void UpdateThreatLevel()
    {
        int total = _totalCount.Value > 0 ? _totalCount.Value : (_minPackageCount + _maxPackageCount) / 2;
        _networkThreatLevel.Value = total > 0 ? (float)_spawnedPackages.Count / total : 0f;
    }

    private void DespawnExistingPackages()
    {
        foreach (NetworkObject netObj in _spawnedPackages)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _spawnedPackages.Clear();
        _networkThreatLevel.Value = 0f;
    }

    private Vector3 GetRandomSpawnPosition()
    {
        if (_crateSpawnPoint == null)
        {
            Debug.LogWarning("[SortMailTask] _crateSpawnPoint not assigned — spawning at origin.");
            return Vector3.zero;
        }

        Vector2 offset = Random.insideUnitCircle * _spawnRadius;
        Vector3 castOrigin = _crateSpawnPoint.position + new Vector3(offset.x, 5f, offset.y);

        if (Physics.Raycast(castOrigin, Vector3.down, out RaycastHit hit, 20f, _groundLayer, QueryTriggerInteraction.Ignore))
            return hit.point + Vector3.up * _spawnHeightOffset;

        return new Vector3(castOrigin.x, _crateSpawnPoint.position.y + _spawnHeightOffset, castOrigin.z);
    }

    /// <summary>
    /// Shows a lightweight, non-blocking alert on every client — the mail-sorting equivalent of
    /// the "Go to the booth to start your shift" prompt — announcing today's delivery and which
    /// goods categories are prohibited.
    /// </summary>
    [ClientRpc]
    private void NotifyDeliveryAlertClientRpc()
    {
        if (PlayerTutorialUI.Instance == null) return;

        string prohibited = _todaysProhibitedGoods.Count > 0 ? string.Join(", ", _todaysProhibitedGoods) : "none";
        string message = $"A mail delivery has arrived — sort it at the mail bins.\nToday's prohibited goods: {prohibited}";
        PlayerTutorialUI.Instance.ShowTextOnly(message, 5f);
    }
}
