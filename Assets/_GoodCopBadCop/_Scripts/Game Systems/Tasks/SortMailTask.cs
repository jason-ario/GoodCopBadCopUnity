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
///     is preceded by a drive-in cutscene instead of packages appearing instantly. With a truck,
///     packages spawn pinned inside its crate as it activates (tune the "Crate Ride-Along" area
///     via the selection gizmo) and are released onto physics once the crate lands.
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

    [Header("Crate Ride-Along (truck deliveries)")]
    [Tooltip("Centre of the package area inside the delivery crate, in METRES relative to the crate's pivot " +
             "and rotation (crate scale is compensated). Y is the height of the lowest package layer's centre.")]
    [SerializeField] private Vector3 _cratePackageAreaCenter = new Vector3(0f, 0.15f, 0f);
    [Tooltip("Width (X) and depth (Z) in metres of the area inside the crate packages are randomly placed in.")]
    [SerializeField] private Vector2 _cratePackageAreaSize = new Vector2(1f, 1f);
    [Tooltip("Minimum horizontal distance between package centres on the same layer before a new layer is started.")]
    [SerializeField] private float _cratePackageSpacing = 0.3f;
    [Tooltip("Vertical step between stacked package layers inside the crate.")]
    [SerializeField] private float _cratePackageLayerHeight = 0.25f;
    [Tooltip("Random placement attempts per package before it is moved up to the next layer.")]
    [SerializeField] private int _cratePlacementAttempts = 16;

    [Header("Gate Button Tutorial")]
    [Tooltip("The checkpoint gate button's Interactable. While the shipment-is-waiting-at-the-gate " +
             "alert is showing (see NotifyShipmentWaitingAtGate), it is force-highlighted and pointed at " +
             "with a pooled TutorialMarker arrow (see TutorialMarkerManager) so players know where to go " +
             "to let the truck through. Both are cleared as soon as the gate opens (NotifyShipmentGateOpened).")]
    [SerializeField] private GateButtonInteractable _gateButtonInteractable;
    [Tooltip("Task-list label registered in TaskRegistry while the delivery truck is waiting at the " +
             "closed checkpoint gate. Completed (removed) as soon as the gate opens.")]
    [SerializeField] private string _openGateTaskName = "Open the gate for a shipment";

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

    /// <summary>
    /// True while the delivery truck is stopped at the closed checkpoint gate waiting for a player
    /// to open it. Drives the "Open the gate for a shipment" task row on every client — replicated
    /// (rather than ClientRpc-only) so late joiners still see the pending task.
    /// </summary>
    private readonly NetworkVariable<bool> _shipmentWaitingAtGate = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private OpenGateForShipmentTask _openGateTask;

    /// <summary>Today's randomly-chosen prohibited goods categories, replicated to all clients so
    /// the prohibited-goods sign (and the delivery alert) can display them.</summary>
    private readonly NetworkList<FixedString64Bytes> _prohibitedGoodsToday = new(
        writePerm: NetworkVariableWritePermission.Server);

    /// <summary>Today's prohibited goods categories. Read-only view for UI such as the sign display.</summary>
    public NetworkList<FixedString64Bytes> ProhibitedGoodsToday => _prohibitedGoodsToday;

    // ── Local state (server-only) ─────────────────────────────────────────────

    private readonly List<NetworkObject> _spawnedPackages = new();

    /// <summary>Packages spawned pinned inside the delivery crate while the truck is still en route
    /// (see <see cref="TryPrepareCrateDelivery"/>). Not part of the live task (HUD, save state,
    /// threat level) until <see cref="TryReleaseCrateDelivery"/> moves them to <see cref="_spawnedPackages"/>.</summary>
    private readonly List<NetworkObject> _pendingCratePackages = new();

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

    /// <summary>
    /// When set to a day number, <see cref="OnDayChanged"/> skips its normal automatic delivery
    /// trigger for that specific day entirely — no delivery, no crate, no "Sort the Mail" task —
    /// and does NOT expect a later manual <see cref="TriggerDeferredDelivery"/> call. Unlike
    /// <see cref="DeferAutoTriggerForDay"/>, the day is simply skipped rather than postponed.
    /// The daily prohibited-goods roll (<see cref="ChooseTodaysProhibitedGoods"/>) still runs as
    /// normal since it is independent of whether a delivery happens. Reset to -1 automatically
    /// once consumed. Must be set before the day actually changes (e.g. in a day script's
    /// DayActivated, which CampaignManager calls before OnDayChanged).
    /// </summary>
    public static int SkipDeliveryForDay = -1;

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

    /// <summary>
    /// Fired on the server right after a delivery's packages have actually spawned in the world
    /// (i.e. once the mail has truly been dropped off — see <see cref="TriggerTask"/>, called by
    /// <see cref="DeliveryTruckController"/> once the crate settles at pointC). Used instead of
    /// hooking the truck's earlier "sequence started"/"waiting at gate" beats so anything that
    /// should only appear once the mail is actually here (e.g. Day 2's sorting tutorial overlay,
    /// broadcast to clients separately once the server-side handler reacts) isn't shown
    /// prematurely while the truck is still en route.
    /// </summary>
    public static event Action OnMailDelivered;

    public int SortedCount => _sortedCount.Value;
    public int TotalCount  => _totalCount.Value;

    /// <summary>
    /// The crate transform packages are pinned to while riding the delivery truck. Resolvable on
    /// every peer (scene references), used by <see cref="MailPackageItem"/> to rebuild its pin.
    /// </summary>
    public Transform DeliveryCrateTransform =>
        _deliveryTruck != null && _deliveryTruck.DeliveryCrate != null ? _deliveryTruck.DeliveryCrate : _crateSpawnPoint;

    /// <summary>Captures every unresolved package with its authoritative labels and placement.</summary>
    public MailTaskSaveState CaptureSaveState()
    {
        var packages = new List<MailPackageSaveData>(_spawnedPackages.Count);
        foreach (NetworkObject netObj in _spawnedPackages)
        {
            if (netObj == null || !netObj.IsSpawned) continue;
            MailPackageItem package = netObj.GetComponent<MailPackageItem>();
            if (package == null || package.IsResolved) continue;

            packages.Add(new MailPackageSaveData
            {
                ResidentPoolIndex = package.ResidentPoolIndex,
                ResidentName = package.ResidentName,
                GoodsLabel = package.GoodsLabel,
                CorrectBin = (int)package.CorrectBin,
                Position = netObj.transform.position,
                RotationEuler = netObj.transform.eulerAngles
            });
        }

        return new MailTaskSaveState
        {
            IsActive = _isActive.Value,
            SortedCount = _sortedCount.Value,
            TotalCount = _totalCount.Value,
            Packages = packages.ToArray()
        };
    }

    /// <summary>
    /// Recreates only the unresolved packages from a host snapshot. Package identity is preserved
    /// through the replicated resident-pool index and labels, avoiding a new mail roll on resume.
    /// </summary>
    public void RestoreSaveState(MailTaskSaveState state)
    {
        if (!IsServer || state == null) return;

        DespawnExistingPackages();
        _resolvedPackages.Clear();
        _sortedCount.Value = Mathf.Max(0, state.SortedCount);

        if (state.Packages != null)
        {
            foreach (MailPackageSaveData packageState in state.Packages)
                SpawnSavedPackage(packageState);
        }

        // The only finishable total is completed packages plus packages actually rebuilt. This
        // also repairs older/incomplete saves that recorded a stale total without every package.
        _totalCount.Value = _sortedCount.Value + _spawnedPackages.Count;
        _taskActive = state.IsActive && _spawnedPackages.Count > 0;
        _isActive.Value = _taskActive;
        UpdateThreatLevel();

        if (_taskActive)
        {
            ShiftManager.Instance?.RegisterPendingDailyTask(this);
        }
    }

    private void SpawnSavedPackage(MailPackageSaveData state)
    {
        if (_packagePrefab == null || state == null) return;

        SuspectData resident = MailCubbyManager.Instance?.ResolveResident(state.ResidentPoolIndex);
        if (resident == null)
        {
            Debug.LogWarning($"[SortMailTask] Skipped saved package for missing resident-pool index {state.ResidentPoolIndex}.");
            return;
        }

        GameObject itemGo = Instantiate(_packagePrefab, state.Position, Quaternion.Euler(state.RotationEuler));
        NetworkObject netObj = itemGo.GetComponent<NetworkObject>();
        MailPackageItem package = itemGo.GetComponent<MailPackageItem>();
        if (netObj == null || package == null)
        {
            Destroy(itemGo);
            return;
        }

        netObj.Spawn(destroyWithScene: true);
        package.ServerInitialize(resident, state.ResidentName, state.GoodsLabel, (MailSortBinType)state.CorrectBin);
        _spawnedPackages.Add(netObj);
    }

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
        _shipmentWaitingAtGate.OnValueChanged += OnShipmentWaitingAtGateChanged;

        if (_isActive.Value)
            TaskRegistry.Instance?.AddThreat(this);

        if (_shipmentWaitingAtGate.Value)
            TaskRegistry.Instance?.AddThreat(OpenGateTask);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _sortedCount.OnValueChanged -= OnNetworkValueChanged;
        _totalCount.OnValueChanged  -= OnNetworkValueChanged;
        _isActive.OnValueChanged    -= OnIsActiveChanged;
        _shipmentWaitingAtGate.OnValueChanged -= OnShipmentWaitingAtGateChanged;

        if (_openGateTask != null)
            TaskRegistry.Instance?.RemoveThreat(_openGateTask);
    }

    private OpenGateForShipmentTask OpenGateTask =>
        _openGateTask ??= new OpenGateForShipmentTask(_openGateTaskName);

    private void OnShipmentWaitingAtGateChanged(bool previous, bool current)
    {
        if (current)
            TaskRegistry.Instance?.AddThreat(OpenGateTask);
        else
            TaskRegistry.Instance?.RemoveThreat(OpenGateTask);
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

        // The saved workday owns its active delivery. Avoid clearing it or rolling a new package
        // set while CampaignManager is preparing the host-side restore; ShiftManager will replay
        // this task only when the persisted blocker journal says it was still incomplete.
        if (CampaignManager.Instance != null && CampaignManager.Instance.HasPendingWorkdayRestore)
            return;

        // Clear out any packages left sitting in mailboxes/bins from the previous day's delivery.
        DespawnResolvedPackages();

        // If yesterday had a delivery, its crate has been sitting active on the ground ever
        // since (see DeliveryTruckController — only the truck itself deactivates after driving
        // off, not the crate it dropped). Hide it now that a new day has started; it gets
        // reactivated and repositioned on the truck's roof next time a delivery sequence begins.
        if (_deliveryTruck != null && _lastTriggeredDay == day - 1)
            _deliveryTruck.DeactivateCrate();

        if (day != _lastGoodsRollDay)
        {
            _lastGoodsRollDay = day;
            ChooseTodaysProhibitedGoods();
        }

        if (day <= 1) return;
        if (day == _lastTriggeredDay) return;

        _lastTriggeredDay = day;

        if (day == SkipDeliveryForDay)
        {
            SkipDeliveryForDay = -1;
            Debug.Log($"[SortMailTask] Day {day} delivery skipped entirely — no delivery will occur today.");
            return;
        }

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
        TryTriggerTask();
    }

    /// <summary>
    /// Attempts to spawn a fresh mail delivery. Returns <see langword="true"/> only after at
    /// least one package has been spawned and the task is registered. The delivery truck uses the
    /// result to retry a transient startup-order failure after the crate has landed.
    /// </summary>
    public bool TryTriggerTask()
    {
        if (!TryRollDelivery(out List<SuspectRecord> addressees))
            return false;

        DespawnExistingPackages();

        int failedSpawnCount = 0;
        foreach (SuspectRecord resident in addressees)
        {
            NetworkObject netObj = SpawnSinglePackage(resident, GetRandomSpawnPosition(),
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            if (netObj != null)
                _spawnedPackages.Add(netObj);
            else
                failedSpawnCount++;
        }

        if (_spawnedPackages.Count == 0)
        {
            _taskActive = false;
            _isActive.Value = false;
            _totalCount.Value = 0;
            UpdateThreatLevel();
            Debug.LogWarning($"[SortMailTask] Mail delivery spawned no packages ({failedSpawnCount}/{addressees.Count} attempts failed); it can be retried.");
            return false;
        }

        ActivateDelivery(failedSpawnCount);
        return true;
    }

    /// <summary>
    /// Server-only. Called by <see cref="DeliveryTruckController"/> as the truck activates: rolls
    /// today's delivery and spawns every package pinned at a random spot inside the delivery
    /// crate (locked, kinematic, no highlight) so they ride along with the truck instead of
    /// popping into existence on arrival. The task itself does not go live (HUD, save state,
    /// <see cref="OnMailDelivered"/>) until <see cref="TryReleaseCrateDelivery"/>. Returns
    /// <see langword="false"/> if nothing could be spawned — the truck then falls back to
    /// <see cref="TryTriggerTask"/> once the crate lands.
    /// </summary>
    public bool TryPrepareCrateDelivery()
    {
        Transform crate = DeliveryCrateTransform;
        if (crate == null)
        {
            Debug.LogWarning("[SortMailTask] No delivery crate transform — packages will spawn on landing instead.");
            return false;
        }

        if (!TryRollDelivery(out List<SuspectRecord> addressees))
            return false;

        DespawnExistingPackages();

        List<Vector3> localPositions = BuildCratePackageLayout(addressees.Count);
        Vector3 scale = crate.lossyScale;
        Vector3 inverseScale = new Vector3(
            Mathf.Approximately(scale.x, 0f) ? 1f : 1f / scale.x,
            Mathf.Approximately(scale.y, 0f) ? 1f : 1f / scale.y,
            Mathf.Approximately(scale.z, 0f) ? 1f : 1f / scale.z);

        int failedSpawnCount = 0;
        for (int i = 0; i < addressees.Count; i++)
        {
            // Layout is authored in metres; SocketFollow uses crate.TransformPoint, so undo scale.
            Vector3    localPos = Vector3.Scale(localPositions[i], inverseScale);
            Quaternion localRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            NetworkObject netObj = SpawnSinglePackage(addressees[i],
                crate.TransformPoint(localPos), crate.rotation * localRot,
                pinToCrate: true, crateLocalPosition: localPos, crateLocalRotation: localRot);

            if (netObj != null)
                _pendingCratePackages.Add(netObj);
            else
                failedSpawnCount++;
        }

        if (_pendingCratePackages.Count == 0)
        {
            Debug.LogWarning($"[SortMailTask] Crate delivery spawned no packages ({failedSpawnCount}/{addressees.Count} attempts failed); will retry on landing.");
            return false;
        }

        Debug.Log($"[SortMailTask] {_pendingCratePackages.Count} package(s) loaded into the delivery crate" +
                  (failedSpawnCount > 0 ? $" ({failedSpawnCount} failed)." : "."));
        return true;
    }

    /// <summary>
    /// Server-only. Called once the delivery crate has landed: un-pins every package prepared by
    /// <see cref="TryPrepareCrateDelivery"/> (physics, solid colliders, interaction and the
    /// delivery highlight come on) and makes the task live. Returns <see langword="false"/> if
    /// there were no pending crate packages.
    /// </summary>
    public bool TryReleaseCrateDelivery()
    {
        if (!IsServer) return false;

        _pendingCratePackages.RemoveAll(netObj => netObj == null || !netObj.IsSpawned);
        if (_pendingCratePackages.Count == 0) return false;

        foreach (NetworkObject netObj in _pendingCratePackages)
        {
            if (netObj.TryGetComponent(out MailPackageItem package))
                package.ReleaseFromCrate();
            _spawnedPackages.Add(netObj);
        }
        _pendingCratePackages.Clear();

        ActivateDelivery(0);
        return true;
    }

    /// <summary>
    /// Server-only. Validates prerequisites and draws today's addressees. Does not spawn anything.
    /// </summary>
    private bool TryRollDelivery(out List<SuspectRecord> addressees)
    {
        addressees = null;

        if (!IsServer)
        {
            Debug.LogWarning("[SortMailTask] Tried to trigger mail delivery outside the server.");
            return false;
        }

        if (_packagePrefab == null)
        {
            Debug.LogError("[SortMailTask] Cannot trigger mail delivery: _packagePrefab is not assigned.");
            return false;
        }

        List<SuspectRecord> addressPool = BuildAddressablePool();
        if (addressPool.Count == 0)
        {
            string reason = SuspectRunRecords.Instance == null
                ? "SuspectRunRecords is not available yet"
                : "there are no living residents in the runtime record pool";
            Debug.LogWarning($"[SortMailTask] Mail delivery postponed: {reason}.");
            return false;
        }

        // A deferred/manual delivery can arrive without the regular day-change goods roll.
        // Do not attempt Random.Range with an empty category cache.
        if (_todaysAllowedGoods.Count + _todaysProhibitedGoods.Count == 0)
            ChooseTodaysProhibitedGoods();

        if (_todaysAllowedGoods.Count + _todaysProhibitedGoods.Count == 0)
        {
            Debug.LogError("[SortMailTask] Cannot trigger mail delivery: no valid goods categories are configured.");
            return false;
        }

        int packageCount = Random.Range(_minPackageCount, _maxPackageCount + 1);

        // Cap at the number of eligible residents: each resident has exactly one physical
        // MailCubbySlot (see MailCubbyManager), so once a package has been correctly delivered
        // there, that cubby is occupied and cannot accept a second package for the same resident.
        // Without this cap the draw could hand the same resident two packages in one delivery —
        // the second one is then physically impossible to deliver correctly. This was the
        // "exactly one package never registers, even sorted correctly" bug.
        packageCount = Mathf.Min(packageCount, addressPool.Count);

        // Draw addressees from a single shuffled pass so each resident gets at most one package.
        Shuffle(addressPool);
        addressees = addressPool.GetRange(0, packageCount);
        return true;
    }

    /// <summary>
    /// Server-only. Makes the delivery live once its packages are in <see cref="_spawnedPackages"/>:
    /// resets progress, registers the HUD/pending task and fires <see cref="OnMailDelivered"/>.
    /// </summary>
    private void ActivateDelivery(int failedSpawnCount)
    {
        _taskActive = true;
        _sortedCount.Value = 0;
        _totalCount.Value = _spawnedPackages.Count;
        UpdateThreatLevel();

        _isActive.Value = true;

        ShiftManager.Instance?.RegisterPendingDailyTask(this);

        NotifyDeliveryAlertClientRpc();

        OnMailDelivered?.Invoke();

        string spawnResult = failedSpawnCount > 0
            ? $"{_spawnedPackages.Count} package(s) live; {failedSpawnCount} attempt(s) failed"
            : $"{_spawnedPackages.Count} package(s) live";
        Debug.Log($"[SortMailTask] Delivery triggered — {spawnResult}. " +
                  $"Prohibited today: {string.Join(", ", _todaysProhibitedGoods)}");
    }

    /// <summary>
    /// Random, non-overlapping package positions inside the crate, in metres in crate space.
    /// Fills the bottom layer first; once a package can't find a free spot after
    /// <see cref="_cratePlacementAttempts"/> tries, a new layer is started on top.
    /// </summary>
    private List<Vector3> BuildCratePackageLayout(int count)
    {
        var positions = new List<Vector3>(count);
        var currentLayer = new List<Vector2>();
        Vector2 half = _cratePackageAreaSize * 0.5f;
        float spacingSqr = _cratePackageSpacing * _cratePackageSpacing;
        float layerY = _cratePackageAreaCenter.y;
        int attempts = Mathf.Max(1, _cratePlacementAttempts);

        for (int i = 0; i < count; i++)
        {
            Vector2 candidate = default;
            bool placed = false;

            for (int attempt = 0; attempt < attempts && !placed; attempt++)
            {
                candidate = new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y));
                placed = true;
                foreach (Vector2 other in currentLayer)
                {
                    if ((other - candidate).sqrMagnitude < spacingSqr)
                    {
                        placed = false;
                        break;
                    }
                }
            }

            if (!placed)
            {
                currentLayer.Clear();
                layerY += _cratePackageLayerHeight;
                candidate = new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y));
            }

            currentLayer.Add(candidate);
            positions.Add(new Vector3(
                _cratePackageAreaCenter.x + candidate.x,
                layerY,
                _cratePackageAreaCenter.z + candidate.y));
        }

        return positions;
    }

    /// <summary>
    /// Called by <see cref="MailPackageItem.RequestSortServerRpc"/> when a package is dropped
    /// into a bin or cubby slot. Server-only; validates the placement and either resolves the
    /// package in place — locked and left sitting there (correct) — or bounces it back out
    /// (incorrect).
    ///
    /// For <see cref="MailSortBinType.Delivery"/>, correctness additionally requires that the
    /// resident assigned to the specific <see cref="MailCubbySlot"/> the package was dropped into
    /// (resolved from <paramref name="slotResidentPoolIndex"/> via
    /// <see cref="MailCubbyManager.ResolveResident"/>) is the exact same <see cref="SuspectData"/>
    /// reference as the package's addressee (<see cref="MailPackageItem.AssignedResident"/>) —
    /// dropping a deliverable package into the wrong resident's cubby is treated as incorrect,
    /// even though the bin type matches. Matching is a direct object reference comparison, not a
    /// display-name string comparison — both sides are ultimately drawn from the same
    /// <see cref="SuspectSet"/> asset (see <see cref="SpawnSinglePackage"/> and
    /// <see cref="MailCubbySlot.AssignedResident"/>), so comparing the actual SuspectData
    /// reference sidesteps any whitespace/casing/typo mismatch that comparing two independently
    /// built name strings could silently trip on.
    ///
    /// <paramref name="hasSnapPose"/>/<paramref name="snapPosition"/>/<paramref name="snapRotation"/>
    /// optionally carry a fixed placement pose (e.g. a cubby's <see cref="PlacementSlot"/>) so a
    /// correctly sorted package can be snapped exactly into place and have its throw momentum
    /// cleared — see <see cref="MailPackageItem.MarkDelivered"/>/<see cref="MailPackageItem.MarkConfiscated"/>.
    /// </summary>
    public void EvaluateSort(MailPackageItem package, MailSortBinType binType, int slotResidentPoolIndex = -1,
        bool hasSnapPose = false, Vector3 snapPosition = default, Quaternion snapRotation = default)
    {
        if (!IsServer) return;
        if (package == null || package.IsResolved) return;

        SuspectData slotResident = binType == MailSortBinType.Delivery
            ? MailCubbyManager.Instance?.ResolveResident(slotResidentPoolIndex)
            : null;

        bool isCorrect = binType == MailSortBinType.Delivery
            ? package.CorrectBin == MailSortBinType.Delivery &&
              package.AssignedResident != null &&
              slotResident == package.AssignedResident
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

            string slotResidentName = slotResident != null ? $"{slotResident.FirstName} {slotResident.LastName}".Trim() : "(none)";
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

    /// <summary>
    /// Server-only. Spawns one labelled package at the given pose. When <paramref name="pinToCrate"/>
    /// is set, the package is pinned to the delivery crate at the crate-local pose before spawn
    /// (replicated in the spawn payload), locked, and left un-highlighted until released.
    /// Returns the spawned NetworkObject, or null on failure. Does not add it to any list.
    /// </summary>
    private NetworkObject SpawnSinglePackage(SuspectRecord resident, Vector3 spawnPos, Quaternion spawnRot,
        bool pinToCrate = false, Vector3 crateLocalPosition = default, Quaternion crateLocalRotation = default)
    {
        if (resident == null || resident.SuspectData == null)
        {
            Debug.LogWarning("[SortMailTask] Skipped package spawn for an invalid resident record.");
            return null;
        }

        GameObject itemGo = null;
        NetworkObject netObj = null;
        try
        {
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

            itemGo = Instantiate(_packagePrefab, spawnPos, spawnRot);
            netObj = itemGo.GetComponent<NetworkObject>();
            MailPackageItem package = itemGo.GetComponent<MailPackageItem>();

            if (netObj == null || package == null)
            {
                Debug.LogError("[SortMailTask] Package prefab is missing a NetworkObject or MailPackageItem component.");
                Destroy(itemGo);
                return null;
            }

            if (pinToCrate)
                package.SetCratePinBeforeSpawn(crateLocalPosition, crateLocalRotation);

            netObj.Spawn(destroyWithScene: true);
            package.ServerInitialize(resident.SuspectData, residentName, goodsLabel, correctBin, highlight: !pinToCrate);

            if (pinToCrate)
            {
                package.LockInteractableNetworked();
                package.EnsureCratePinAppliedOnServer();
            }

            return netObj;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SortMailTask] Failed to spawn a package for '{resident.SuspectData.name}': {exception.Message}");
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
            else if (itemGo != null)
                Destroy(itemGo);
            return null;
        }
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
        // Tasks no longer pay coupons — players are only paid for processing suspects (see SuspectController.PayOutResults).
        // if (ATM.Instance != null)
        //     ATM.Instance.SpawnCoupons(_couponReward);

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

        foreach (NetworkObject netObj in _pendingCratePackages)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _pendingCratePackages.Clear();

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
    /// button) before continuing on to the drop-off point. The alert keeps fading out and back
    /// in on a loop — it does not disappear for good — until <see cref="NotifyShipmentGateOpened"/>
    /// is called once the gate is actually opened. Also points a pooled <see cref="TutorialMarker"/>
    /// at <see cref="_gateButtonInteractable"/> for the same duration, so players always have a
    /// visual cue for where to go.
    /// </summary>
    public void NotifyShipmentWaitingAtGate()
    {
        if (!IsServer) return;
        _shipmentWaitingAtGate.Value = true;
        NotifyShipmentWaitingAtGateClientRpc();
    }

    [ClientRpc]
    private void NotifyShipmentWaitingAtGateClientRpc()
    {
        UIController.Instance?.ShowMailDeliveryNotification("A shipment is waiting at the gate.", loop: true);

        if (_gateButtonInteractable != null)
            TutorialMarkerManager.Instance?.Mark(_gateButtonInteractable.transform);
    }

    /// <summary>
    /// Dismisses the looping "shipment is waiting at the gate" alert on every client, and clears
    /// the gate button's tutorial arrow (see <see cref="NotifyShipmentWaitingAtGate"/>).
    /// Called by <see cref="DeliveryTruckController"/> as soon as a player opens the checkpoint gate.
    /// </summary>
    public void NotifyShipmentGateOpened()
    {
        if (!IsServer) return;
        _shipmentWaitingAtGate.Value = false;
        NotifyShipmentGateOpenedClientRpc();
    }

    [ClientRpc]
    private void NotifyShipmentGateOpenedClientRpc()
    {
        UIController.Instance?.HideMailDeliveryNotification();

        if (_gateButtonInteractable != null)
            TutorialMarkerManager.Instance?.Unmark(_gateButtonInteractable.transform);
    }

    /// <summary>
    /// Placeholder for delivery-alert side effects on every client. The mail task's progress is
    /// already surfaced via the <see cref="ISystemicThreat"/> threat panel (see
    /// <see cref="ThreatName"/>/<see cref="ThreatDescription"/>) registered through
    /// <see cref="TaskRegistry"/>, so this no longer also adds a duplicate
    /// <see cref="TutorialObjectiveList"/> row — the two showed the same "sorted/total" count
    /// twice on screen (e.g. "Sort mail — Packages sorted 2/9" and "Put away the mail (2/9)").
    ///
    /// Previously also showed a text popup announcing the delivery and prohibited goods
    /// (the mail-sorting equivalent of the "Someone is waiting at the booth" prompt); that popup
    /// was deemed unnecessary and removed.
    /// </summary>
    [ClientRpc]
    private void NotifyDeliveryAlertClientRpc()
    {
    }

    private void OnDrawGizmosSelected()
    {
        Transform crate = DeliveryCrateTransform;
        if (crate == null) return;

        // Package area inside the crate (metres, crate pivot/rotation, scale ignored).
        Gizmos.matrix = Matrix4x4.TRS(crate.position, crate.rotation, Vector3.one);
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
        Vector3 size = new Vector3(_cratePackageAreaSize.x, _cratePackageLayerHeight, _cratePackageAreaSize.y);
        Gizmos.DrawWireCube(_cratePackageAreaCenter, size);
        Gizmos.matrix = Matrix4x4.identity;
    }

    /// <summary>
    /// Lightweight <see cref="ISystemicThreat"/> task-list entry for "Open the gate for a shipment".
    /// Registered/removed in <see cref="TaskRegistry"/> by <see cref="OnShipmentWaitingAtGateChanged"/>
    /// on every peer; its lifetime is owned by the replicated <see cref="_shipmentWaitingAtGate"/> flag.
    /// </summary>
    private sealed class OpenGateForShipmentTask : ISystemicThreat
    {
        public OpenGateForShipmentTask(string name) => ThreatName = name;

        public string ThreatName { get; }
        public string ThreatDescription => string.Empty;
        public float ThreatLevel => 0f;
        public float ScoreWeight => 0f;
        public void BeginNightPhase() { }
        public void EndNightPhase() { }
    }
}
