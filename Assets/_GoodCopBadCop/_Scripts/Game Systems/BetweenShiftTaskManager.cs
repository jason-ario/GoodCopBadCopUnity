using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Local manager for the systemic threat list.
/// Starts and ends the night phase, drives the minimum night duration timer,
/// manages the ShiftPerformanceEvaluator, and keeps TaskRegistry in sync.
///
/// Plain MonoBehaviour — no NetworkObject required. Each client manages its own
/// timer independently; the timer fires OnMinimumNightDurationElapsed locally
/// so all clients enable the shift-start button at approximately the same time.
/// </summary>
public class BetweenShiftTaskManager : MonoBehaviour
{
    public static BetweenShiftTaskManager Instance;

    /// <summary>
    /// Fired locally when the minimum night duration has elapsed.
    /// ShiftManager subscribes to this to trigger the shift-start button.
    /// </summary>
    public static event Action OnMinimumNightDurationElapsed;

    /// <summary>Read-only view of all registered threats.</summary>
    public ISystemicThreat[] Threats => _threats;

    // ── Inspector ────────────────────────────────────────────────────────────

    /// <summary>
    /// Assign all ISystemicThreat MonoBehaviours here via the Inspector.
    /// Each entry must implement ISystemicThreat.
    /// </summary>
    [SerializeField] private MonoBehaviour[] _threatBehaviours;

    [Tooltip("Evaluates threat samples at shift end and awards performance coupons.")]
    [SerializeField] private ShiftPerformanceEvaluator _performanceEvaluator;

    [Tooltip("Minimum seconds the player must spend in the night phase before the shift button lights up.")]
    [SerializeField] private float _minimumNightDuration = 180f;

    // ── State ────────────────────────────────────────────────────────────────

    private ISystemicThreat[] _threats;
    private Coroutine _nightTimerCoroutine;
    private bool _nightPhaseActive;
    private float _uiRefreshTimer;

    private const float UIRefreshInterval = 2f;

    private bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
        BuildThreatList();
    }

    private void Update()
    {
        _uiRefreshTimer += Time.deltaTime;
        if (_uiRefreshTimer < UIRefreshInterval) return;

        _uiRefreshTimer = 0f;
        TaskRegistry.Instance?.NotifyTaskStateChanged();
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    private void BuildThreatList()
    {
        _threats = new ISystemicThreat[_threatBehaviours.Length];

        for (int i = 0; i < _threatBehaviours.Length; i++)
        {
            _threats[i] = _threatBehaviours[i] as ISystemicThreat;

            if (_threats[i] == null)
                Debug.LogWarning($"[BetweenShiftTaskManager] Entry {i} ({_threatBehaviours[i]?.name}) does not implement ISystemicThreat.");
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the night phase: activates threats (server only), begins performance sampling
    /// (server only), registers tasks in the HUD task list (all clients), and starts the
    /// minimum duration timer.
    /// Called on all clients — server-only work is guarded internally.
    /// </summary>
    public void BeginNightPhase()
    {
        if (_nightPhaseActive) return;
        _nightPhaseActive = true;

        // Start the minimum night timer on all clients so the shift button lights up locally.
        if (_nightTimerCoroutine != null) StopCoroutine(_nightTimerCoroutine);
        _nightTimerCoroutine = StartCoroutine(NightDurationTimer());

        if (IsServer)
        {
            foreach (ISystemicThreat threat in _threats) threat?.BeginNightPhase();
            _performanceEvaluator?.BeginSampling(_threats);
        }

        TaskRegistry.Instance?.SetThreats(_threats);

        Debug.Log($"[BetweenShiftTaskManager] Night phase begun. {_threats.Length} threat(s) active.");
    }

    /// <summary>
    /// Ends the night phase: stops threats (server only), evaluates performance (server only),
    /// and stops the timer. SERVER ONLY for the scoring side; safe to call on all clients.
    /// </summary>
    public void EndNightPhase()
    {
        _nightPhaseActive = false;

        if (_nightTimerCoroutine != null)
        {
            StopCoroutine(_nightTimerCoroutine);
            _nightTimerCoroutine = null;
        }

        if (!IsServer) return;

        foreach (ISystemicThreat threat in _threats) threat?.EndNightPhase();
        _performanceEvaluator?.EvaluateAndAward();

        Debug.Log("[BetweenShiftTaskManager] Night phase ended. Performance evaluated.");
    }

    /// <summary>
    /// Alias for BeginNightPhase(). Kept for compatibility with the existing
    /// ShiftManager.TriggerAddShiftTasks() → AddShiftTasksOnServer() code path.
    /// </summary>
    public void ActivateTasks()
    {
        BeginNightPhase();
    }

    /// <summary>
    /// Fires OnMinimumNightDurationElapsed immediately, bypassing the timer.
    /// Debug helper — also used by ShiftManager.ForceCompleteAllTasksServerRpc.
    /// </summary>
    public void HandleNightPhaseReady()
    {
        OnMinimumNightDurationElapsed?.Invoke();
    }

    /// <summary>
    /// Returns the weighted average threat level across all registered threats (0–1).
    /// </summary>
    public float GetAggregateThreatLevel()
    {
        if (_threats == null || _threats.Length == 0) return 0f;

        float total       = 0f;
        float totalWeight = 0f;

        foreach (ISystemicThreat t in _threats)
        {
            if (t == null) continue;
            total       += t.ThreatLevel * t.ScoreWeight;
            totalWeight += t.ScoreWeight;
        }

        return totalWeight > 0f ? total / totalWeight : 0f;
    }

    /// <summary>Debug helper — immediately fires OnMinimumNightDurationElapsed.</summary>
    public void ForceCompleteAllTasks()
    {
        HandleNightPhaseReady();
    }

    // ── Backward-compatibility stubs ─────────────────────────────────────────

    /// <summary>Obsolete. No-op — the night phase no longer uses discrete task completion.</summary>
    [System.Obsolete("NotifyTaskComplete is obsolete. The night phase is gated by a minimum duration timer.")]
    public void NotifyTaskComplete(IBetweenShiftTask task)
    {
        Debug.LogWarning("[BetweenShiftTaskManager] NotifyTaskComplete is obsolete and has no effect.");
    }

    /// <summary>Obsolete. No-op — task physics reset is handled by individual threat scripts.</summary>
    [System.Obsolete("ResetTaskPhysics is obsolete. Threat state is managed by BeginNightPhase().")]
    public void ResetTaskPhysics()
    {
        Debug.LogWarning("[BetweenShiftTaskManager] ResetTaskPhysics is obsolete and has no effect.");
    }

    // ── Timer ─────────────────────────────────────────────────────────────────

    private IEnumerator NightDurationTimer()
    {
        yield return new WaitForSeconds(_minimumNightDuration);
        OnMinimumNightDurationElapsed?.Invoke();
    }
}
