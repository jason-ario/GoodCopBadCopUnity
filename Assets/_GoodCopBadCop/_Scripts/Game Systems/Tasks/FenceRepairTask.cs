using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Between-shift task: repair a randomly selected set of broken perimeter fence segments.
///
/// At the start of each night phase the server:
///   1. Heals all fences back to their healthy state.
///   2. Picks a random count (within BrokenFenceCount range) of fences to break.
///   3. Assigns each a random damage level (within DamageRange).
///   4. Subscribes to OnFullyRepaired on every broken fence.
///
/// Players repair fences by hitting them with HammerPickable. When all broken fences
/// are repaired, the task is marked complete and the coupon reward is granted.
///
/// Scene setup:
///   - Add a NetworkObject component to this GameObject.
///   - Assign all PerimiterFence instances present in the scene to _allFences.
///   - Register this MonoBehaviour on BetweenShiftTaskManager via the Inspector.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class FenceRepairTask : NetworkBehaviour, IBetweenShiftTask
{
    public static FenceRepairTask Instance { get; private set; }

    [Header("Task Properties")]
    [SerializeField] private string _taskName = "Fix the Perimeter Fence";
    [SerializeField] private int _couponReward = 15;

    [Header("Fence Configuration")]
    [Tooltip("Every PerimiterFence in the scene. A random subset is broken each night.")]
    [SerializeField] private PerimiterFence[] _allFences;

    [Tooltip("Inclusive range for how many fence segments are broken per night phase.")]
    [SerializeField] private Vector2Int _brokenFenceCount = new Vector2Int(2, 4);

    [Tooltip("Inclusive range for the starting damage level assigned to each broken fence. " +
             "Min 1 (slightly damaged) — must not exceed the fence's MaxDamageLevel.")]
    [SerializeField] private Vector2Int _damageRange = new Vector2Int(1, 3);

    // ── IBetweenShiftTask ────────────────────────────────────────────────────

    public string TaskName     => _taskName;
    public int    CouponReward => _couponReward;
    public bool   IsComplete   => _isComplete;

    /// <summary>Dynamic description updated as fences are repaired.</summary>
    public string TaskDescription =>
        _isComplete
            ? "All fence segments repaired!"
            : $"Repair broken fence segments: {_fencesRepaired.Value}/{_targetFenceCount.Value}";

    // ── Networked state ──────────────────────────────────────────────────────

    private NetworkVariable<int> _targetFenceCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkVariable<int> _fencesRepaired = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Local flag propagated to all clients via MarkCompleteClientRpc.
    private bool _isComplete;

    // Server-only: which fences were broken this round (for cleanup on the next reset).
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
        _targetFenceCount.OnValueChanged += OnProgressChanged;
        _fencesRepaired.OnValueChanged   += OnProgressChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _targetFenceCount.OnValueChanged -= OnProgressChanged;
        _fencesRepaired.OnValueChanged   -= OnProgressChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── IBetweenShiftTask ────────────────────────────────────────────────────

    /// <summary>
    /// Resets all fences to healthy, then randomly breaks a subset at varied damage levels.
    /// Non-server clients call this too (per BetweenShiftTaskManager.BeginNightPhase()),
    /// but only the server performs the authoritative break logic.
    /// </summary>
    public void ResetTask()
    {
        _isComplete = false;

        if (!IsServer) return;

        _fencesRepaired.Value = 0;

        UnsubscribeFromAllBrokenFences();
        HealAllFences();

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

        Debug.Log($"[FenceRepairTask] Night phase begun: {count} fence segment(s) broken.");
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
        _isComplete = true;

        if (BetweenShiftTaskManager.Instance != null)
            BetweenShiftTaskManager.Instance.NotifyTaskComplete(this);

        MarkCompleteClientRpc();
    }

    [ClientRpc]
    private void MarkCompleteClientRpc()
    {
        _isComplete = true;
        GuidebookTaskRegistry.Instance?.NotifyTaskStateChanged();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Restores all scene fences to their healthy state (damage level 0).</summary>
    private void HealAllFences()
    {
        if (_allFences == null) return;

        foreach (PerimiterFence fence in _allFences)
        {
            if (fence != null)
                fence.SetDamageLevelServer(0);
        }
    }

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
    /// Returns a random count of fences to break, clamped to the available fence pool size.
    /// </summary>
    private int PickBrokenFenceCount()
    {
        if (_allFences == null || _allFences.Length == 0)
        {
            Debug.LogWarning("[FenceRepairTask] _allFences is empty — no fences will be broken.");
            return 0;
        }

        int max = Mathf.Min(_brokenFenceCount.y, _allFences.Length);
        int min = Mathf.Min(_brokenFenceCount.x, max);
        return Random.Range(min, max + 1);
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

    /// <summary>Notifies the guidebook to refresh task row text whenever progress changes.</summary>
    private void OnProgressChanged(int previous, int current)
    {
        GuidebookTaskRegistry.Instance?.NotifyTaskStateChanged();
    }
}
