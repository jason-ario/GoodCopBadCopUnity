using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// One-shot task: repair every broken perimeter fence segment.
///
/// Unlike <see cref="FenceThreat"/> (a continuous night-phase systemic threat active from
/// Day <c>_firstActiveDay</c> onward), this task is manually triggered — e.g. after Day 1's
/// mutant breach or as a scripted Day 3 tutorial beat — and breaks a batch of fences
/// immediately rather than damaging them one at a time on a timer.
///
/// ── Progress model ────────────────────────────────────────────────────────────
/// Progress is <em>derived</em>, never incremented. On every authoritative fence state change
/// the server recounts how many tracked segments are pristine
/// (<see cref="PerimiterFence.IsRepaired"/>) and writes that count to a NetworkVariable.
///
/// The previous implementation incremented a counter from the one-shot
/// <see cref="PerimiterFence.OnFullyRepaired"/> event and set the target to the number of
/// fences it *intended* to break. Any missed event, any segment that ended up not actually
/// damaged, and any segment a mutant had already destroyed left the counter permanently one
/// or more short — the reported "14/15 with nothing broken left to repair". A derived count
/// cannot drift: it is recomputed from live fence state, so it is always exactly right and
/// self-heals.
///
/// The tracked set is also every fence that is <em>currently</em> broken, not just the random
/// subset this task rolled. Fences smashed by mutants during the breach (or while the task is
/// already running) are picked up automatically, so repairing them counts toward the objective
/// instead of being invisible to it.
///
/// ── Networking ────────────────────────────────────────────────────────────────
/// Counts, active state, and completion all live in NetworkVariables, so the host, every
/// connected client, and any late joiner read identical values. Completion is announced off
/// <see cref="_isComplete"/>'s change callback rather than a ClientRpc, so a client that was
/// not connected when the last fence was fixed still observes the correct final state.
///
/// Scene setup:
///   - Add a NetworkObject component to this GameObject.
///   - Assign all PerimiterFence instances present in the scene to _allFences.
///
/// Also implements <see cref="ISystemicThreat"/> so <see cref="CheckpointIntegrityService"/>
/// can factor broken/unrepaired fence segments into the Checkpoint Integrity Score.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class FenceRepairTask : NetworkBehaviour, ISystemicThreat
{
    public static FenceRepairTask Instance { get; private set; }

    [Header("Task Properties")]
    [SerializeField] private string _taskName = "Fix Perimeter Fences";
    [SerializeField] private int _couponReward = 15;
    [Tooltip("Relative weight of this threat when computing the Checkpoint Integrity Score.")]
    [SerializeField] private float _scoreWeight = 1f;

    [Header("Fence Configuration")]
    [Tooltip("Every PerimiterFence in the scene. A random subset is broken each time TriggerTask() is called.")]
    [SerializeField] private PerimiterFence[] _allFences;

    [Tooltip("Inclusive range (0–1) for the fraction of _allFences broken per trigger. " +
             "E.g. 0.4–0.6 breaks roughly 40–60% of all fences.")]
    [SerializeField] private Vector2 _brokenFencePercentage = new Vector2(0.4f, 0.6f);

    [Tooltip("Inclusive range for the starting damage level assigned to each broken fence. " +
             "Min 1 (slightly damaged) — must not exceed the fence's MaxDamageLevel.")]
    [SerializeField] private Vector2Int _damageRange = new Vector2Int(1, 3);

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Display name for this task.</summary>
    public string TaskName => _taskName;

    /// <summary>Money awarded upon completion.</summary>
    public int CouponReward => _couponReward;

    /// <summary>True once every broken fence from the most recent trigger has been repaired.</summary>
    public bool IsComplete => _isComplete.Value;

    /// <summary>Number of fences repaired so far this round.</summary>
    public int RepairedCount => _fencesRepaired.Value;

    /// <summary>Number of fences broken this round (the round's target).</summary>
    public int TotalCount => _targetFenceCount.Value;

    /// <summary>Captures the visual damage state of each authored fence in stable inspector order.</summary>
    public FenceTaskSaveState CaptureSaveState()
    {
        int count = _allFences?.Length ?? 0;
        var damageStates = new int[count];
        for (int i = 0; i < count; i++)
            damageStates[i] = _allFences[i] != null ? _allFences[i].DamageState : 0;

        return new FenceTaskSaveState
        {
            IsActive = _isActive.Value,
            IsComplete = _isComplete.Value,
            DamageStates = damageStates
        };
    }

    /// <summary>
    /// Restores the authoritative fence damage states and rebuilds live observation from that
    /// restored world state, so a save can never retain a fence objective for a repaired/missing
    /// segment or omit a visibly broken segment from the count.
    /// </summary>
    public void RestoreSaveState(FenceTaskSaveState state)
    {
        if (!IsServer || state == null) return;

        StopObservingFences();
        _trackedFences.Clear();
        int count = Mathf.Min(_allFences?.Length ?? 0, state.DamageStates?.Length ?? 0);
        for (int i = 0; i < count; i++)
        {
            if (_allFences[i] != null)
                _allFences[i].SetDamageLevelServer(state.DamageStates[i]);
        }

        _isComplete.Value = false;
        _isActive.Value = state.IsActive;
        if (!_isActive.Value)
        {
            _targetFenceCount.Value = 0;
            _fencesRepaired.Value = 0;
            _isComplete.Value = state.IsComplete;
            return;
        }

        StartObservingFences();
        RebuildTrackedFences();
        if (_trackedFences.Count == 0)
        {
            StopObservingFences();
            _isActive.Value = false;
            _isComplete.Value = state.IsComplete;
            _targetFenceCount.Value = 0;
            _fencesRepaired.Value = 0;
            return;
        }

        RecomputeProgress();
    }

    /// <summary>Dynamic description updated as fences are repaired.</summary>
    public string TaskDescription =>
        IsComplete
            ? "All fence segments repaired!"
            : $"{RepairedCount}/{TotalCount}";

    /// <summary>Fired on every client whenever the repaired/total counts change.</summary>
    public static event Action OnProgressChanged;

    /// <summary>Fired on every client once every broken fence has been repaired.</summary>
    public static event Action OnAllFencesRepaired;

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string ThreatName  => _taskName;

    /// <inheritdoc/>
    public float  ScoreWeight => _scoreWeight;

    /// <summary>
    /// Fraction of this round's broken fences still unrepaired (0 = all repaired/no fences
    /// currently broken, 1 = none repaired yet).
    /// </summary>
    public float ThreatLevel =>
        TotalCount > 0 ? 1f - Mathf.Clamp01((float)RepairedCount / TotalCount) : 0f;

    /// <inheritdoc/>
    public string ThreatDescription => TaskDescription;

    /// <summary>
    /// Set by a day script (e.g. Day 1) while it is showing its own hand-scripted tutorial row
    /// for this task (see Day_01.EnsureFixFencesObjective). While true, HUDTaskList's generic
    /// TaskRegistry bridge skips adding its own row, preventing a duplicate "Fix Perimeter
    /// Fences" entry in the tutorial objective list.
    /// </summary>
    public bool HasCustomTutorialRow { get; set; }

    /// <summary>No-op — this task is manually triggered (e.g. a scripted day beat), not by the night phase.</summary>
    public void BeginNightPhase() { }

    /// <summary>No-op.</summary>
    public void EndNightPhase() { }

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<int> _targetFenceCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _fencesRepaired = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Whether this task is currently active and should appear in the HUD task list.
    /// Drives TaskRegistry registration on all clients, including late joiners.
    /// </summary>
    private readonly NetworkVariable<bool> _isActive = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Replicated completion latch. Networked (rather than a local bool set by a ClientRpc) so a
    /// client that connects after the final repair still reports the task complete instead of
    /// showing a permanently stalled "n/n" row.
    /// </summary>
    private readonly NetworkVariable<bool> _isComplete = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Server-only: the fences this round's objective is counting.
    private readonly List<PerimiterFence> _trackedFences = new();

    // Server-only: fences we currently hold an OnDamageStateChangedServer subscription on.
    private readonly HashSet<PerimiterFence> _observedFences = new();

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[FenceRepairTask] Duplicate instance detected — destroying self.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _targetFenceCount.OnValueChanged += OnProgressChangedInternal;
        _fencesRepaired.OnValueChanged   += OnProgressChangedInternal;
        _isActive.OnValueChanged         += OnIsActiveChanged;
        _isComplete.OnValueChanged       += OnIsCompleteChanged;

        // Handle the initial value for late-joining clients: if the task was already active
        // before this client connected, register it in TaskRegistry immediately.
        if (_isActive.Value)
            TaskRegistry.Instance?.AddThreat(this);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _targetFenceCount.OnValueChanged -= OnProgressChangedInternal;
        _fencesRepaired.OnValueChanged   -= OnProgressChangedInternal;
        _isActive.OnValueChanged         -= OnIsActiveChanged;
        _isComplete.OnValueChanged       -= OnIsCompleteChanged;

        if (IsServer) StopObservingFences();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        OnProgressChanged     = null;
        OnAllFencesRepaired   = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Randomly damages a subset of fences on top of whatever state they're already in, then
    /// starts tracking every currently broken segment. Never heals fences — damage is only ever
    /// raised (see <see cref="PerimiterFence.EnsureMinimumDamageLevelServer"/>) and repair only
    /// ever happens via <see cref="HammerPickable"/> hits.
    /// Server-only — safe to call from any client, but only the server performs the
    /// authoritative break logic (replicated to clients via the NetworkVariables above).
    /// </summary>
    public void TriggerTask()
    {
        if (!IsServer) return;

        StopObservingFences();
        _trackedFences.Clear();

        _isComplete.Value     = false;
        _fencesRepaired.Value = 0;

        int count = PickBrokenFenceCount();
        PerimiterFence[] shuffled = ShuffleCopy(_allFences);

        for (int i = 0; i < count && i < shuffled.Length; i++)
        {
            PerimiterFence fence = shuffled[i];
            if (fence == null) continue;

            int maxAllowed  = fence.MaxDamageLevel;
            int damageMin   = Mathf.Clamp(_damageRange.x, 1, Mathf.Max(1, maxAllowed));
            int damageMax   = Mathf.Clamp(_damageRange.y, damageMin, Mathf.Max(1, maxAllowed));
            int damageLevel = Random.Range(damageMin, damageMax + 1);

            // Raise damage only — a fence a mutant already smashed to rubble must not be healed
            // back up to the rolled level (which is what the old absolute SetDamageLevelServer
            // call did).
            fence.EnsureMinimumDamageLevelServer(damageLevel);
        }

        // Track every segment that is actually broken right now — including ones mutants
        // destroyed during the breach — so the objective total matches what the players can see,
        // and repairing any of them counts.
        StartObservingFences();
        RebuildTrackedFences();

        Debug.Log($"[FenceRepairTask] Task triggered: {_trackedFences.Count} broken fence segment(s) tracked " +
                  $"(rolled {count}).");

        _isActive.Value = _trackedFences.Count > 0;

        // Explicitly re-register on every client rather than relying solely on
        // _isActive's OnValueChanged — if the task was already active this cycle (e.g. a
        // debug re-trigger of Day 3), that NetworkVariable write is a no-op and
        // OnIsActiveChanged never fires, silently dropping the task from the HUD.
        if (_isActive.Value)
            RegisterInTaskRegistryClientRpc();

        // A trigger that broke nothing is immediately satisfied.
        RecomputeProgress();
        SaveDataManager.Instance?.SaveCurrentWorkdayState();
    }

    // ── Repair flow ──────────────────────────────────────────────────────────

    /// <summary>
    /// Server-side callback fired for every authoritative fence health change while this task is
    /// running. Newly broken segments join the tracked set; progress is then recomputed from
    /// scratch, which is what makes the counter impossible to stall.
    /// </summary>
    private void HandleFenceStateChanged(PerimiterFence fence)
    {
        if (!IsServer || _isComplete.Value) return;

        if (fence != null && fence.IsBroken && !_trackedFences.Contains(fence))
            _trackedFences.Add(fence);

        RecomputeProgress();
    }

    /// <summary>
    /// Recounts repaired/total from live fence state and completes the task when nothing tracked
    /// is broken any more. Server-only.
    /// </summary>
    private void RecomputeProgress()
    {
        if (!IsServer || _isComplete.Value) return;

        _trackedFences.RemoveAll(f => f == null);

        int total    = _trackedFences.Count;
        int repaired = 0;

        for (int i = 0; i < _trackedFences.Count; i++)
        {
            if (_trackedFences[i].IsRepaired) repaired++;
        }

        _targetFenceCount.Value = total;
        _fencesRepaired.Value   = Mathf.Clamp(repaired, 0, total);
        SaveDataManager.Instance?.SaveCurrentWorkdayState();

        if (total <= 0 || repaired < total) return;

        // Every tracked segment is pristine — latch completion.
        // Tasks no longer pay coupons — players are only paid for processing suspects
        // (see SuspectController.PayOutResults).
        // ATM.Instance?.SpawnCoupons(_couponReward);

        StopObservingFences();

        _isComplete.Value = true;   // → OnIsCompleteChanged on every peer, host included.
        _isActive.Value   = false;  // Hide from HUD once all fences are repaired.

        Debug.Log($"[FenceRepairTask] All {total} tracked fence segment(s) repaired — task complete.");
    }

    /// <summary>Runs on every peer (including the host) when the replicated completion latch flips.</summary>
    private void OnIsCompleteChanged(bool previous, bool current)
    {
        TaskRegistry.Instance?.NotifyTaskStateChanged();

        if (current && !previous)
            OnAllFencesRepaired?.Invoke();
    }

    // ── Registry management ──────────────────────────────────────────────────

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

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Subscribes to state changes on EVERY fence (not only the ones this task broke) so a
    /// segment that mutants destroy mid-task still enters the objective, and repairing it counts.
    /// Server-only.
    /// </summary>
    private void StartObservingFences()
    {
        if (_allFences == null) return;

        foreach (PerimiterFence fence in _allFences)
        {
            if (fence == null || !_observedFences.Add(fence)) continue;
            fence.OnDamageStateChangedServer += HandleFenceStateChanged;
        }
    }

    private void StopObservingFences()
    {
        foreach (PerimiterFence fence in _observedFences)
        {
            if (fence != null)
                fence.OnDamageStateChangedServer -= HandleFenceStateChanged;
        }
        _observedFences.Clear();
    }

    /// <summary>Rebuilds the tracked set from whichever fences are currently broken. Server-only.</summary>
    private void RebuildTrackedFences()
    {
        _trackedFences.Clear();

        if (_allFences == null) return;

        foreach (PerimiterFence fence in _allFences)
        {
            if (fence != null && fence.IsBroken && !_trackedFences.Contains(fence))
                _trackedFences.Add(fence);
        }
    }

    /// <summary>
    /// Returns a random count of fences to break, derived from _brokenFencePercentage
    /// applied to the total fence pool size (always at least 1 if any fences exist).
    /// </summary>
    private int PickBrokenFenceCount()
    {
        if (_allFences == null || _allFences.Length == 0)
        {
            Debug.LogWarning("[FenceRepairTask] _allFences is empty — no fences will be broken.");
            return 0;
        }

        float minPct = Mathf.Clamp01(Mathf.Min(_brokenFencePercentage.x, _brokenFencePercentage.y));
        float maxPct = Mathf.Clamp01(Mathf.Max(_brokenFencePercentage.x, _brokenFencePercentage.y));
        float pct = Random.Range(minPct, maxPct);

        int count = Mathf.RoundToInt(_allFences.Length * pct);
        return Mathf.Clamp(count, 1, _allFences.Length);
    }

    /// <summary>Returns a Fisher-Yates shuffled copy of the source array.</summary>
    private static T[] ShuffleCopy<T>(T[] source)
    {
        if (source == null) return Array.Empty<T>();

        T[] copy = (T[])source.Clone();
        for (int i = copy.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy;
    }

    /// <summary>Notifies the task registry and static subscribers whenever progress changes.</summary>
    private void OnProgressChangedInternal(int previous, int current)
    {
        TaskRegistry.Instance?.NotifyTaskStateChanged();
        OnProgressChanged?.Invoke();
    }
}
