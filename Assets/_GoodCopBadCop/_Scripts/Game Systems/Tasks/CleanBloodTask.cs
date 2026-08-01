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
/// Scene setup:
///   - NetworkObject on this GameObject (in-scene placed — no prefab registration needed).
///   - No prefabs or spawn points to assign here — TakeOutTrashTask (or MutantEnemy, for gore
///     landing in the yard during a mutant breach) feeds this task splatters directly via
///     RegisterBloodSplatter.
///   - The blood decal prefab(s) fed in must each have a GraffitiInteractable component (e.g.
///     "Random Blood Splatter Variant.prefab") — decals without one are skipped (treated as
///     purely cosmetic, not counted).
///   - Calling TriggerTask() is optional — RegisterBloodSplatter auto-activates the task the
///     first time a splatter is registered while it's inactive, so days/events that spawn gore
///     dynamically (e.g. a Day 1 mutant breach) don't need to call TriggerTask() themselves.
///     Days that want the task active BEFORE any splatters are registered (e.g. Day 3's
///     scripted gore trash task) can still call TriggerTask() explicitly first.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class CleanBloodTask : NetworkBehaviour, ISystemicThreat, IDailyTask
{
    public static CleanBloodTask Instance { get; private set; }

    [Header("Task Properties")]
    [SerializeField] private string _taskName = "Clean Blood";
    [Tooltip("Number of coupons the ATM dispenses when all blood has been scrubbed.")]
    [SerializeField] private int _couponReward = 10;

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

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public string ThreatName  => _taskName;
    public float  ScoreWeight => 1f;

    public float ThreatLevel => _totalCount.Value > 0
        ? 1f - Mathf.Clamp01((float)_scrubbed.Value / _totalCount.Value)
        : 0f;

    public string ThreatDescription =>
        _isComplete
            ? $"All {_totalCount.Value} splatter(s) scrubbed!"
            : _totalCount.Value > 0
                ? $"Scrub blood: {_scrubbed.Value}/{_totalCount.Value}"
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
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _scrubbed.OnValueChanged   -= OnNetworkValueChanged;
        _totalCount.OnValueChanged -= OnNetworkValueChanged;
        _isActive.OnValueChanged   -= OnIsActiveChanged;
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

        if (_scrubbed.Value < _totalCount.Value) return;

        _isComplete = true;
        _taskActive = false;

        ATM.Instance?.SpawnCoupons(_couponReward);

        MarkCompleteClientRpc();

        // Hide from HUD once all splatters are clean.
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
