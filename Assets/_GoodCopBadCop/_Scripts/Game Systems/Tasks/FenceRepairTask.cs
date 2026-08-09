using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// One-shot task: repair a randomly selected set of broken perimeter fence segments.
///
/// Unlike <see cref="FenceThreat"/> (a continuous night-phase systemic threat active from
/// Day <c>_firstActiveDay</c> onward), this task is manually triggered — e.g. once at the start
/// of Day 3 as a scripted tutorial beat (see <see cref="Day_03"/>) — and breaks a batch of
/// fences immediately rather than damaging them one at a time on a timer.
///
/// When triggered on the server:
///   1. Picks a random count (within BrokenFenceCount range) of fences to break.
///   2. Assigns each a random damage level (within DamageRange).
///   3. Subscribes to OnFullyRepaired on every broken fence.
///
/// Players repair fences by hitting them with HammerPickable. When all broken fences are
/// repaired, the task is marked complete, the coupon reward is granted, and
/// <see cref="OnAllFencesRepaired"/> fires so subscribers (e.g. Day_03's tutorial objective
/// list entry) can react.
///
/// Scene setup:
///   - Add a NetworkObject component to this GameObject.
///   - Assign all PerimiterFence instances present in the scene to _allFences.
///
/// Also implements <see cref="ISystemicThreat"/> so <see cref="CheckpointIntegrityService"/>
/// can factor broken/unrepaired fence segments into the Checkpoint Integrity Score, alongside
/// <see cref="CleanGraffitiTask"/> and <see cref="TakeOutTrashTask"/>.
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
    public bool IsComplete => _isComplete;

    /// <summary>Number of fences repaired so far this round.</summary>
    public int RepairedCount => _fencesRepaired.Value;

    /// <summary>Number of fences broken this round (the round's target).</summary>
    public int TotalCount => _targetFenceCount.Value;

    /// <summary>Dynamic description updated as fences are repaired.</summary>
    public string TaskDescription =>
        _isComplete
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
    /// currently broken, 1 = none repaired yet). Mirrors the pattern used by
    /// <see cref="CleanGraffitiTask"/> and <see cref="TakeOutTrashTask"/>.
    /// </summary>
    public float ThreatLevel =>
        TotalCount > 0 ? 1f - Mathf.Clamp01((float)RepairedCount / TotalCount) : 0f;

    /// <inheritdoc/>
    public string ThreatDescription => TaskDescription;

    /// <summary>
    /// Set by a day script (e.g. Day 1) while it is showing its own hand-scripted tutorial row
    /// for this task (see Day_01.EnsureFixFencesObjective). While true, HUDTaskList's generic
    /// TaskRegistry bridge skips adding its own row, preventing a duplicate "Fix Perimeter
    /// Fences" entry in the tutorial objective list. Mirrors the pattern used by
    /// <see cref="TakeOutTrashTask"/> and <see cref="CleanGraffitiTask"/>.
    /// </summary>
    public bool HasCustomTutorialRow { get; set; }

    /// <summary>No-op — this task is manually triggered (e.g. a scripted day beat), not by the night phase.</summary>
    public void BeginNightPhase() { }

    /// <summary>No-op.</summary>
    public void EndNightPhase() { }

    // ── Networked state ──────────────────────────────────────────────────────

    private NetworkVariable<int> _targetFenceCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkVariable<int> _fencesRepaired = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Whether this task is currently active and should appear in the HUD task list.
    /// Drives TaskRegistry registration on all clients, including late joiners. Mirrors the
    /// pattern used by <see cref="CleanBloodTask"/> and <see cref="TakeOutTrashTask"/> — without
    /// this, FenceRepairTask was never added to TaskRegistry at all, so it never showed up in
    /// HUDTaskList/TutorialObjectiveList regardless of when it was triggered.
    /// </summary>
    private readonly NetworkVariable<bool> _isActive = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Local flag propagated to all clients via MarkCompleteClientRpc.
    private bool _isComplete;

    // Server-only: which fences were broken this round (for cleanup on the next trigger).
    private readonly List<PerimiterFence> _brokenFences = new();

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
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        OnProgressChanged     = null;
        OnAllFencesRepaired   = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Randomly breaks a subset of fences at varied damage levels, on top of whatever state
    /// they're already in. Never heals fences directly — repair only ever happens via
    /// <see cref="HammerPickable"/> hits on <see cref="PerimiterFence"/>.
    /// Server-only — safe to call from any client, but only the server performs the
    /// authoritative break logic (replicated to clients via the NetworkVariables above).
    /// </summary>
    public void TriggerTask()
    {
        if (!IsServer) return;

        _isComplete = false;
        _fencesRepaired.Value = 0;

        UnsubscribeFromAllBrokenFences();

        _brokenFences.Clear();

        int count = PickBrokenFenceCount();
        _targetFenceCount.Value = count;

        PerimiterFence[] shuffled = ShuffleCopy(_allFences);

        for (int i = 0; i < count && i < shuffled.Length; i++)
        {
            PerimiterFence fence = shuffled[i];
            if (fence == null) continue;

            int maxAllowed = fence.MaxDamageLevel;
            int damageMin  = Mathf.Min(_damageRange.x, maxAllowed);
            int damageMax  = Mathf.Min(_damageRange.y, maxAllowed);
            int damageLevel = Random.Range(damageMin, damageMax + 1);

            fence.SetDamageLevelServer(damageLevel);
            fence.OnFullyRepaired += HandleFenceRepaired;
            _brokenFences.Add(fence);
        }

        Debug.Log($"[FenceRepairTask] Task triggered: {count} fence segment(s) broken.");

        _isActive.Value = true;

        // Explicitly re-register on every client rather than relying solely on
        // _isActive's OnValueChanged — if the task was already active this cycle (e.g. a
        // debug re-trigger of Day 3), that NetworkVariable write is a no-op and
        // OnIsActiveChanged never fires, silently dropping the task from the HUD. Mirrors
        // the equivalent fix in TakeOutTrashTask / CleanBloodTask.
        RegisterInTaskRegistryClientRpc();
    }

    // ── Repair flow ──────────────────────────────────────────────────────────

    /// <summary>
    /// Called on the server when a fence's damage reaches 0.
    /// Increments the repair counter and completes the task when all fences are fixed.
    /// </summary>
    private void HandleFenceRepaired(PerimiterFence fence)
    {
        Debug.Assert(IsServer, "[FenceRepairTask] HandleFenceRepaired called on non-server.");

        fence.OnFullyRepaired -= HandleFenceRepaired;

        _fencesRepaired.Value = Mathf.Clamp(_fencesRepaired.Value + 1, 0, _targetFenceCount.Value);

        Debug.Log($"[FenceRepairTask] Fence repaired ({_fencesRepaired.Value}/{_targetFenceCount.Value}).");

        if (_fencesRepaired.Value < _targetFenceCount.Value) return;

        // All broken fences have been repaired — complete the task.
        // Tasks no longer pay coupons — players are only paid for processing suspects (see SuspectController.PayOutResults).
        // ATM.Instance?.SpawnCoupons(_couponReward);

        MarkCompleteClientRpc();

        // Hide from HUD once all fences are repaired.
        _isActive.Value = false;
    }

    [ClientRpc]
    private void MarkCompleteClientRpc()
    {
        _isComplete = true;
        TaskRegistry.Instance?.NotifyTaskStateChanged();
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

    /// <summary>Removes HandleFenceRepaired subscriptions from all previously broken fences.</summary>
    private void UnsubscribeFromAllBrokenFences()
    {
        foreach (PerimiterFence fence in _brokenFences)
        {
            if (fence != null)
                fence.OnFullyRepaired -= HandleFenceRepaired;
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
