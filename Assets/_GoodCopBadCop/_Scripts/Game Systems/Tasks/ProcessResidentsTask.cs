using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Always-on daily HUD task that tracks suspect/resident processing progress for the current
/// shift. Displayed as "Process residents: X/Y" via <see cref="TaskRegistry"/>.
///
/// This component does not itself gate clock-out — <see cref="ShiftManager"/> already requires
/// every suspect to be processed (via <see cref="ShiftManager.SetNextSuspectReady"/>) before
/// enabling the timecard machine. This class exists purely to surface that same requirement in
/// the HUD task list so players can see it alongside the other daily tasks (trash, graffiti, etc.).
///
/// Scene setup:
///   - NetworkObject on this GameObject.
///   - Place under ---Task Manager alongside CleanGraffitiTask / TakeOutTrashTask.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class ProcessResidentsTask : NetworkBehaviour, ISystemicThreat
{
    public static ProcessResidentsTask Instance { get; private set; }

    /// <summary>
    /// Debug-only escape hatch. Set true right before <see cref="ShiftManager.TryStartShift"/>
    /// to suppress this task's next <see cref="OnShiftStart"/> initialization — used by debug
    /// skips (e.g. <see cref="Day_01.DebugSkipToMutantBreach"/>) that start the shift with no
    /// suspects ever going to be processed, where tracking "Process N residents" would just
    /// stay stuck at 0/N forever. Automatically consumed (reset to false) the next time
    /// <see cref="OnShiftStart"/> fires, so it only ever suppresses a single shift.
    /// </summary>
    public static bool SuppressNextShiftStart = false;

    [Header("Task Properties")]
    [SerializeField] private string _taskName = "Process Residents";

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<int> _processedCount = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _totalCount = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Whether this task is currently active and should appear in the HUD task list.
    /// Drives TaskRegistry registration on all clients, including late joiners.
    /// </summary>
    private readonly NetworkVariable<bool> _isActive = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public string ThreatName  => _taskName;
    public float  ScoreWeight => 0f;

    public float ThreatLevel => _totalCount.Value > 0
        ? 1f - Mathf.Clamp01((float)_processedCount.Value / _totalCount.Value)
        : 0f;

    /// <summary>Shown in the HUD as "Process residents X/Y".</summary>
    public string ThreatDescription =>
        _totalCount.Value > 0
            ? $"Process residents {Mathf.Min(_processedCount.Value, _totalCount.Value)}/{_totalCount.Value}"
            : string.Empty;

    /// <summary>No-op — this task is driven by the day/suspect cycle, not the night phase.</summary>
    public void BeginNightPhase() { }

    /// <summary>No-op.</summary>
    public void EndNightPhase() { }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[ProcessResidentsTask] Duplicate instance detected — destroying self.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _processedCount.OnValueChanged += OnNetworkValueChanged;
        _totalCount.OnValueChanged     += OnNetworkValueChanged;
        _isActive.OnValueChanged       += OnIsActiveChanged;

        // Handle the initial value for late-joining clients.
        if (_isActive.Value)
            TaskRegistry.Instance?.AddThreat(this);

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftStart += OnShiftStart;

        ShiftManager.OnSuspectProcessed += OnSuspectProcessed;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _processedCount.OnValueChanged -= OnNetworkValueChanged;
        _totalCount.OnValueChanged     -= OnNetworkValueChanged;
        _isActive.OnValueChanged       -= OnIsActiveChanged;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftStart -= OnShiftStart;

        ShiftManager.OnSuspectProcessed -= OnSuspectProcessed;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ShiftManager.OnSuspectProcessed -= OnSuspectProcessed;
    }

    private void OnNetworkValueChanged<T>(T previous, T current)
    {
        TaskRegistry.Instance?.NotifyTaskStateChanged();
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

    // ── Server-side progress tracking ───────────────────────────────────────

    /// <summary>
    /// Called on the server when the shift starts and the suspect lineup for the day is about
    /// to be populated by <see cref="DailySuspectManager"/>. Waits one frame so the lineup is
    /// guaranteed to be populated before reading its count, then resets progress and registers
    /// the task in the HUD.
    /// </summary>
    private void OnShiftStart()
    {
        if (!IsServer) return;

        if (SuppressNextShiftStart)
        {
            SuppressNextShiftStart = false;
            _processedCount.Value = 0;
            _totalCount.Value = 0;
            _isActive.Value = false;
            Debug.Log("[ProcessResidentsTask] Shift started — suppressed via SuppressNextShiftStart (debug skip).");
            return;
        }

        StartCoroutine(InitializeAfterLineupPopulated());
    }

    private IEnumerator InitializeAfterLineupPopulated()
    {
        yield return null;

        DayBase activeDay = CampaignManager.Instance != null ? CampaignManager.Instance.ActiveDay : null;
        // Day 1's SubjectsToProcessOverrideForDisplay is the sole exception to the shared total
        // below — its lineup is hand-scripted and includes slots the player never actually
        // processes. Every other day reads DailySuspectManager.TotalSuspectsThisShift, the single
        // source of truth also used by DayBase's objective counter — it deliberately EXCLUDES
        // mutant intruder slots, which are a random combat threat and never a "suspect to
        // process" (ShiftManager's end-of-shift check uses a separate, mutant-inclusive count).
        int total = activeDay != null && activeDay.SubjectsToProcessOverrideForDisplay >= 0
            ? activeDay.SubjectsToProcessOverrideForDisplay
            : (DailySuspectManager.Instance != null ? DailySuspectManager.Instance.TotalSuspectsThisShift : 0);
        _processedCount.Value = 0;
        _totalCount.Value = total;
        _isActive.Value = total > 0;

        Debug.Log($"[ProcessResidentsTask] Shift started — tracking {total} resident(s) for the day.");
    }

    /// <summary>
    /// Called on the server whenever <see cref="ShiftManager.OnSuspectProcessed"/> fires
    /// (a suspect was passed, killed, or quarantined). Advances progress and hides the task
    /// once every resident has been processed.
    /// </summary>
    private void OnSuspectProcessed()
    {
        if (!IsServer) return;

        int next = _processedCount.Value + 1;
        _processedCount.Value = _totalCount.Value > 0 ? Mathf.Min(next, _totalCount.Value) : next;

        if (_totalCount.Value > 0 && _processedCount.Value >= _totalCount.Value)
            _isActive.Value = false;
    }
}
