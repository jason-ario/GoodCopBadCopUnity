using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Day 3 scripted task: mop up every blood splatter left behind by the gore/body-part junk
/// <see cref="TakeOutTrashTask"/> scatters across the yard. Operates exactly like
/// <see cref="CleanGraffitiTask"/> — a <see cref="GraffitiInteractable"/> scrubbed clean with a
/// <see cref="Mop"/> — except this task doesn't spawn its own splatters or use fixed spawn
/// points. <see cref="TakeOutTrashTask"/> spawns one blood decal per gore piece at a random
/// yard position and hands each one to <see cref="RegisterBloodSplatter"/> as it's created, so
/// this task's total grows to match however much gore actually landed that cycle.
///
/// Implements <see cref="ISystemicThreat"/> for HUD / performance scoring, same as
/// <see cref="CleanGraffitiTask"/> and <see cref="TakeOutTrashTask"/>. Also implements
/// <see cref="IDailyTask"/> so it blocks clock-out via <see cref="ShiftManager.RegisterPendingDailyTask"/>
/// until every registered splatter has been scrubbed — same as <see cref="TakeOutTrashTask"/>.
///
/// Every splatter handed to <see cref="RegisterBloodSplatter"/> is mop-able and always credits when
/// scrubbed. Position decides only whether it is REQUIRED: splatters inside the
/// <see cref="CheckpointCleanupArea"/> count toward the total and block clock-out, while splatters
/// outside it are credited as a bonus at scrub time (+1 total, +1 scrubbed) so the work registers in
/// the HUD without ever being required. <see cref="RegisterTransientBloodSplatter"/> remains for blood
/// that is purely cosmetic by design.
///
/// A periodic reconcile sweep (<see cref="ReconcileSplatters"/>) drops any counted splatter that has
/// been destroyed by another system or has ended up outside the region, so the total can never
/// require blood that isn't there — the failure mode behind "I cleaned it all and it never registered".
///
/// Scene setup:
///   - NetworkObject on this GameObject (in-scene placed — no prefab registration needed).
///   - No prefabs or spawn points to assign here — TakeOutTrashTask feeds this task splatters
///     directly via RegisterBloodSplatter (e.g. Day 3's scripted gore trash task).
///   - The blood decal prefab(s) fed in must each have a GraffitiInteractable component (e.g.
///     "Random Blood Splatter Variant.prefab") — decals without one are skipped (treated as
///     purely cosmetic, not counted).
///   - Calling TriggerTask() is optional — RegisterBloodSplatter auto-activates the task the
///     first time a splatter is registered while it's inactive. Days that want the task active
///     BEFORE any splatters are registered (e.g. Day 3's scripted gore trash task) can still
///     call TriggerTask() explicitly first.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class CleanBloodTask : NetworkBehaviour, ISystemicThreat, IDailyTask
{
    public static CleanBloodTask Instance { get; private set; }

    [Header("Task Properties")]
    [SerializeField] private string _taskName = "Clean Blood";
    [Tooltip("Number of coupons the ATM dispenses when all blood has been scrubbed.")]
    [SerializeField] private int _couponReward = 10;

    [Tooltip("Forgiveness buffer: this many splatters are allowed to remain unscrubbed and the " +
             "task still completes. Helps when the last splatter or two is hard to spot/reach. " +
             "0 = must scrub every splatter. Never reduces the requirement below 1 splatter " +
             "(as long as at least one was registered).")]
    [SerializeField] private int _completionBuffer = 1;

    [Header("Daily Task")]
    [Tooltip("Stable identifier used by DailyTaskScheduler and SaveDataManager. Must match the TaskId entry in DailyTaskScheduler's pool, if this task is ever added to it.")]
    [SerializeField] private string _dailyTaskId = "CleanBlood";

    [Header("Cleanup Region")]
    [Tooltip("Region that decides which splatters COUNT toward this task. Leave empty to use " +
             "CheckpointCleanupArea.Instance (and, failing that, TakeOutTrashTask's own test). " +
             "Blood outside it is still fully mop-able and still credits when scrubbed — it just " +
             "can never be required.")]
    [SerializeField] private CheckpointCleanupArea _cleanupArea;

    [Tooltip("Seconds between server-side sweeps that drop splatters which no longer exist, or " +
             "have ended up outside the cleanup region, from the required total. This is what stops " +
             "a destroyed/stranded splatter from holding the task at 4/5 with no blood in sight. " +
             "0 disables the sweep.")]
    [SerializeField] private float _reconcileInterval = 2f;

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<int> _scrubbed = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Total blood splatters registered for this task run.</summary>
    private readonly NetworkVariable<int> _totalCount = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Whether this task is currently active and should appear in the HUD task list.
    /// Drives TaskRegistry registration on all clients, including late joiners.
    /// </summary>
    private readonly NetworkVariable<bool> _isActive = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Local state (server-only) ─────────────────────────────────────────────

    private readonly List<NetworkObject> _spawnedSplatters = new();
    private bool _taskActive;
    private bool _isComplete;

    /// <summary>
    /// Splatters that landed OUTSIDE the cleanup region. Fully mop-able and wired to the scrub
    /// callback like any other, but never added to <see cref="_totalCount"/> — instead, scrubbing one
    /// credits +1 total AND +1 scrubbed at that moment (see <see cref="OnBonusBloodScrubbed"/>), so
    /// the player's work visibly registers in the HUD without ever changing how much is still
    /// required inside the checkpoint. Mirrors <see cref="TakeOutTrashTask"/>'s bonus junk handling.
    /// </summary>
    private readonly List<NetworkObject> _bonusSplatters = new();

    /// <summary>
    /// True once at least one COUNTED splatter has been registered this run. Guards the reconcile
    /// sweep from "completing" a freshly-triggered task that hasn't been handed any blood yet
    /// (total 0, scrubbed 0 would otherwise satisfy the completion test immediately).
    /// </summary>
    private bool _hasRegisteredThisRun;

    private Coroutine _reconcileRoutine;

    /// <summary>
    /// Blood splatters registered via <see cref="RegisterTransientBloodSplatter"/> — dropped
    /// ambiently by mutants during a breach rather than as part of a scripted mop task. These
    /// are never counted toward a task total and never block clock-out; they just get swept away
    /// the next time a day starts (see <see cref="DespawnTransientSplattersOnDayStart"/>).
    /// </summary>
    private readonly List<NetworkObject> _transientSplatters = new();

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    /// <summary>
    /// Set by a day script (e.g. Day_01, in <c>DayActivated</c>/<c>DayDeactivated</c>) for the
    /// entire duration it will show its OWN hand-scripted TutorialObjectiveList row for this
    /// task — e.g. Day 1's post-breach "Clean up the blood" objective added in
    /// <c>Day_01.EnsureCleanBloodSplatterObjective</c>. Set it well before any blood can possibly
    /// be registered (activation time, not trigger time) so <see cref="HUDTaskList"/> never has
    /// a chance to add its own generic bridged row first — that race would leave a stale
    /// duplicate row behind even after this flag is later set. While true, HUDTaskList skips this
    /// threat entirely, mirroring the same exclusion already used for
    /// <see cref="TakeOutTrashTask"/>, <see cref="CleanGraffitiTask"/>, and
    /// <see cref="FenceRepairTask"/>. Days that don't hand-manage this task (e.g. Day 2+) leave
    /// this false and get the task's row purely from the generic HUDTaskList/TaskRegistry bridge.
    /// </summary>
    public bool HasCustomTutorialRow { get; set; }

    public string ThreatName  => _taskName;
    public float  ScoreWeight => 1f;

    /// <summary>
    /// Number of splatters that must actually be scrubbed to complete the task this cycle —
    /// the registered total minus <see cref="_completionBuffer"/>, floored at 1 splatter as long
    /// as at least one was ever registered.
    /// </summary>
    public int RequiredCount => _totalCount.Value > 0
        ? Mathf.Max(_totalCount.Value - _completionBuffer, 1)
        : 0;

    public float ThreatLevel => RequiredCount > 0
        ? 1f - Mathf.Clamp01((float)_scrubbed.Value / RequiredCount)
        : 0f;

    public string ThreatDescription =>
        _isComplete
            ? $"All {_totalCount.Value} splatter(s) scrubbed!"
            : _totalCount.Value > 0
                ? $"{Mathf.Min(_scrubbed.Value, RequiredCount)}/{RequiredCount}"
                : string.Empty;

    /// <summary>No-op — this task is triggered explicitly on Day 3, not by the night phase.</summary>
    public void BeginNightPhase() { }

    /// <summary>No-op.</summary>
    public void EndNightPhase() { }

    // ── IDailyTask ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string DailyTaskId => _dailyTaskId;

    /// <summary>Delegates to <see cref="TriggerTask"/>. Server-only; TriggerTask enforces the IsServer guard.</summary>
    public void TriggerDailyTask() => TriggerTask();

    /// <inheritdoc/>
    public event Action OnDailyTaskCompleted;

    /// <summary>
    /// Fired on every client whenever <see cref="ScrubbedCount"/> or <see cref="TotalCount"/>
    /// changes. Subscribe to drive live count updates in tutorial UI (e.g. Day_01's post-breach
    /// "Clean all blood splatter" objective).
    /// </summary>
    public static event Action OnProgressChanged;

    /// <summary>Blood splatters scrubbed clean so far this task run.</summary>
    public int ScrubbedCount => _scrubbed.Value;

    /// <summary>Total blood splatters registered for this task run.</summary>
    public int TotalCount => _totalCount.Value;

    /// <summary>Captures blood-cleanup progress; decal placement is owned by TakeOutTrashTask.</summary>
    public BloodTaskSaveState CaptureSaveState() => new()
    {
        IsActive = _isActive.Value,
        IsComplete = _isComplete,
        ScrubbedCount = _scrubbed.Value,
        TotalCount = _totalCount.Value
    };

    /// <summary>
    /// Applies saved logical progress after the trash task has rebuilt and registered its blood
    /// decals. The count remains authoritative even if an older snapshot lacks a decal prefab.
    /// </summary>
    public void RestoreSaveState(BloodTaskSaveState state)
    {
        if (!IsServer || state == null) return;
        _taskActive = state.IsActive;
        _isComplete = state.IsComplete;
        _hasRegisteredThisRun = state.TotalCount > 0;
        _scrubbed.Value = Mathf.Max(0, state.ScrubbedCount);
        _totalCount.Value = Mathf.Max(_scrubbed.Value, state.TotalCount);
        _isActive.Value = state.IsActive && !state.IsComplete;
        if (_taskActive)
        {
            EnsureReconcileRoutine();
            ShiftManager.Instance?.RegisterPendingDailyTask(this);
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CleanBloodTask] Duplicate instance detected — destroying self.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _scrubbed.OnValueChanged   += OnNetworkValueChanged;
        _totalCount.OnValueChanged += OnNetworkValueChanged;
        _isActive.OnValueChanged   += OnIsActiveChanged;

        // Handle the initial value for late-joining clients.
        if (_isActive.Value)
            TaskRegistry.Instance?.AddThreat(this);

        // _isComplete is otherwise only ever set by MarkCompleteClientRpc, which a client that
        // joined after the last splatter was scrubbed never received — leaving its HUD row showing
        // a stale "3/3" instead of the completed description. Derive it from the replicated counts
        // instead. Guarded to clients: on the server _isComplete is authoritative run state that
        // also gates re-completion (see TryCompleteTask), and must not be inferred from counts.
        if (!IsServer)
            _isComplete = RequiredCount > 0 && _scrubbed.Value >= RequiredCount;

        // Server-only: any breach-dropped blood splatters registered via
        // RegisterTransientBloodSplatter get swept away the next time a day starts, regardless
        // of whether anyone mopped them up.
        if (IsServer && ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += DespawnTransientSplattersOnDayStart;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _scrubbed.OnValueChanged   -= OnNetworkValueChanged;
        _totalCount.OnValueChanged -= OnNetworkValueChanged;
        _isActive.OnValueChanged   -= OnIsActiveChanged;

        if (IsServer && ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= DespawnTransientSplattersOnDayStart;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        OnDailyTaskCompleted = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Activates the blood-cleanup task fresh for this cycle: despawns any leftover splatters
    /// from a previous trigger, resets progress to zero, and marks the task active so
    /// subsequent <see cref="RegisterBloodSplatter"/> calls count toward the total. Server-only.
    /// Call this BEFORE spawning the gore/blood for the cycle (see class doc comment).
    /// </summary>
    public void TriggerTask()
    {
        if (!IsServer) return;

        DespawnExistingSplatters();

        _taskActive  = true;
        _isComplete  = false;
        _hasRegisteredThisRun = false;
        _scrubbed.Value    = 0;
        _totalCount.Value  = 0;

        _isActive.Value = true;

        EnsureReconcileRoutine();

        // Explicitly re-register on every client rather than relying solely on
        // _isActive's OnValueChanged — if the task was already active this cycle, that
        // NetworkVariable write is a no-op and OnIsActiveChanged never fires, silently
        // dropping the task from the HUD (see the equivalent fix in TakeOutTrashTask).
        RegisterInTaskRegistryClientRpc();

        // Block clock-out until every splatter registered this cycle is scrubbed clean —
        // same gating TakeOutTrashTask uses for trash.
        ShiftManager.Instance?.RegisterPendingDailyTask(this);
        SaveDataManager.Instance?.SaveCurrentWorkdayState();

        Debug.Log("[CleanBloodTask] Task triggered — awaiting blood splatter registrations.");
    }

    /// <summary>
    /// Registers an externally-spawned blood splatter's NetworkObject with this task — called
    /// by <see cref="TakeOutTrashTask"/> immediately after it spawns a blood decal alongside a
    /// gore piece, or by <see cref="MutantEnemy"/> for every gore piece dropped during a breach.
    /// Routes the splatter's <see cref="GraffitiInteractable.OnScrubCompleted"/> callback to this
    /// task so mopping it always registers somewhere.
    ///
    /// Whether it COUNTS is decided here, by position (see <see cref="CountsTowardCleanup"/>):
    ///   - Inside the cleanup region — counted toward <see cref="_totalCount"/> and required before
    ///     clock-out, exactly as before.
    ///   - Outside it — tracked as a bonus splatter instead: still mop-able, and scrubbing it
    ///     credits +1 total/+1 scrubbed so it visibly registers, but it is never required and can
    ///     never block completion. Callers must NOT pre-filter by position; this is the one decision
    ///     point, mirroring how out-of-region mutant corpses are handled.
    ///
    /// If no blood-cleanup task is currently active, a COUNTED splatter dynamically activates one
    /// (rather than being silently dropped) so blood spawned outside a scripted TriggerTask() call
    /// (e.g. a Day 1 mutant breach) still has to be scrubbed before clocking out. A bonus splatter
    /// never activates the task on its own — out-of-region blood alone is not a job.
    ///
    /// No-op if the splatter has no <see cref="GraffitiInteractable"/> (a purely cosmetic decal
    /// variant with nothing to mop). Server-only.
    /// </summary>
    public void RegisterBloodSplatter(NetworkObject netObj)
    {
        if (!IsServer || netObj == null) return;

        GraffitiInteractable interactable = netObj.GetComponent<GraffitiInteractable>();
        if (interactable == null) return;

        if (!CountsTowardCleanup(netObj.transform.position))
        {
            // Hook the callback even with no task running: GraffitiInteractable falls back to
            // GraffitiThreat.OnGraffitiScrubbed when OnScrubCompleted is null, so an unhooked blood
            // splatter would credit the *graffiti* task when mopped.
            interactable.OnScrubCompleted = () => OnBonusBloodScrubbed(netObj);
            _bonusSplatters.Add(netObj);

            // Still swept on the next day start like any other out-of-region blood, so it can't
            // accumulate across days.
            _transientSplatters.Add(netObj);

            Debug.Log($"[CleanBloodTask] Splatter '{netObj.name}' landed outside the checkpoint " +
                      "cleanup area — mop-able and credited as a bonus when scrubbed, but not required.");
            return;
        }

        if (!_taskActive)
            ActivateDynamically();

        _hasRegisteredThisRun = true;

        interactable.OnScrubCompleted = () => OnBloodScrubbed(netObj);

        _spawnedSplatters.Add(netObj);
        _totalCount.Value++;

        EnsureReconcileRoutine();
    }

    /// <summary>
    /// Removes <paramref name="netObj"/> from this task's tracking and, if it was a counted
    /// splatter, drops it from the required total (never below the number already scrubbed), then
    /// re-evaluates completion.
    ///
    /// Must be called by anything that destroys a registered blood decal WITHOUT it being scrubbed —
    /// notably <see cref="TakeOutTrashTask.DespawnExistingItems"/>, which wipes its own decals when
    /// its task is re-triggered. Without this the total kept counting decals that no longer existed,
    /// leaving the task permanently short (e.g. 4/5) with no blood anywhere in the yard: the exact
    /// "I cleaned it all and it never registered" report. Server-only.
    /// </summary>
    public void UnregisterBloodSplatter(NetworkObject netObj)
    {
        if (!IsServer || netObj == null) return;

        bool wasBonus = _bonusSplatters.Remove(netObj);
        if (!_spawnedSplatters.Remove(netObj))
        {
            if (wasBonus) _transientSplatters.Remove(netObj);
            return;
        }

        _totalCount.Value = Mathf.Max(_scrubbed.Value, _totalCount.Value - 1);

        Debug.Log($"[CleanBloodTask] Splatter '{netObj.name}' unregistered without being scrubbed — " +
                  $"new total {_totalCount.Value}.");

        TryCompleteTask();
    }

    /// <summary>
    /// Does a splatter at <paramref name="worldPosition"/> count toward this task? Prefers the
    /// scene's <see cref="CheckpointCleanupArea"/>, then <see cref="TakeOutTrashTask"/>'s equivalent
    /// test, and finally fails OPEN (counts it) when neither exists — matching the pre-region
    /// behaviour where everything handed to this task counted, so a scene without a region authored
    /// can't silently stop requiring blood.
    /// </summary>
    private bool CountsTowardCleanup(Vector3 worldPosition)
    {
        if (_cleanupArea == null)
            _cleanupArea = CheckpointCleanupArea.Instance;

        if (_cleanupArea != null && _cleanupArea.HasRegions)
            return _cleanupArea.Contains(worldPosition);

        if (TakeOutTrashTask.Instance != null)
            return TakeOutTrashTask.Instance.CountsTowardCleanup(worldPosition);

        return true;
    }

    /// <summary>
    /// Activates the blood-cleanup task without resetting progress or despawning existing
    /// splatters — used by <see cref="RegisterBloodSplatter"/> when a splatter is registered
    /// while no task is currently running. Server-only, expects <see cref="_taskActive"/> to
    /// already be false.
    /// </summary>
    private void ActivateDynamically()
    {
        _taskActive = true;
        _isComplete = false;

        _isActive.Value = true;
        RegisterInTaskRegistryClientRpc();

        ShiftManager.Instance?.RegisterPendingDailyTask(this);

        EnsureReconcileRoutine();

        Debug.Log("[CleanBloodTask] Dynamically activated — a blood splatter was registered with no active task.");
    }

    /// <summary>
    /// Registers an externally-spawned blood splatter that should NOT count toward a mop task
    /// or block clock-out — purely cosmetic blood. Tracked only so it can be swept away automatically
    /// the next time a day starts (see <see cref="DespawnTransientSplattersOnDayStart"/>); players are
    /// free to mop it up early for the visual, but it's never required.
    ///
    /// Prefer <see cref="RegisterBloodSplatter"/> for anything gameplay-spawned — it makes the
    /// counted/bonus decision by position instead of the caller having to. Server-only.
    /// </summary>
    public void RegisterTransientBloodSplatter(NetworkObject netObj)
    {
        if (!IsServer || netObj == null) return;

        // Claim the scrub callback even though nothing is counted: GraffitiInteractable falls back to
        // GraffitiThreat.OnGraffitiScrubbed when OnScrubCompleted is null, so mopping an unhooked
        // blood splatter would silently credit the *graffiti* task instead.
        GraffitiInteractable interactable = netObj.GetComponent<GraffitiInteractable>();
        if (interactable != null)
            interactable.OnScrubCompleted = () => _transientSplatters.Remove(netObj);

        _transientSplatters.Add(netObj);
    }

    /// <summary>
    /// Server-side <see cref="ShiftManager.OnDayStart"/> handler. Despawns every blood splatter
    /// registered via <see cref="RegisterTransientBloodSplatter"/> so breach gore never lingers
    /// past the day it was spawned, regardless of whether it was mopped up.
    /// </summary>
    private void DespawnTransientSplattersOnDayStart()
    {
        foreach (NetworkObject netObj in _transientSplatters)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _transientSplatters.Clear();
    }

    // ── Scrub callback (called by GraffitiInteractable on the server) ─────────

    /// <summary>
    /// Called by a registered splatter's <see cref="GraffitiInteractable"/> on the server once it has
    /// been fully scrubbed. Credits the scrub and re-evaluates completion.
    ///
    /// The scrub is credited even after the task has already completed. <see cref="_completionBuffer"/>
    /// means the task finishes with a splatter or two still on the ground, and swallowing those scrubs
    /// (as this used to) is precisely what made players report that cleaning blood "didn't register" —
    /// they mopped a splatter they could plainly see and nothing moved. Only the completion
    /// side-effects are one-shot.
    /// </summary>
    private void OnBloodScrubbed(NetworkObject netObj)
    {
        if (!IsServer) return;

        // Drop tracking BEFORE the splatter despawns, so the reconcile sweep can't also treat it as
        // a destroyed-without-scrubbing splatter and decrement the total for it a second time.
        _spawnedSplatters.Remove(netObj);

        _scrubbed.Value = Mathf.Clamp(_scrubbed.Value + 1, 0, _totalCount.Value);

        TryCompleteTask();
    }

    /// <summary>
    /// Scrub callback for a splatter outside the cleanup region (see <see cref="_bonusSplatters"/>).
    /// Raises BOTH the total and the scrubbed count by one, so the extra work shows up in the HUD
    /// readout ("3/5" becomes "4/6") while leaving the amount still outstanding inside the checkpoint
    /// unchanged — it can never be required, and can never block or shortcut completion.
    /// No-op when no task has ever run, in which case the splatter is purely cosmetic.
    /// </summary>
    private void OnBonusBloodScrubbed(NetworkObject netObj)
    {
        if (!IsServer) return;

        _bonusSplatters.Remove(netObj);
        _transientSplatters.Remove(netObj);

        if (!_taskActive && !_isComplete) return;

        _totalCount.Value++;
        _scrubbed.Value = Mathf.Clamp(_scrubbed.Value + 1, 0, _totalCount.Value);

        Debug.Log("[CleanBloodTask] Bonus splatter from outside the checkpoint scrubbed — " +
                  $"credited as {_scrubbed.Value}/{_totalCount.Value}.");

        TryCompleteTask();
    }

    /// <summary>
    /// Completes the task if everything still required has been scrubbed. Safe to call after any
    /// change to the scrubbed count or the total (a scrub, an unregister, a reconcile sweep) — the
    /// task can only complete once per run, and never before a counted splatter has been registered.
    /// </summary>
    private void TryCompleteTask()
    {
        if (!IsServer || _isComplete || !_taskActive) return;
        if (!_hasRegisteredThisRun) return;
        if (_scrubbed.Value < RequiredCount) return;

        _isComplete = true;
        _taskActive = false;

        // Tasks no longer pay coupons — players are only paid for processing suspects (see SuspectController.PayOutResults).
        // ATM.Instance?.SpawnCoupons(_couponReward);

        MarkCompleteClientRpc();

        // Hide from HUD once all splatters are clean. Registering a fresh batch of blood later
        // (e.g. a second source dropping splatters after this batch is already clean) correctly
        // re-adds this as a new task via ActivateDynamically — that's a genuinely new cleanup job,
        // not a duplicate of this one.
        _isActive.Value = false;
        SaveDataManager.Instance?.SaveCurrentWorkdayState();

        Debug.Log($"[CleanBloodTask] All required blood scrubbed ({_scrubbed.Value}/{RequiredCount}) — task complete.");
    }

    // ── Reconciliation ────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the periodic server-side reconcile sweep if it isn't already running.
    /// </summary>
    private void EnsureReconcileRoutine()
    {
        if (!IsServer || _reconcileRoutine != null || _reconcileInterval <= 0f) return;

        _reconcileRoutine = StartCoroutine(ReconcileRoutine());
    }

    private IEnumerator ReconcileRoutine()
    {
        while (_taskActive)
        {
            yield return new WaitForSeconds(_reconcileInterval);
            ReconcileSplatters();
        }

        _reconcileRoutine = null;
    }

    /// <summary>
    /// Drops from the required total any counted splatter that can no longer be scrubbed, then
    /// re-checks completion. Two cases, both of which used to strand the task at e.g. 4/5 with
    /// nothing left to mop — the "it looked complete but never registered" reports:
    ///
    /// 1. The splatter no longer exists. Blood decals are destroyed by systems that don't own this
    ///    task (<see cref="TakeOutTrashTask.DespawnExistingItems"/> on a re-trigger, scene teardown,
    ///    a despawned parent), and the total kept requiring them forever.
    /// 2. The splatter is no longer inside the cleanup region — e.g. it was authored/spawned on a
    ///    surface that later moved, or the region was retuned mid-run. It stays on the ground and
    ///    stays mop-able; it simply stops being required.
    ///
    /// Scrubbed splatters are deliberately untouched: <see cref="OnBloodScrubbed"/> removes them from
    /// tracking before they despawn, so case 1 can never double-count a legitimate scrub and complete
    /// the task early. Server-only.
    /// </summary>
    private void ReconcileSplatters()
    {
        if (!IsServer) return;

        // Bonus splatters count for nothing until scrubbed — just drop dead references.
        _bonusSplatters.RemoveAll(n => n == null || !n.IsSpawned);
        _transientSplatters.RemoveAll(n => n == null || !n.IsSpawned);

        // RemoveAll (not Remove) because a destroyed NetworkObject can't be reliably matched by
        // reference in a list — same reason TakeOutTrashTask.PruneCollectedItems does it this way.
        int vanished = _spawnedSplatters.RemoveAll(n => n == null || !n.IsSpawned);
        if (vanished > 0)
        {
            _totalCount.Value = Mathf.Max(_scrubbed.Value, _totalCount.Value - vanished);

            Debug.LogWarning($"[CleanBloodTask] {vanished} tracked splatter(s) no longer exist and " +
                             "were never scrubbed — dropped from the total so they can't block " +
                             $"completion. New total {_totalCount.Value}.");
        }

        // Snapshot: UnregisterBloodSplatter mutates the list.
        var tracked = new List<NetworkObject>(_spawnedSplatters);
        foreach (NetworkObject netObj in tracked)
        {
            if (CountsTowardCleanup(netObj.transform.position)) continue;

            Debug.LogWarning($"[CleanBloodTask] Tracked splatter '{netObj.name}' is outside the " +
                             "checkpoint cleanup area — unregistering it so it can't block completion.");
            UnregisterBloodSplatter(netObj);
        }

        TryCompleteTask();
    }

    [ClientRpc]
    private void MarkCompleteClientRpc()
    {
        _isComplete = true;
        TaskRegistry.Instance?.NotifyTaskStateChanged();

        // Fired on every client (including the host, which is also "a client" for RPC
        // purposes) so ShiftManager's clock-out gate — subscribed via RegisterPendingDailyTask
        // — actually unblocks. Mirrors TakeOutTrashTask.NotifyTaskCompletedClientRpc.
        OnDailyTaskCompleted?.Invoke();
    }

    private void DespawnExistingSplatters()
    {
        foreach (NetworkObject netObj in _spawnedSplatters)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _spawnedSplatters.Clear();
    }

    // ── Registry management ───────────────────────────────────────────────────

    [ClientRpc]
    private void RegisterInTaskRegistryClientRpc()
    {
        TaskRegistry.Instance?.AddThreat(this);
    }

    private void OnIsActiveChanged(bool previous, bool current)
    {
        if (current)
            TaskRegistry.Instance?.AddThreat(this);
        else
            TaskRegistry.Instance?.RemoveThreat(this);
    }

    private void OnNetworkValueChanged<T>(T previous, T current)
    {
        TaskRegistry.Instance?.NotifyTaskStateChanged();
        OnProgressChanged?.Invoke();
    }
}
