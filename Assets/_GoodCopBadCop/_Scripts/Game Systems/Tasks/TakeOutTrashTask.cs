using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// One-shot trash-collection task. Call <see cref="TriggerTask()"/> on the server to
/// immediately spawn a random number of trash items across the configured spawn zones.
/// Call <see cref="TriggerTask(bool)"/> with <c>useGorePrefabs: true</c> to spawn from the
/// <see cref="_goreJunkPrefabs"/> pool instead (e.g. gore/body parts strewn across the yard).
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
///   - Assign _goreJunkPrefabs (gore/body-part variants, also registered as Network Prefabs)
///     for days that dress the yard with gore instead of standard junk. Because these spawn
///     with a live Rigidbody and physically settle, each one MUST have a single root Rigidbody
///     plus NetworkTransform and NetworkRigidbody (UseRigidBodyForMotion +
///     AutoUpdateKinematicState) so the server's simulation is the only authority and every
///     client sees the same resting pose. Without them each peer simulates its own copy and
///     players end up looking at gore that isn't where — or isn't visible at all — for others.
///     Do not give these prefabs nested Rigidbodies/joints (ragdolls): only the root transform
///     is replicated, so child bodies would drift away from it independently on every client.
///   - Assign _bloodDecalPrefabs (flat ground-decal prefabs, also registered as Network
///     Prefabs) to have a blood splatter spawned under each gore item when useGorePrefabs
///     is true. Optional — leave empty to disable. Prefabs with a GraffitiInteractable
///     component (e.g. "Random Blood Splatter Variant.prefab") are automatically registered
///     with CleanBloodTask so they count toward the Day 3 "Clean Blood" mop task.
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

    [Tooltip("Minimum number of gore/body-part items to spawn when TriggerTask is called with " +
             "useGorePrefabs: true (inclusive). Independent of _minSpawnCount so gore-heavy days " +
             "(e.g. Day 3) can spawn a different amount than standard trash days.")]
    [SerializeField] private int _minGoreSpawnCount = 8;

    [Tooltip("Maximum number of gore/body-part items to spawn when TriggerTask is called with " +
             "useGorePrefabs: true (inclusive). Independent of _maxSpawnCount so gore-heavy days " +
             "(e.g. Day 3) can spawn a different amount than standard trash days.")]
    [SerializeField] private int _maxGoreSpawnCount = 12;

    [Tooltip("Pool of trash prefabs to pick from. All must be registered as Network Prefabs in the NetworkManager.")]
    [SerializeField] private GameObject[] _trashPrefabs;

    [Tooltip("Pool of gore/body-part prefabs to pick from when TriggerTask is called with useGorePrefabs: true. " +
             "All must be registered as Network Prefabs in the NetworkManager.")]
    [SerializeField] private GameObject[] _goreJunkPrefabs;

    [Tooltip("Pool of blood decal prefabs spawned on the ground under each gore/body-part item " +
             "(only used when useGorePrefabs is true). Flat quad/plane prefabs expected — oriented " +
             "with their forward axis facing down into the ground. All must be registered as " +
             "Network Prefabs in the NetworkManager. Leave empty to disable blood decals.")]
    [SerializeField] private GameObject[] _bloodDecalPrefabs;

    [Tooltip("Small cosmetic blood-spray particle spawned alongside every blood decal, aligned with " +
             "the same ground normal and in world space. Purely cosmetic/local — not a NetworkObject, " +
             "broadcast to every client via RPC. Leave unassigned to disable.")]
    [SerializeField] private GameObject _bloodParticlePrefab;

    [Tooltip("Seconds before a spawned blood particle effect is automatically destroyed.")]
    [Min(0f)]
    [SerializeField] private float _bloodParticleLifetime = 3f;

    [Tooltip("One or more zones in which items are randomly placed.")]
    [SerializeField] private SpawnZone[] _spawnZones;

    [Header("Cleanup Region")]
    [Tooltip("Compound-collider region defining the checkpoint for SCORING purposes — what counts " +
             "toward this task's total. Authored independently of (and normally much wider than) " +
             "the spawn zones above, which only describe where items are randomly placed. Leave " +
             "unassigned to auto-resolve CheckpointCleanupArea.Instance; if no area exists at all, " +
             "the spawn zones are used as a legacy fallback.")]
    [SerializeField] private CheckpointCleanupArea _cleanupArea;

    [Tooltip("Layer(s) the downward raycast hits to land items on the ground.")]
    [SerializeField] private LayerMask _groundLayer;

    [Tooltip("Extra height added above the raycast hit point so items sit on the surface rather than clipping into it.")]
    [SerializeField] private float _spawnHeightOffset = 0.05f;

    [Tooltip("Maximum seconds the server waits for a spawned gore/body-part item's Rigidbody to " +
             "fall asleep before treating it as settled and running its final in-yard bounds " +
             "check (see MonitorGoreJunkItem). Only applies to items spawned with " +
             "useGorePrefabs: true.")]
    [Min(0f)]
    [SerializeField] private float _goreSettleTimeout = 5f;

    [Tooltip("How far below its spawn height a gore/body-part item may fall before the server " +
             "treats it as having clipped through the world and despawns it (after removing it " +
             "from the task total, so it can never leave the objective at e.g. 12/13 with an " +
             "unreachable item still required). Mirrors MutantEnemy's goreMaxFallDistance.")]
    [Min(0f)]
    [SerializeField] private float _goreMaxFallDistance = 15f;

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
    private readonly List<NetworkObject> _spawnedDecals = new();
    private readonly Dictionary<NetworkObject, WorldObjectPlacementSaveData> _itemPlacements = new();
    private readonly Dictionary<NetworkObject, WorldObjectPlacementSaveData> _decalPlacements = new();
    private bool _taskActive;
    private bool _isGoreTask;

    /// <summary>
    /// Pre-existing scene <see cref="JunkItem"/>s counted into <see cref="_totalCount"/> by
    /// <see cref="ActivateForExistingItems"/>/<see cref="TriggerTask"/> (e.g. the Day 1 soldier
    /// body). Tracked separately from <see cref="_spawnedItems"/> because those are despawned
    /// wholesale by <see cref="DespawnExistingItems"/> on the next trigger and these must not be —
    /// they belong to the scene/other systems, not to this task.
    ///
    /// Needed so <see cref="OnJunkItemCollected"/> can tell a COUNTED item from an UNCOUNTED
    /// bonus one; without it, collecting the soldier body would be mistaken for a bonus pickup and
    /// wrongly inflate the total (see <see cref="OnTrashBagDeposited"/>).
    /// </summary>
    private readonly List<NetworkObject> _countedExistingItems = new();

    /// <summary>
    /// Number of junk items collected into bags this run that were NOT part of this task's counted
    /// total.
    ///
    /// Gore and corpses outside the <see cref="CheckpointCleanupArea"/> can no longer produce these:
    /// countability and interactability are one rule again, so anything outside the region is inert
    /// scenery (see <see cref="UnregisterExternalJunkItem"/> and
    /// <c>MutantEnemy.EnableCorpseJunkPickupAfterSettle</c>). What remains are pickups belonging to
    /// OTHER systems — booth-mess junk, a reusable guard corpse — collected into a bag while this task
    /// happens to be running.
    ///
    /// This is a safety valve, not a feature: without it, a bag containing junk this task never
    /// counted would push <see cref="_depositedCount"/> toward a <see cref="_totalCount"/> it didn't
    /// earn and complete the task early (or have the deposit silently swallowed by the clamp).
    /// Reconciling at DEPOSIT time by incrementing both counters together (+1/+1) keeps the readout
    /// honest without moving the goalpost.
    /// </summary>
    private int _pendingBonusCollected;

    /// <summary>
    /// Set by a day script (e.g. Day_01, in <c>DayActivated</c>/<c>DayDeactivated</c>) for the
    /// entire duration it will show its OWN hand-scripted TutorialObjectiveList row for this
    /// task — e.g. Day 1's tutorial-choreographed trash objective added in
    /// <c>Day_01.OnTrashTaskReadySync</c>. Set it well before <see cref="TriggerTask"/> can
    /// possibly run (activation time, not trigger time) so <see cref="HUDTaskList"/> never has
    /// a chance to add its own generic row first — that race would leave a stale duplicate row
    /// behind even after this flag is later set. While true, HUDTaskList skips this threat
    /// entirely (same pattern as <c>ProcessResidentsTask</c>'s exclusion for the automatic
    /// subject counter). Days that don't hand-manage this task (e.g. Day 2+) leave this false
    /// and get the task's row purely from the generic HUDTaskList/TaskRegistry bridge.
    /// </summary>
    public bool HasCustomTutorialRow { get; set; }

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

    /// <summary>Captures live trash/gore and blood-decals so the host can reconstruct the workday.</summary>
    public TrashTaskSaveState CaptureSaveState()
    {
        return new TrashTaskSaveState
        {
            IsActive = _isActive.Value,
            IsGoreTask = _isGoreTask,
            DepositedCount = _depositedCount.Value,
            TotalCount = _totalCount.Value,
            PendingBonusCollected = _pendingBonusCollected,
            Items = CapturePlacements(_spawnedItems, _itemPlacements),
            BloodDecals = CapturePlacements(_spawnedDecals, _decalPlacements)
        };
    }

    /// <summary>Recreates saved dynamic trash, gore, and decals on the authoritative host.</summary>
    public void RestoreSaveState(TrashTaskSaveState state)
    {
        if (!IsServer || state == null) return;

        DespawnExistingItems();
        _isGoreTask = state.IsGoreTask;
        _taskActive = state.IsActive;
        _depositedCount.Value = Mathf.Max(0, state.DepositedCount);
        _totalCount.Value = Mathf.Max(_depositedCount.Value, state.TotalCount);
        _pendingBonusCollected = Mathf.Max(0, state.PendingBonusCollected);

        foreach (WorldObjectPlacementSaveData placement in state.Items ?? Array.Empty<WorldObjectPlacementSaveData>())
            SpawnSavedItem(placement, _isGoreTask);
        foreach (WorldObjectPlacementSaveData placement in state.BloodDecals ?? Array.Empty<WorldObjectPlacementSaveData>())
            SpawnSavedBloodDecal(placement);

        UpdateThreatLevel();
        _isActive.Value = state.IsActive;
        if (_taskActive)
        {
            JunkItem.OnAnyJunkItemCollected += OnJunkItemCollected;
            DumpsterInteractable.OnTrashBagDeposited += OnTrashBagDeposited;
            ShiftManager.Instance?.RegisterPendingDailyTask(this);
        }
    }

    private static WorldObjectPlacementSaveData[] CapturePlacements(
        List<NetworkObject> objects,
        Dictionary<NetworkObject, WorldObjectPlacementSaveData> placements)
    {
        var result = new List<WorldObjectPlacementSaveData>();
        foreach (NetworkObject netObj in objects)
        {
            if (netObj == null || !netObj.IsSpawned || !placements.TryGetValue(netObj, out WorldObjectPlacementSaveData placement))
                continue;

            WorldObjectPlacementSaveData copy = new WorldObjectPlacementSaveData
            {
                PrefabIndex = placement.PrefabIndex,
                Position = netObj.transform.position,
                RotationEuler = netObj.transform.eulerAngles,
                LocalScale = netObj.transform.localScale,
                ScrubProgress = netObj.TryGetComponent(out GraffitiInteractable scrub) ? scrub.ScrubProgress : 0f
            };
            result.Add(copy);
        }
        return result.ToArray();
    }

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
    /// <param name="includeSuspects">
    /// When false, skips every JunkItem attached to a SuspectCharacter — used when this is
    /// triggered by mutant gore/corpse registration (see <see cref="RegisterExternalJunkItem"/>)
    /// so NPC bodies tracked by their own separate systems aren't swept into a gore-only task.
    /// </param>
    public void ActivateForExistingItems(bool includeSuspects = true)
    {
        if (!IsServer) return;
        if (_taskActive) return;

        int count = CollectCountablePreExistingItems(includeSuspects);

        if (count == 0)
        {
            Debug.Log("[TakeOutTrashTask] ActivateForExistingItems — no active JunkItems found, task not activated.");
            return;
        }

        _taskActive = true;
        _depositedCount.Value = 0;
        _totalCount.Value = count;
        _pendingBonusCollected = 0;

        UpdateThreatLevel();

        JunkItem.OnAnyJunkItemCollected          += OnJunkItemCollected;
        DumpsterInteractable.OnTrashBagDeposited += OnTrashBagDeposited;

        _isActive.Value = true;

        // Explicitly re-register on every client rather than relying solely on
        // _isActive's OnValueChanged — if the task was already active (e.g. left
        // active across a day rollover), the NetworkVariable write above is a no-op
        // and OnIsActiveChanged never fires, silently dropping the task from the HUD.
        RegisterInTaskRegistryClientRpc();

        ShiftManager.Instance?.RegisterPendingDailyTask(this);

        Debug.Log($"[TakeOutTrashTask] ActivateForExistingItems — activated for {count} existing JunkItem(s) (no new items spawned).");
    }

    /// <summary>
    /// Spawns a random number of trash items (between <see cref="_minSpawnCount"/> and
    /// <see cref="_maxSpawnCount"/>), counts any pre-existing <see cref="JunkItem"/>s
    /// already active in the scene (e.g. the dead soldier body), and registers this task
    /// in <see cref="TaskRegistry"/> on all clients. Server-only.
    /// </summary>
    public void TriggerTask() => TriggerTask(useGorePrefabs: false);

    /// <summary>
    /// Spawns a random number of items (between <see cref="_minSpawnCount"/> and
    /// <see cref="_maxSpawnCount"/>) across the configured <see cref="_spawnZones"/>, counts
    /// any pre-existing <see cref="JunkItem"/>s already active in the scene (e.g. the dead
    /// soldier body), and registers this task in <see cref="TaskRegistry"/> on all clients.
    /// Server-only.
    /// </summary>
    /// <param name="useGorePrefabs">
    /// When true, items are spawned from <see cref="_goreJunkPrefabs"/> (gore/body parts)
    /// instead of the standard <see cref="_trashPrefabs"/> pool.
    /// </param>
    public void TriggerTask(bool useGorePrefabs)
    {
        if (!IsServer) return;

        DespawnExistingItems();
        _isGoreTask = useGorePrefabs;
        _taskActive = true;
        _depositedCount.Value = 0;
        _pendingBonusCollected = 0;

        // Count pre-existing JunkItems in the scene BEFORE spawning (e.g. soldier body).
        // Suspect NPC bodies (e.g. the Day 1 soldier, Vlad) are tracked by their own separate
        // systems and shouldn't be swept into the mutant-breach gore task's total just because
        // they happen to be lying uncollected inside the checkpoint.
        int preExistingCount = CollectCountablePreExistingItems(includeSuspects: !useGorePrefabs);

        GameObject[] prefabPool = useGorePrefabs ? _goreJunkPrefabs : _trashPrefabs;

        int spawnCount = useGorePrefabs
            ? Random.Range(_minGoreSpawnCount, _maxGoreSpawnCount + 1)
            : Random.Range(_minSpawnCount, _maxSpawnCount + 1);
        for (int i = 0; i < spawnCount; i++)
            SpawnSingleItem(prefabPool, spawnBloodDecal: useGorePrefabs);

        // Total = actually spawned (may be less than spawnCount on error) + pre-existing.
        _totalCount.Value = _spawnedItems.Count + preExistingCount;

        UpdateThreatLevel();

        JunkItem.OnAnyJunkItemCollected          += OnJunkItemCollected;
        DumpsterInteractable.OnTrashBagDeposited += OnTrashBagDeposited;

        // Flip the active flag — OnIsActiveChanged fires on all clients (and late joiners
        // read the initial value in OnNetworkSpawn) to register this task in TaskRegistry.
        _isActive.Value = true;

        // Explicitly re-register on every client rather than relying solely on
        // _isActive's OnValueChanged — if the task was already active from a prior
        // trigger this cycle (e.g. left active across a day rollover), the NetworkVariable
        // write above is a no-op and OnIsActiveChanged never fires, silently dropping the
        // task from the HUD even though new items (e.g. Day 3's gore) were just spawned.
        RegisterInTaskRegistryClientRpc();

        ShiftManager.Instance?.RegisterPendingDailyTask(this);
        SaveDataManager.Instance?.SaveCurrentWorkdayState();

        Debug.Log($"[TakeOutTrashTask] Task triggered ({(useGorePrefabs ? "gore" : "trash")} pool) — " +
                  $"spawned {_spawnedItems.Count}, pre-existing {preExistingCount}, total {_totalCount.Value}.");
    }

    /// <summary>
    /// Tutorial-only helper: turns on the persistent outline highlight for every currently
    /// active, actually-collectible <see cref="JunkItem"/> (freshly spawned or pre-existing,
    /// e.g. the soldier body) on every client. Intended to be called once, right after
    /// <see cref="TriggerTask"/>, so the player can immediately spot every piece of trash/gore.
    /// Does not affect later/other triggers of this same shared task instance — callers must
    /// invoke this explicitly. Server-only.
    /// </summary>
    /// <param name="includeSuspects">
    /// When false, skips every JunkItem attached to a <see cref="SuspectCharacter"/> (e.g. the
    /// Day 1 soldier body, or Vlad) entirely — used for the mutant-breach gore highlight, since
    /// those NPCs' bodies belong to their own separate tasks/systems and shouldn't be swept
    /// into a highlight pass meant only for mutant gore/corpses.
    /// </param>
    public void HighlightAllItemsForTutorial(bool includeSuspects = true)
    {
        if (!IsServer) return;

        var junkItems = FindObjectsByType<JunkItem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (JunkItem junk in junkItems)
        {
            if (!IsCountablePreExistingJunkItem(junk, includeSuspects))
                continue;

            NetworkObject netObj = junk.NetworkObject;
            if (netObj != null && netObj.IsSpawned)
                junk.SetTutorialHighlight(true);
        }
    }

    /// <summary>
    /// Resolves the checkpoint cleanup region, falling back to
    /// <see cref="CheckpointCleanupArea.Instance"/> when the Inspector reference is unassigned.
    /// Resolved lazily rather than in Awake because component Awake order isn't guaranteed.
    /// </summary>
    private CheckpointCleanupArea CleanupArea
    {
        get
        {
            if (_cleanupArea == null)
                _cleanupArea = CheckpointCleanupArea.Instance;

            return _cleanupArea;
        }
    }

    /// <summary>
    /// THE authoritative "does this position count toward checkpoint cleanup?" test — used by
    /// every external system that spawns something scorable (gore chunks and corpses from a killed
    /// <see cref="MutantEnemy"/>, blood splatters, stray junk) to decide whether it should be
    /// added to this task's total and factored into the Checkpoint Integrity Score, or left as
    /// purely cosmetic, uncounted debris.
    ///
    /// Delegates to the <see cref="CheckpointCleanupArea"/> — a compound collider region tracing
    /// the inside of the fence — and only falls back to the <see cref="_spawnZones"/> when no such
    /// area exists in the scene. The zones are a poor stand-in for this: they describe where the
    /// task RANDOMLY PLACES items, so they are deliberately tight and flat and cover far less
    /// ground than the fenced checkpoint the player perceives. Using them as the countability test
    /// is what made bodies that died well inside the fence — but a few metres off a spawn zone —
    /// silently stop counting.
    ///
    /// Note this answers "does it COUNT", not "can it be picked up". Interactability is no longer
    /// gated on it: see <see cref="_pendingBonusCollected"/> and
    /// <c>MutantEnemy.EnableCorpseJunkPickupAfterSettle</c>.
    /// </summary>
    public bool CountsTowardCleanup(Vector3 worldPosition)
    {
        CheckpointCleanupArea area = CleanupArea;

        if (area != null && area.HasRegions)
            return area.Contains(worldPosition);

        return IsPositionInSpawnZones(worldPosition);
    }

    /// <summary>
    /// Returns true when <paramref name="worldPosition"/> falls within any of this task's
    /// configured item-placement <see cref="SpawnZone"/>s. This is the literal spawn footprint —
    /// for "does this count toward cleanup", use <see cref="CountsTowardCleanup"/> instead, which
    /// only falls back to this when no <see cref="CheckpointCleanupArea"/> is present.
    /// </summary>
    public bool IsPositionInSpawnZones(Vector3 worldPosition)
    {
        if (_spawnZones == null)
            return false;

        foreach (SpawnZone zone in _spawnZones)
        {
            if (zone != null && zone.Contains(worldPosition))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="junk"/> should count toward this task's pre-existing
    /// scene sweep (<see cref="ActivateForExistingItems"/>/<see cref="TriggerTask"/>). Filters
    /// out three false-positive cases that previously inflated the task's denominator:
    ///
    /// 1. Mutant bodies/gore that landed (or later rolled/settled) outside the
    ///    <see cref="CheckpointCleanupArea"/> — dynamically-spawned gore already excludes these via
    ///    <see cref="UnregisterExternalJunkItem"/>, but that unregister only removes tracking;
    ///    the JunkItem component stays enabled on an active GameObject, so a later blanket
    ///    scan would otherwise sweep it back in. Checked via <see cref="CountsTowardCleanup"/>.
    ///    This is now the ONLY thing keeping an out-of-region corpse out of the denominator —
    ///    such corpses are deliberately left enabled and collectible (see
    ///    <c>MutantEnemy.EnableCorpseJunkPickupAfterSettle</c>), they simply don't count.
    /// 2. Living <see cref="SuspectCharacter"/>s. JunkItem components pre-attached to a suspect
    ///    start non-collectible and are Unity-'enabled' the whole time the suspect is alive
    ///    (see <see cref="JunkItem"/>'s class doc) — only <see cref="JunkItem.IsCollectible"/>
    ///    flips true once <see cref="SuspectCharacter.EnableJunkPickup"/> runs on death. Skip
    ///    any JunkItem still attached to a live suspect so alive characters are never counted.
    /// 3. Any JunkItem that isn't currently collectible — a gore chunk that was never activated as
    ///    collectible junk, or one that has been ruled out of the cleanup because it came to rest
    ///    outside the region (<see cref="JunkItem.IsCleanupEligible"/>). Delegated to
    ///    <see cref="JunkItem.CanBeCollected"/>, the same predicate that drives interactability and
    ///    the findability glow, so the sweep can never count something the player can't touch.
    /// </summary>
    /// <param name="includeSuspects">
    /// When false, any JunkItem attached to a <see cref="SuspectCharacter"/> is excluded
    /// entirely, regardless of collectibility — used by the mutant-breach gore task/highlight
    /// so NPC bodies (e.g. the Day 1 soldier, Vlad) — tracked by their own separate systems —
    /// are never swept into a count/highlight meant only for mutant gore/corpses.
    /// </param>
    private bool IsCountablePreExistingJunkItem(JunkItem junk, bool includeSuspects = true)
    {
        if (junk == null)
            return false;

        if (!includeSuspects && junk.GetComponent<SuspectCharacter>() != null)
            return false;

        // Covers all of the above: activeInHierarchy, the suspect-only IsCollectible rule (a
        // suspect's own Unity 'enabled' flag is deliberately never toggled — see JunkItem's class
        // doc — so testing it here would wrongly exclude every suspect corpse, dead or alive), the
        // plain 'enabled' rule for everything else, and the IsCleanupEligible veto.
        if (!junk.CanBeCollected)
            return false;

        return CountsTowardCleanup(junk.transform.position);
    }

    /// <summary>
    /// Rebuilds <see cref="_countedExistingItems"/> from every <see cref="JunkItem"/> already in
    /// the scene that passes <see cref="IsCountablePreExistingJunkItem"/>, and returns how many
    /// there were. Uses FindObjectsInactive.Include so disabled-component JunkItems on active
    /// GameObjects are found, then filters to those that are actually enabled, active in the
    /// hierarchy, inside the checkpoint cleanup region, and not still attached to a living suspect.
    ///
    /// Recording the identities (not just the count) is what lets
    /// <see cref="OnJunkItemCollected"/> distinguish a counted pickup from an uncounted bonus one.
    /// Server-only.
    /// </summary>
    private int CollectCountablePreExistingItems(bool includeSuspects)
    {
        _countedExistingItems.Clear();

        var existingJunk = FindObjectsByType<JunkItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (JunkItem j in existingJunk)
        {
            if (!IsCountablePreExistingJunkItem(j, includeSuspects))
                continue;

            NetworkObject netObj = j.NetworkObject;
            if (netObj != null && !_countedExistingItems.Contains(netObj))
                _countedExistingItems.Add(netObj);
        }

        return _countedExistingItems.Count;
    }

    /// <summary>
    /// True when <paramref name="netObj"/> is part of this run's counted total — either an item
    /// this task spawned/registered (<see cref="_spawnedItems"/>) or a pre-existing scene item
    /// swept in by <see cref="CollectCountablePreExistingItems"/>. Anything else that gets
    /// collected is an out-of-region bonus pickup (see <see cref="_pendingBonusCollected"/>).
    /// </summary>
    private bool IsCountedItem(NetworkObject netObj)
    {
        return netObj != null
            && (_spawnedItems.Contains(netObj) || _countedExistingItems.Contains(netObj));
    }

    /// <summary>
    /// Registers an externally-spawned <see cref="JunkItem"/>'s NetworkObject with this task
    /// (e.g. a gore chunk dropped by a killed mutant that landed inside the yard, or a corpse
    /// that just became collectible junk). Collection is already tracked generically via the
    /// static <see cref="JunkItem.OnAnyJunkItemCollected"/> event, so this only needs to keep
    /// the HUD denominator accurate.
    ///
    /// If the task is already active, the item is added to the tracked list and the total
    /// count is incremented immediately. If no trash task is currently running, this call
    /// dynamically starts one via <see cref="ActivateForExistingItems"/> — since the caller
    /// already enabled/spawned <paramref name="netObj"/> before calling this, it (plus any
    /// other junk already active in the scene) is swept up into the freshly-activated task
    /// rather than being silently dropped. Server-only.
    /// </summary>
    public void RegisterExternalJunkItem(NetworkObject netObj)
    {
        if (!IsServer || netObj == null)
            return;

        if (!_taskActive)
        {
            // No active trash task — dynamically trigger one for whatever junk is already
            // active in the scene, which by now includes netObj itself. This path is only
            // ever reached by mutant gore/corpse registration, so exclude suspect NPC bodies
            // (e.g. the Day 1 soldier, Vlad) from being swept in alongside it.
            ActivateForExistingItems(includeSuspects: false);
            return;
        }

        _spawnedItems.Add(netObj);
        _totalCount.Value++;
        UpdateThreatLevel();
    }

    /// <summary>
    /// Removes a previously-registered external <see cref="JunkItem"/> from this task's total —
    /// used when a gore chunk registered via <see cref="RegisterExternalJunkItem"/> (based on
    /// its initial in-region launch position) later comes to rest outside the
    /// <see cref="CheckpointCleanupArea"/> once physics settles (e.g. it rolled or bounced past
    /// the fence line). Decrements the total so the task no longer requires collecting it and
    /// completes the task if it was the last outstanding item.
    ///
    /// The item is also ruled out of the cleanup entirely via
    /// <see cref="JunkItem.SetCleanupEligible"/>: leaving the checkpoint means it stops being
    /// interactable and stops being highlighted, not merely uncounted. Countability and
    /// interactability are one rule — inside the region it is collectible gore, outside it is
    /// scenery — so the player is never shown an affordance that doesn't contribute to anything.
    /// No-op if the item was never tracked (e.g. already collected). Server-only.
    /// </summary>
    public void UnregisterExternalJunkItem(NetworkObject netObj)
    {
        if (!IsServer || netObj == null) return;

        // Non-short-circuiting '|' so the item is removed from BOTH tracking lists.
        bool wasTracked = _spawnedItems.Remove(netObj) | _countedExistingItems.Remove(netObj);
        if (!wasTracked) return;

        JunkItem junk = netObj.GetComponent<JunkItem>();
        if (junk != null)
            junk.SetCleanupEligible(false);

        _totalCount.Value = Mathf.Max(_depositedCount.Value, _totalCount.Value - 1);
        UpdateThreatLevel();

        Debug.Log($"[TakeOutTrashTask] External junk item is resting outside the checkpoint — " +
                  $"unregistered and made inert. New total {_totalCount.Value}.");

        if (_taskActive && _depositedCount.Value >= _totalCount.Value)
            CompleteTask();
    }

    // ── Private ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called on the server when a TrashBag is deposited in a dumpster.
    /// Increments the deposited count by the number of junk items the bag contained.
    /// When all items are deposited, fires <see cref="OnAllItemsDeposited"/> and removes
    /// the task from the HUD on all clients.
    ///
    /// Bonus reconciliation: any items in this bag that were collected from OUTSIDE the
    /// <see cref="CheckpointCleanupArea"/> (see <see cref="_pendingBonusCollected"/>) are added to
    /// BOTH <see cref="_totalCount"/> and <see cref="_depositedCount"/>, so the player's extra
    /// work shows up in the HUD readout ("3/5" becomes "4/6") while leaving the amount still
    /// outstanding inside the checkpoint completely unchanged. This is what makes out-of-region
    /// corpses safe to pick up: they can never be required, and they can never block completion.
    /// </summary>
    private void OnTrashBagDeposited(int junkCount)
    {
        // Drop anything that has ended up outside the checkpoint before evaluating completion, so a
        // stranded piece can never hold the objective at e.g. 12/13 (see ReconcileOutOfYardItems).
        ReconcileOutOfYardItems();

        int bonus = Mathf.Clamp(_pendingBonusCollected, 0, junkCount);
        if (bonus > 0)
        {
            _pendingBonusCollected -= bonus;
            _totalCount.Value += bonus;

            Debug.Log($"[TakeOutTrashTask] {bonus} bonus item(s) from outside the checkpoint " +
                      $"deposited — total raised to {_totalCount.Value} so they credit without " +
                      "changing what's still required inside.");
        }

        _depositedCount.Value = Mathf.Min(_depositedCount.Value + junkCount, _totalCount.Value);
        Debug.Log($"[TakeOutTrashTask] {junkCount} item(s) deposited. " +
                  $"Total deposited: {_depositedCount.Value}/{_totalCount.Value}");

        if (_depositedCount.Value < _totalCount.Value) return;

        CompleteTask();
    }

    /// <summary>
    /// Marks the task complete: stops listening for further collection/deposit events, awards
    /// coupons, and removes the task from the HUD on all clients. Shared by
    /// <see cref="OnTrashBagDeposited"/> (every item deposited) and
    /// <see cref="UnregisterExternalJunkItem"/> (the last outstanding item turned out to be
    /// untrackable, e.g. gore that rolled outside the yard). No-op if the task isn't active.
    /// </summary>
    private void CompleteTask()
    {
        if (!_taskActive) return;

        _taskActive = false;
        _pendingBonusCollected = 0;
        _countedExistingItems.Clear();
        JunkItem.OnAnyJunkItemCollected          -= OnJunkItemCollected;
        DumpsterInteractable.OnTrashBagDeposited -= OnTrashBagDeposited;

        Debug.Log("[TakeOutTrashTask] All items deposited — task complete.");
        // Tasks no longer pay coupons — players are only paid for processing suspects (see SuspectController.PayOutResults).
        // ATM.Instance?.SpawnCoupons(_couponReward);

        // Flip the active flag — OnIsActiveChanged fires on all clients to remove the task.
        _isActive.Value = false;
        SaveDataManager.Instance?.SaveCurrentWorkdayState();

        // Broadcast completion to every client, not just wherever this ServerRpc-triggered
        // code happens to run (the server/host process). Day_01 subscribes to these events
        // per-client to gate the shared TutorialObjectiveList; previously OnAllItemsDeposited/
        // OnDailyTaskCompleted only ever fired locally on the server, so remote (non-host)
        // clients never received them.
        NotifyTaskCompletedClientRpc();
    }

    [ClientRpc]
    private void NotifyTaskCompletedClientRpc()
    {
        OnAllItemsDeposited?.Invoke();
        OnDailyTaskCompleted?.Invoke();
    }

    /// <summary>
    /// Explicitly (re-)adds this task to <see cref="TaskRegistry"/> on every client. Called
    /// right after every trigger, independent of <see cref="_isActive"/>'s OnValueChanged —
    /// that callback only fires on an actual value transition, so re-triggering the task
    /// while it was already active (e.g. gore spawned on Day 3 on top of an unfinished prior
    /// day's trash) would otherwise leave the task silently missing from the HUD even though
    /// new items were spawned into the world.
    /// </summary>
    [ClientRpc]
    private void RegisterInTaskRegistryClientRpc()
    {
        TaskRegistry.Instance?.AddThreat(this);
    }

    /// <summary>
    /// Fires on the server each time any <see cref="JunkItem"/> is collected into a bag.
    /// Classifies the pickup: an item that belongs to this run's counted total is simply pruned
    /// from tracking, while anything else is an out-of-region bonus pickup (a mutant corpse or
    /// gore chunk from beyond the fence) and is banked in <see cref="_pendingBonusCollected"/> so
    /// <see cref="OnTrashBagDeposited"/> can credit it without moving the goalpost.
    /// </summary>
    private void OnJunkItemCollected(JunkItem junk)
    {
        if (!IsServer) return;

        NetworkObject netObj = junk != null ? junk.NetworkObject : null;
        bool wasCounted = IsCountedItem(netObj);

        if (!wasCounted)
            _pendingBonusCollected++;
        else
            // Drop it from pre-existing tracking explicitly: a counted item whose JunkItem has
            // _destroyOnCollect = false (e.g. a reusable guard corpse) stays spawned after
            // collection, so PruneCollectedItems' IsSpawned sweep would never remove it and a
            // later re-collection of the same object would be double-counted as counted again.
            _countedExistingItems.Remove(netObj);

        PruneCollectedItems();
        UpdateThreatLevel();

        Debug.Log($"[TakeOutTrashTask] Item collected into bag " +
                  $"({(wasCounted ? "counted" : "bonus — outside the checkpoint")}) — " +
                  $"remaining tracked items: {_spawnedItems.Count}");
    }

    private void SpawnSingleItem(GameObject[] prefabPool, bool spawnBloodDecal)
    {
        if (prefabPool == null || prefabPool.Length == 0)
        {
            Debug.LogError("[TakeOutTrashTask] Prefab pool is empty or not assigned.");
            return;
        }

        int prefabIndex = Random.Range(0, prefabPool.Length);
        GameObject prefab = prefabPool[prefabIndex];
        if (prefab == null) return;

        Vector3    spawnPos = GetRandomSpawnPosition(out Vector3 groundNormal);
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
        _itemPlacements[netObj] = new WorldObjectPlacementSaveData
        {
            PrefabIndex = prefabIndex,
            Position = spawnPos,
            RotationEuler = spawnRot.eulerAngles,
            LocalScale = itemGo.transform.localScale
        };

        if (spawnBloodDecal)
        {
            SpawnBloodDecal(spawnPos, groundNormal);
            BeginGoreSettleWatchdog(itemGo, spawnPos);
        }
    }

    /// <summary>
    /// Starts the server-side settle watchdog for a freshly-spawned gore/body-part item (see
    /// <see cref="SpawnSingleItem"/>'s <c>spawnBloodDecal</c>/useGorePrefabs path).
    ///
    /// Deliberately does NOT add a <see cref="GoreKinematicSettler"/>: that helper is documented
    /// as cosmetic/local-only and explicitly not for networked gore, because a networked gore
    /// piece's kinematic state is owned by Netcode's <c>NetworkRigidbody</c>
    /// (AutoUpdateKinematicState keeps every non-authority copy kinematic and transform-driven).
    /// Adding it here previously froze the piece on the server alone while every client kept
    /// simulating its own Rigidbody forever — with no NetworkTransform on the prefabs at the
    /// time, each peer's copy settled somewhere different, so a piece visible to one player was
    /// under the floor or behind a prop for another. The prefabs now carry NetworkTransform +
    /// NetworkRigidbody, so the server's simulation is the only one that counts and every client
    /// receives the same resting pose.
    /// </summary>
    private void BeginGoreSettleWatchdog(GameObject piece, Vector3 spawnPos)
    {
        NetworkObject netObj = piece.GetComponent<NetworkObject>();
        if (netObj == null) return;

        Rigidbody rb = piece.GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogWarning($"[TakeOutTrashTask] Gore prefab '{piece.name}' has no Rigidbody — it " +
                             "cannot settle or be watchdogged. Add a Rigidbody (plus NetworkTransform " +
                             "and NetworkRigidbody) to the prefab root.");
            return;
        }

        // The server is the authority, so it is the peer that actually simulates the drop.
        rb.isKinematic = false;

        StartCoroutine(MonitorGoreJunkItem(netObj, rb, spawnPos.y - _goreMaxFallDistance));
    }

    /// <summary>
    /// Server-only watchdog for a networked gore <see cref="JunkItem"/> spawned by this task.
    /// Mirrors <c>MutantEnemy.MonitorGoreJunkItem</c>, which this task previously lacked entirely —
    /// the missing guard is why a gore run could strand the objective at e.g. 12/13 forever: a
    /// piece that tunnelled through the yard floor or rolled outside every
    /// <see cref="SpawnZone"/> stayed permanently required, and <see cref="CompleteTask"/> only
    /// fires once <c>_depositedCount >= _totalCount</c>.
    ///
    /// Every frame: if the piece has fallen below <paramref name="minY"/> it is unregistered from
    /// the task and despawned outright, so it can never be both required and unreachable. Once
    /// its Rigidbody settles (or <see cref="_goreSettleTimeout"/> elapses), a piece resting
    /// outside the <see cref="CheckpointCleanupArea"/> is unregistered — which also rules it out of
    /// the cleanup entirely (not counted, not interactable, not highlighted; see
    /// <see cref="UnregisterExternalJunkItem"/>) — and switched to kinematic, since it no longer
    /// counts and there's no reason to keep simulating it. A piece that settles inside the region
    /// stays dynamic and tracked. No-op once the item is collected (despawned) before either check
    /// fires.
    /// </summary>
    private IEnumerator MonitorGoreJunkItem(NetworkObject netObj, Rigidbody rb, float minY)
    {
        float elapsed = 0f;
        bool settled = false;

        while (true)
        {
            if (netObj == null || !netObj.IsSpawned)
                yield break;

            if (netObj.transform.position.y < minY)
            {
                Debug.LogWarning($"[TakeOutTrashTask] Gore item '{netObj.name}' fell out of the world — " +
                                 "unregistering and despawning it so it can't block task completion.");
                UnregisterExternalJunkItem(netObj);
                netObj.Despawn(destroy: true);
                yield break;
            }

            if (rb == null || rb.IsSleeping() || elapsed >= _goreSettleTimeout)
            {
                settled = true;
            }
            else
            {
                elapsed += Time.deltaTime;
            }

            if (settled)
                break;

            yield return null;
        }

        if (netObj == null || !netObj.IsSpawned)
            yield break;

        if (CountsTowardCleanup(netObj.transform.position))
            yield break;

        UnregisterExternalJunkItem(netObj);

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
    }

    /// <summary>
    /// Safety net for gore/junk that left the checkpoint AFTER its
    /// <see cref="MonitorGoreJunkItem"/> settle check already passed — e.g. a piece a player kicked
    /// over the fence, a body dragged out of the gate, or one that was stranded by an older build
    /// that shipped without the watchdog. Any tracked item that is still spawned but now resting
    /// outside the <see cref="CheckpointCleanupArea"/> is unregistered so it stops being required.
    ///
    /// Deliberately ignores items that are null/despawned: a collected item is despawned and its
    /// contribution is already accounted for by the bag's deposit count, so decrementing
    /// <see cref="_totalCount"/> for it would double-count and complete the task early.
    /// Server-only.
    /// </summary>
    private void ReconcileOutOfYardItems()
    {
        if (!IsServer) return;

        // Snapshot both lists: UnregisterExternalJunkItem mutates them.
        var tracked = new List<NetworkObject>(_spawnedItems);
        tracked.AddRange(_countedExistingItems);

        foreach (NetworkObject netObj in tracked)
        {
            if (netObj == null || !netObj.IsSpawned)
                continue;

            if (CountsTowardCleanup(netObj.transform.position))
                continue;

            Debug.LogWarning($"[TakeOutTrashTask] Tracked item '{netObj.name}' is resting outside " +
                             "the checkpoint — unregistering it so it can't block task completion.");
            UnregisterExternalJunkItem(netObj);
        }
    }

    /// <summary>
    /// Spawns a random blood decal from <see cref="_bloodDecalPrefabs"/> at <paramref name="position"/>,
    /// oriented so its forward axis faces down into the ground surface described by
    /// <paramref name="groundNormal"/>. No-op when no decal prefabs are assigned. Server-only.
    ///
    /// Registers the spawned decal with <see cref="CleanBloodTask"/> so it counts toward the
    /// Day 3 "Clean Blood" task — decal prefabs with a <see cref="GraffitiInteractable"/> (e.g.
    /// "Random Blood Splatter Variant.prefab") become mop-cleanable and count toward the total;
    /// decals without one are left as purely cosmetic and are ignored by that task.
    /// </summary>
    private void SpawnBloodDecal(Vector3 position, Vector3 groundNormal)
    {
        if (_bloodDecalPrefabs == null || _bloodDecalPrefabs.Length == 0)
            return;

        int prefabIndex = Random.Range(0, _bloodDecalPrefabs.Length);
        GameObject prefab = _bloodDecalPrefabs[prefabIndex];
        if (prefab == null) return;

        // TODO: BloodDecalUtility.GetGroundDecalRotation(groundNormal) was producing incorrect
        // orientations on landing; forcing identity rotation for now until that's fixed.
        Quaternion rotation = Quaternion.identity;
        GameObject   decalGo = Instantiate(prefab, position, rotation);
        NetworkObject netObj = decalGo.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[TakeOutTrashTask] Blood decal prefab is missing a NetworkObject component.");
            Destroy(decalGo);
            return;
        }

        netObj.Spawn(destroyWithScene: true);
        _spawnedDecals.Add(netObj);
        _decalPlacements[netObj] = new WorldObjectPlacementSaveData
        {
            PrefabIndex = prefabIndex,
            Position = position,
            RotationEuler = rotation.eulerAngles,
            LocalScale = decalGo.transform.localScale
        };

        CleanBloodTask.Instance?.RegisterBloodSplatter(netObj);

        SpawnBloodParticleClientRpc(position, rotation);
    }

    /// <summary>
    /// Spawns <see cref="_bloodParticlePrefab"/> on every client at the same position/rotation as
    /// a just-spawned blood-splatter decal (see <see cref="SpawnBloodDecal"/>), so the cosmetic
    /// spray effect appears everywhere the networked decal does. No-op when
    /// <see cref="_bloodParticlePrefab"/> is unassigned.
    /// </summary>
    [ClientRpc]
    private void SpawnBloodParticleClientRpc(Vector3 position, Quaternion rotation)
    {
        BloodDecalUtility.SpawnAlignedParticle(_bloodParticlePrefab, position, rotation, _bloodParticleLifetime);
    }

    private void SpawnSavedItem(WorldObjectPlacementSaveData placement, bool useGorePrefabs)
    {
        GameObject[] pool = useGorePrefabs ? _goreJunkPrefabs : _trashPrefabs;
        if (placement == null || pool == null || placement.PrefabIndex < 0 || placement.PrefabIndex >= pool.Length)
            return;

        GameObject prefab = pool[placement.PrefabIndex];
        if (prefab == null) return;

        GameObject go = Instantiate(prefab, placement.Position, Quaternion.Euler(placement.RotationEuler));
        go.transform.localScale = placement.LocalScale;
        NetworkObject netObj = go.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Destroy(go);
            return;
        }

        netObj.Spawn(destroyWithScene: true);
        _spawnedItems.Add(netObj);
        _itemPlacements[netObj] = placement;
        if (useGorePrefabs)
            BeginGoreSettleWatchdog(go, placement.Position);
    }

    private void SpawnSavedBloodDecal(WorldObjectPlacementSaveData placement)
    {
        if (placement == null || _bloodDecalPrefabs == null || placement.PrefabIndex < 0 ||
            placement.PrefabIndex >= _bloodDecalPrefabs.Length)
            return;

        GameObject prefab = _bloodDecalPrefabs[placement.PrefabIndex];
        if (prefab == null) return;

        GameObject go = Instantiate(prefab, placement.Position, Quaternion.Euler(placement.RotationEuler));
        go.transform.localScale = placement.LocalScale;
        NetworkObject netObj = go.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Destroy(go);
            return;
        }

        netObj.Spawn(destroyWithScene: true);
        GraffitiInteractable scrub = go.GetComponent<GraffitiInteractable>();
        if (scrub != null)
            scrub.RestoreScrubProgress(placement.ScrubProgress);
        _spawnedDecals.Add(netObj);
        _decalPlacements[netObj] = placement;
        CleanBloodTask.Instance?.RegisterBloodSplatter(netObj);
    }

    private void PruneCollectedItems()
    {
        _spawnedItems.RemoveAll(n => n == null || !n.IsSpawned);
        _countedExistingItems.RemoveAll(n => n == null || !n.IsSpawned);
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
        _itemPlacements.Clear();

        foreach (NetworkObject netObj in _spawnedDecals)
        {
            // Tell the mop task first: these decals were registered with CleanBloodTask and count
            // toward its total, so destroying them silently left that task requiring blood that no
            // longer existed — permanently stuck at e.g. 4/5 with a spotless yard.
            CleanBloodTask.Instance?.UnregisterBloodSplatter(netObj);

            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _spawnedDecals.Clear();
        _decalPlacements.Clear();

        _networkThreatLevel.Value = 0f;
    }

    private Vector3 GetRandomSpawnPosition() => GetRandomSpawnPosition(out _);

    private Vector3 GetRandomSpawnPosition(out Vector3 groundNormal)
    {
        groundNormal = Vector3.up;

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
        {
            groundNormal = hit.normal;
            return hit.point + Vector3.up * _spawnHeightOffset;
        }

        return new Vector3(castOrigin.x, zone.transform.position.y + _spawnHeightOffset, castOrigin.z);
    }
}
