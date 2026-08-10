using System;
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
/// Blood dropped ambiently by mutant gore during a breach does NOT go through this scripted
/// mop task — <see cref="MutantEnemy"/> feeds those splatters through
/// <see cref="RegisterTransientBloodSplatter"/> instead, which never blocks clock-out and simply
/// despawns them the next time a day starts (see <see cref="DespawnTransientSplattersOnDayStart"/>).
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
        _scrubbed.Value    = 0;
        _totalCount.Value  = 0;

        _isActive.Value = true;

        // Explicitly re-register on every client rather than relying solely on
        // _isActive's OnValueChanged — if the task was already active this cycle, that
        // NetworkVariable write is a no-op and OnIsActiveChanged never fires, silently
        // dropping the task from the HUD (see the equivalent fix in TakeOutTrashTask).
        RegisterInTaskRegistryClientRpc();

        // Block clock-out until every splatter registered this cycle is scrubbed clean —
        // same gating TakeOutTrashTask uses for trash.
        ShiftManager.Instance?.RegisterPendingDailyTask(this);

        Debug.Log("[CleanBloodTask] Task triggered — awaiting blood splatter registrations.");
    }

    /// <summary>
    /// Registers an externally-spawned blood splatter's NetworkObject with this task — called
    /// by <see cref="TakeOutTrashTask"/> immediately after it spawns a blood decal alongside a
    /// gore piece, or by <see cref="MutantEnemy"/> when a gore piece it dropped lands inside the
    /// yard during a mutant breach. Routes the splatter's <see cref="GraffitiInteractable.OnScrubCompleted"/>
    /// callback to this task and counts it toward the total.
    ///
    /// If no blood-cleanup task is currently active, this dynamically activates one (rather than
    /// silently dropping the splatter) so any blood spawned outside a scripted TriggerTask() call
    /// (e.g. a Day 1 mutant breach) still has to be scrubbed before clocking out.
    ///
    /// No-op if the splatter has no <see cref="GraffitiInteractable"/> (a purely cosmetic decal
    /// variant with nothing to mop). Server-only.
    /// </summary>
    public void RegisterBloodSplatter(NetworkObject netObj)
    {
        if (!IsServer || netObj == null) return;

        GraffitiInteractable interactable = netObj.GetComponent<GraffitiInteractable>();
        if (interactable == null) return;

        if (!_taskActive)
            ActivateDynamically();

        interactable.OnScrubCompleted = OnBloodScrubbed;

        _spawnedSplatters.Add(netObj);
        _totalCount.Value++;
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

        Debug.Log("[CleanBloodTask] Dynamically activated — a blood splatter was registered with no active task.");
    }

    /// <summary>
    /// Registers an externally-spawned blood splatter that should NOT count toward a mop task
    /// or block clock-out — used by <see cref="MutantEnemy"/> for blood dropped ambiently by
    /// mutant gore during a breach. Tracked only so it can be swept away automatically the next
    /// time a day starts (see <see cref="DespawnTransientSplattersOnDayStart"/>); players are
    /// free to mop it up early for the visual, but it's never required. Server-only.
    /// </summary>
    public void RegisterTransientBloodSplatter(NetworkObject netObj)
    {
        if (!IsServer || netObj == null) return;

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
    /// Called by a registered splatter's <see cref="GraffitiInteractable"/> on the server once
    /// it has been fully scrubbed. Increments progress and completes the task once every
    /// splatter registered this cycle has been cleaned.
    /// </summary>
    private void OnBloodScrubbed()
    {
        if (!IsServer || _isComplete) return;

        _scrubbed.Value = Mathf.Clamp(_scrubbed.Value + 1, 0, _totalCount.Value);

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

        Debug.Log("[CleanBloodTask] All blood splatters scrubbed — task complete.");
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
