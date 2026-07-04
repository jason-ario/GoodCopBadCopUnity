using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Master campaign orchestrator. Owns:
/// - Which day is active (server-authoritative via NetworkVariable)
/// - Activating / deactivating the correct per-day DayBase child GameObject
/// - Injecting the day's SuspectSet into DailySuspectManager
/// - Firing tutorial step events for MegaphoneDialogueManager to handle
/// - Surfacing the door-lock flag to ShiftManager at shift start
/// - Persisting CurrentDay to SaveDataManager
///
/// Per-day configuration (suspects, door lock, tutorial steps) lives directly on
/// each day's DayBase subclass component — no ScriptableObject required.
/// </summary>
public class CampaignManager : NetworkBehaviour
{
    public static CampaignManager Instance;

    private readonly NetworkVariable<int> _networkCurrentDay = new NetworkVariable<int>(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private int _currentDay = 1;

    /// <summary>The current 1-based day number.</summary>
    public int CurrentDay => _currentDay;

    /// <summary>The active day's DayBase component.</summary>
    public DayBase ActiveDay { get; private set; }

    /// <summary>
    /// True when the active day has <see cref="DayBase.LockDoorDuringShift"/> set.
    /// ShiftManager reads this at shift start to decide whether to fire OnDoorLock.
    /// </summary>
    public bool IsDoorLockedForShift => ActiveDay != null && ActiveDay.LockDoorDuringShift;

    /// <summary>
    /// Fired when CampaignManager needs a tutorial step to run.
    /// MegaphoneDialogueManager subscribes here.
    /// </summary>
    public static event Action<TutorialStep> OnTutorialStepRequested;

    /// <summary>Fired after a new day is fully applied on all clients.</summary>
    public static event Action<int> OnDayChanged;

    /// <summary>Fired when the final day has been completed.</summary>
    public static event Action OnCampaignComplete;

    private readonly Dictionary<int, DayBase> _days = new Dictionary<int, DayBase>();

    /// <summary>
    /// When >= 0, overrides the destination day number in the next <see cref="AdvanceDay"/> call.
    /// Consumed and reset to -1 after a single use.
    /// Set by DebugConsole (F10) to queue the test day without interrupting the current shift.
    /// </summary>
    public static int DebugNextDayOverride = -1;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        Instance = this;
        CollectDays();
    }

    private void OnEnable()
    {
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftEnd += OnShiftEnded;
    }

    private void OnDisable()
    {
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftEnd -= OnShiftEnded;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _networkCurrentDay.OnValueChanged += OnNetworkDayChanged;
    }

    public override void OnNetworkDespawn()
    {
        _networkCurrentDay.OnValueChanged -= OnNetworkDayChanged;
    }

    // -------------------------------------------------------------------------
    // Initialisation
    // -------------------------------------------------------------------------

    private void CollectDays()
    {
        _days.Clear();

        foreach (Transform child in transform)
        {
            DayBase day = child.GetComponent<DayBase>();
            if (day == null) continue;

            if (_days.ContainsKey(day.DayNumber))
            {
                Debug.LogWarning($"[CampaignManager] Duplicate DayNumber {day.DayNumber} on '{child.name}' — skipping.");
                continue;
            }

            _days[day.DayNumber] = day;
            child.gameObject.SetActive(false);
        }

        Debug.Log($"[CampaignManager] Collected {_days.Count} day(s).");
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Entry point called by GameManager when the game starts.
    /// Reads CurrentDay from the active save slot and applies the correct day.
    /// </summary>
    public void StartCampaign()
    {
        int savedDay = SaveDataManager.Instance.CurrentDay;
        _currentDay = Mathf.Max(1, savedDay);

        if (IsServer)
            _networkCurrentDay.Value = _currentDay;

        ApplyDay(_currentDay);

        Debug.Log($"[CampaignManager] Campaign started on Day {_currentDay}.");
    }

    /// <summary>
    /// Jumps directly to the specified day number, bypassing normal progression.
    /// Server-only; propagates to clients via NetworkVariable.
    /// Intended for debug use only — sets the save slot's current day and immediately applies the target day.
    /// </summary>
    public void JumpToDay(int targetDay)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[CampaignManager] JumpToDay must only be called on the server.");
            return;
        }

        if (!_days.ContainsKey(targetDay))
        {
            Debug.LogWarning($"[CampaignManager] JumpToDay: no DayBase found for Day {targetDay}.");
            return;
        }

        SaveDataManager.Instance.CurrentDay = targetDay;
        _networkCurrentDay.Value = targetDay;
        ApplyDay(targetDay);

        Debug.Log($"[CampaignManager] DEBUG — jumped to Day {targetDay}.");
    }

    /// <summary>
    /// Advances to the next campaign day. Server-only; propagates to clients via NetworkVariable.
    /// Call this after all shift tasks are complete or when transitioning between days.
    /// </summary>
    public void AdvanceDay()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[CampaignManager] AdvanceDay must only be called on the server.");
            return;
        }

        Debug.Log($"[CampaignManager] AdvanceDay — _currentDay={_currentDay}, will advance to {_currentDay + 1}.\n{System.Environment.StackTrace}");

        int nextDay = _currentDay + 1;

        if (DebugNextDayOverride >= 0)
        {
            nextDay = DebugNextDayOverride;
            DebugNextDayOverride = -1;
            Debug.Log($"[CampaignManager] DEBUG — AdvanceDay redirected to Day {nextDay} via DebugNextDayOverride.");
        }

        if (!_days.ContainsKey(nextDay))
        {
            Debug.Log("[CampaignManager] Campaign complete — no further days configured.");
            ActiveDay?.DayCompleted();
            OnCampaignComplete?.Invoke();
            return;
        }

        SaveDataManager.Instance.CurrentDay = nextDay;
        _networkCurrentDay.Value = nextDay;

        // Advance every suspect's infection score before the new shift is populated.
        if (SuspectRunRecords.Instance != null)
            SuspectRunRecords.Instance.AdvanceDayInfection();

        // Server applies immediately; clients apply via OnNetworkDayChanged.
        ApplyDay(nextDay);
    }

    // -------------------------------------------------------------------------
    // Event Handlers
    // -------------------------------------------------------------------------

    private void OnShiftEnded()
    {
        SaveDataManager.Instance.CurrentDay = _currentDay;
        ActiveDay?.ShiftEnded();
        Debug.Log($"[CampaignManager] Shift ended — Day {_currentDay} saved.");
    }

    private void OnNetworkDayChanged(int oldDay, int newDay)
    {
        _currentDay = newDay;

        // Server already applied the day in AdvanceDay; clients apply here.
        if (!IsServer)
            ApplyDay(newDay);
    }

    // -------------------------------------------------------------------------
    // Internal
    // -------------------------------------------------------------------------

    private void ApplyDay(int day)
    {
        _currentDay = day;

        // Deactivate the previous day.
        if (ActiveDay != null)
        {
            ActiveDay.DayDeactivated();
            ActiveDay.gameObject.SetActive(false);
        }

        if (!_days.TryGetValue(day, out DayBase dayBase))
        {
            ActiveDay = null;
            Debug.LogWarning($"[CampaignManager] No DayBase found for Day {day}.");
            return;
        }

        // Activate the new day.
        dayBase.gameObject.SetActive(true);
        ActiveDay = dayBase;

        // Push day number to ShiftManager.
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.SetCurrentDay(day);

        // Inject this day's suspect pool.
        if (DailySuspectManager.Instance != null && dayBase.SuspectSet != null)
            DailySuspectManager.Instance.SetSuspectSet(dayBase.SuspectSet);

        // Fire tutorial steps configured on the day.
        FireTutorialSteps(dayBase.TutorialStepsToFire);

        // Notify the day itself, then any external listeners.
        dayBase.DayActivated();
        OnDayChanged?.Invoke(day);

        // Lock the door before the shift starts on days 2+ so pre-shift content can play
        // while the room is secured. The door unlocks when the switch button starts the shift.
        if (IsServer && day > 1 && ShiftManager.Instance != null)
            ShiftManager.Instance.OnDoorLock?.Invoke();

        Debug.Log($"[CampaignManager] Day {day} applied.");
    }

    private void FireTutorialSteps(List<TutorialStep> steps)
    {
        if (steps == null || steps.Count == 0) return;

        foreach (TutorialStep step in steps)
        {
            if (step == TutorialStep.None) continue;
            OnTutorialStepRequested?.Invoke(step);
            Debug.Log($"[CampaignManager] Tutorial step requested: {step}");
        }
    }
}
