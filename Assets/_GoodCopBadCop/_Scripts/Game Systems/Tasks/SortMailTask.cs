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
/// The player must physically carry each package and drop it into the correct bin — either by
/// throwing it directly with real physics, or by walking up and interacting with the bin/cubby
/// while holding the package (a scripted toss arc, mirroring <see cref="DumpsterInteractable"/>):
///   - Confiscate bin  — goods category is on the prohibited list.
///   - Addressee's cubby (Mail Cubbies) — the goods are allowed (addressee is alive — dead
///     residents are never used as addressees, see <see cref="BuildAddressablePool"/>). There is
///     no generic "Delivery" bin: the package must land in the specific
///     <see cref="MailCubbySlot"/> assigned to that resident, or it is bounced back out even
///     though it is a deliverable package.
///
/// A correctly sorted package (Confiscate or Delivery) is never despawned immediately — it is
/// locked in place where it landed (see <see cref="MailPackageItem.MarkConfiscated"/>/
/// <see cref="MailPackageItem.MarkDelivered"/>) and only cleared at the start of the next day by
/// <see cref="DespawnResolvedPackages"/>.
///
/// Quarantine sorting has been removed from this task — packages are only ever Confiscate or
/// Delivery, regardless of the addressee's quarantine status.
///
/// Sorting is detected by <see cref="MailSortBin"/> (Confiscate) and
/// <see cref="MailCubbySlot"/> (Delivery) trigger volumes, which call
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

    /// <summary>Packages correctly sorted (delivered to a mailbox cubby or confiscated into a
    /// Confiscate bin) that are left sitting there (locked, no longer despawned immediately —
    /// see <see cref="MailPackageItem.MarkDelivered"/>/<see cref="MailPackageItem.MarkConfiscated"/>)
    /// until <see cref="DespawnResolvedPackages"/> clears them at the start of the next day.</summary>
    private readonly List<NetworkObject> _resolvedPackages = new();
    private readonly List<string> _todaysAllowedGoods = new();
    private readonly List<string> _todaysProhibitedGoods = new();
    private bool _taskActive;
    private int _lastTriggeredDay = -1;

    /// <summary>Last day for which <see cref="ChooseTodaysProhibitedGoods"/> has run — tracked
    /// separately from <see cref="_lastTriggeredDay"/> since the goods roll happens every day
    /// (including Day 1), while the delivery itself only triggers after Day 1.</summary>
    private int _lastGoodsRollDay = -1;

    /// <summary>
    /// When set to a day number, <see cref="OnDayChanged"/> skips its normal automatic delivery
    /// trigger for that specific day — it just marks the day as handled and returns, leaving the
    /// caller responsible for invoking <see cref="TriggerDeferredDelivery"/> once ready. Used by
    /// Day 2, where the mail delivery must not appear until Vlad's tool locker dialogue finishes.
    /// Reset to -1 automatically once consumed. Must be set before the day actually changes
    /// (e.g. in a day script's DayActivated, which CampaignManager calls before OnDayChanged).
    /// </summary>
    public static int DeferAutoTriggerForDay = -1;

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
        // Not normally reached — this task fires unconditionally off CampaignManager.OnDayChanged
        // rather than via DailyTaskScheduler (see class remarks). Roll today's categories here too
        // in case some other system calls this entry point directly, so packages are never spawned
        // against an empty goods pool.
        ChooseTodaysProhibitedGoods();

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

    /// <summary>Client-local handle to this delivery's row in the tutorial objective list overlay
    /// (see <see cref="TutorialObjectiveList"/>). Created when the delivery alert fires, kept
    /// up to date as packages are sorted, and completed/hidden once every package is sorted.</summary>
    private TutorialObjectiveItem _mailObjective;

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
        _mailObjective?.SetText(GetMailObjectiveText());
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
    /// Fires on every day change, including Day 1. Rolls today's prohibited-goods categories
    /// immediately (so replicated UI such as the prohibited-goods sign updates right at day
    /// start, not whenever the mail task itself actually kicks off — and even on Day 1, which
    /// never gets an actual delivery) — see <see cref="_lastGoodsRollDay"/>. Then, for every day
    /// after Day 1, triggers the mail delivery once per day — independent of
    /// DailyTaskScheduler, so it never competes with other daily tasks. If a
    /// <see cref="_deliveryTruck"/> is assigned, the truck's drive-in cutscene decides when
    /// packages actually spawn (on arrival); otherwise packages spawn immediately.
    /// </summary>
    private void OnDayChanged(int day)
    {
        if (!IsServer) return;

        // Clear out any packages left sitting in mailboxes/bins from the previous day's delivery.
        DespawnResolvedPackages();

        if (day != _lastGoodsRollDay)
        {
            _lastGoodsRollDay = day;
            ChooseTodaysProhibitedGoods();
        }

        if (day <= 1) return;
        if (day == _lastTriggeredDay) return;

        _lastTriggeredDay = day;

        if (day == DeferAutoTriggerForDay)
        {
            DeferAutoTriggerForDay = -1;
            Debug.Log($"[SortMailTask] Day {day} delivery deferred — waiting for a manual TriggerDeferredDelivery() call.");
            return;
        }

        if (_deliveryTruck != null)
            _deliveryTruck.BeginDeliverySequence();
        else
            TriggerTask();
    }

    /// <summary>
    /// Manually fires a delivery that was deferred via <see cref="DeferAutoTriggerForDay"/>. Uses
    /// the same dispatch as the automatic day-change trigger (truck cutscene if assigned, else an
    /// immediate spawn). Server-only.
    /// </summary>
    public void TriggerDeferredDelivery()
    {
        if (!IsServer) return;

        if (_deliveryTruck != null)
            _deliveryTruck.BeginDeliverySequence();
        else
            TriggerTask();
    }

    /// <summary>
    /// Server-only. Despawns every package that was correctly sorted — delivered to a mailbox
    /// cubby or confiscated into a Confiscate bin — and left sitting there (see
    /// <see cref="MailPackageItem.MarkDelivered"/>/<see cref="MailPackageItem.MarkConfiscated"/>).
    /// Called at the start of every day change so mailboxes/bins don't accumulate packages
    /// indefinitely.
    /// </summary>
    public void DespawnResolvedPackages()
    {
        if (!IsServer) return;
        if (_resolvedPackages.Count == 0) return;

        foreach (NetworkObject packageObj in _resolvedPackages)
        {
            if (packageObj != null && packageObj.IsSpawned)
                packageObj.Despawn(destroy: true);
        }

        Debug.Log($"[SortMailTask] Despawned {_resolvedPackages.Count} resolved package(s) left in mailboxes/bins.");
        _resolvedPackages.Clear();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Despawns any leftover packages, spawns a fresh delivery, and registers this task in the
    /// HUD. Server-only. Assumes <see cref="ChooseTodaysProhibitedGoods"/> has already been
    /// called for today (done at day start in <see cref="OnDayChanged"/>).
    /// </summary>
    public void TriggerTask()
    {
        if (!IsServer) return;

        DespawnExistingPackages();
        _taskActive = true;
        _sortedCount.Value = 0;

        List<SuspectRecord> addressPool = BuildAddressablePool();
        if (addressPool.Count == 0)
        {
            Debug.LogWarning("[SortMailTask] No eligible residents to address packages to — mail delivery skipped.");
            return;
        }

        int packageCount = Random.Range(_minPackageCount, _maxPackageCount + 1);

        // Draw addressees from a shuffled pass over every eligible resident so each one gets at
        // most one package before anyone gets a second — only reshuffling and starting a fresh
        // pass once every resident has already received one. This prevents the same resident
        // from being picked for multiple pieces of mail in a single delivery whenever there are
        // at least as many residents as packages.
        List<SuspectRecord> residentDrawOrder = new List<SuspectRecord>(addressPool);
        Shuffle(residentDrawOrder);
        int residentCursor = 0;

        for (int i = 0; i < packageCount; i++)
        {
            if (residentCursor >= residentDrawOrder.Count)
            {
                Shuffle(residentDrawOrder);
                residentCursor = 0;
            }

            SpawnSinglePackage(residentDrawOrder[residentCursor]);
            residentCursor++;
        }

        _totalCount.Value = _spawnedPackages.Count;
        UpdateThreatLevel();

        _isActive.Value = true;

        ShiftManager.Instance?.RegisterPendingDailyTask(this);

        NotifyDeliveryAlertClientRpc();

        Debug.Log($"[SortMailTask] Delivery triggered — spawned {_spawnedPackages.Count} package(s). " +
                  $"Prohibited today: {string.Join(", ", _todaysProhibitedGoods)}");
    }

    /// <summary>
    /// Called by <see cref="MailPackageItem.RequestSortServerRpc"/> when a package is dropped
    /// into a bin or cubby slot. Server-only; validates the placement and either resolves the
    /// package in place — locked and left sitting there (correct) — or bounces it back out
    /// (incorrect).
    ///
    /// For <see cref="MailSortBinType.Delivery"/>, correctness additionally requires that
    /// <paramref name="slotResidentName"/> (the resident assigned to the specific
    /// <see cref="MailCubbySlot"/> the package was dropped into) matches the package's addressee —
    /// dropping a deliverable package into the wrong resident's cubby is treated as incorrect,
    /// even though the bin type matches.
    ///
    /// <paramref name="hasSnapPose"/>/<paramref name="snapPosition"/>/<paramref name="snapRotation"/>
    /// optionally carry a fixed placement pose (e.g. a cubby's <see cref="PlacementSlot"/>) so a
    /// correctly sorted package can be snapped exactly into place and have its throw momentum
    /// cleared — see <see cref="MailPackageItem.MarkDelivered"/>/<see cref="MailPackageItem.MarkConfiscated"/>.
    /// </summary>
    public void EvaluateSort(MailPackageItem package, MailSortBinType binType, string slotResidentName = "",
        bool hasSnapPose = false, Vector3 snapPosition = default, Quaternion snapRotation = default)
    {
        if (!IsServer) return;
        if (package == null || package.IsResolved) return;

        bool isCorrect = binType == MailSortBinType.Delivery
            ? package.CorrectBin == MailSortBinType.Delivery &&
              string.Equals(slotResidentName?.Trim(), package.ResidentName?.Trim(), StringComparison.OrdinalIgnoreCase)
            : package.CorrectBin == binType;

        if (isCorrect)
        {
            if (binType == MailSortBinType.Delivery)
            {
                // Delivered packages stay sitting in the mailbox (locked, no longer interactable)
                // instead of despawning immediately — cleared at the start of the next day.
                package.MarkDelivered(hasSnapPose, snapPosition, snapRotation);
            }
            else
            {
                // Confiscated packages likewise stay sitting in the bin instead of despawning
                // immediately — cleared at the start of the next day.
                package.MarkConfiscated(hasSnapPose, snapPosition, snapRotation);
            }

            _spawnedPackages.Remove(package.NetworkObject);
            _resolvedPackages.Add(package.NetworkObject);

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

            string reason = binType == MailSortBinType.Delivery && package.CorrectBin == MailSortBinType.Delivery
                ? $"wrong cubby (dropped in '{slotResidentName}' cubby, belongs to '{package.ResidentName}')"
                : $"dropped in {binType}, belongs in {package.CorrectBin}";
            Debug.Log($"[SortMailTask] Wrong bin for '{package.ResidentName}' ({package.GoodsLabel}) — {reason}.");
        }
    }

    // ── Private ────────────────────────────────────────────────────────────────

    /// <summary>Fisher-Yates shuffle used to randomize resident draw order per delivery (see <see cref="TriggerTask"/>).</summary>
    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

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

    private void SpawnSinglePackage(SuspectRecord resident)
    {
        if (_packagePrefab == null)
        {
            Debug.LogError("[SortMailTask] _packagePrefab is not assigned.");
            return;
        }

        string residentName = $"{resident.SuspectData.FirstName} {resident.SuspectData.LastName}".Trim();

        bool isProhibited = Random.Range(0, _todaysAllowedGoods.Count + _todaysProhibitedGoods.Count) >= _todaysAllowedGoods.Count;
        string goodsLabel = isProhibited
            ? _todaysProhibitedGoods[Random.Range(0, _todaysProhibitedGoods.Count)]
            : _todaysAllowedGoods[Random.Range(0, _todaysAllowedGoods.Count)];

        // Quarantine sorting has been removed from this task — mail is only ever Confiscate
        // (prohibited goods) or Delivery (everything else), regardless of the addressee's
        // quarantine status.
        MailSortBinType correctBin = isProhibited
            ? MailSortBinType.Confiscate
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

        OnDailyTaskCompleted?.Invoke();

        // EvaluateSort (and therefore CompleteTask) only ever runs on the server, so
        // OnAllPackagesSorted must be broadcast via ClientRpc rather than invoked directly —
        // otherwise remote clients would never fire it and their tutorial objective row would
        // never get marked complete / hidden, even though the sorted/total counts (driven by the
        // replicated NetworkVariables) display correctly for them.
        NotifyAllPackagesSortedClientRpc();

        _isActive.Value = false;
    }

    /// <summary>Runs on every client (including the host) so <see cref="OnAllPackagesSorted"/> fires identically everywhere.</summary>
    [ClientRpc]
    private void NotifyAllPackagesSortedClientRpc()
    {
        OnAllPackagesSorted?.Invoke();

        TutorialObjectiveList.Instance?.CompleteObjective(_mailObjective);
        TutorialObjectiveList.Instance?.HideAndClear(preHideDelay: 1.5f);
        _mailObjective = null;
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
    /// Shows a lightweight, non-blocking alert on every client telling players a shipment is
    /// waiting at the checkpoint gate. Called by <see cref="DeliveryTruckController"/> when the
    /// truck arrives at the gate and stops, waiting for a player to open it (e.g. via the gate
    /// button) before continuing on to the drop-off point.
    /// </summary>
    public void NotifyShipmentWaitingAtGate()
    {
        if (!IsServer) return;
        NotifyShipmentWaitingAtGateClientRpc();
    }

    [ClientRpc]
    private void NotifyShipmentWaitingAtGateClientRpc()
    {
        UIController.Instance?.ShowMailDeliveryNotification("A shipment is waiting at the gate.");
    }

    /// <summary>
    /// Shows a lightweight, non-blocking alert on every client — the mail-sorting equivalent of
    /// the "Someone is waiting at the booth" prompt — announcing today's delivery and which
    /// goods categories are prohibited. Uses the same reveal-and-fade notification style as the
    /// booth waiting alert (see <see cref="UIController.ShowMailDeliveryNotification"/>), but on
    /// its own notification instance so it never gets dismissed by booth-arrival logic.
    ///
    /// Also pops up the tutorial objective list overlay (see <see cref="TutorialObjectiveList"/>)
    /// showing how much mail is left to put away. The row stays up — updated live as packages
    /// are sorted — until <see cref="NotifyAllPackagesSortedClientRpc"/> completes and hides it.
    /// </summary>
    [ClientRpc]
    private void NotifyDeliveryAlertClientRpc()
    {
        if (UIController.Instance == null) return;

        string prohibited = _todaysProhibitedGoods.Count > 0 ? string.Join(", ", _todaysProhibitedGoods) : "none";
        string message = $"A mail delivery has arrived — sort it at the mail bins.\nToday's prohibited goods: {prohibited}";
        UIController.Instance.ShowMailDeliveryNotification(message);

        _mailObjective = TutorialObjectiveList.Instance?.AddObjective(GetMailObjectiveText());
    }

    /// <summary>Display text for <see cref="_mailObjective"/>, e.g. "Put away the mail (3/22)".</summary>
    private string GetMailObjectiveText() =>
        $"Put away the mail ({Mathf.Min(SortedCount, TotalCount)}/{TotalCount})";
}
