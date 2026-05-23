using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Master campaign orchestrator. Sits above ShiftManager and owns:
/// - Which day is active and which DayEntry config to apply
/// - Activating/deactivating the correct per-day scene context (DayContext children)
/// - Injecting the day's SuspectSet into DailySuspectManager
/// - Firing tutorial step events for the new tutorial system to handle
/// - Persisting CurrentDay to SaveDataManager
///
/// Day advancement is server-authoritative via NetworkVariable.
/// </summary>
public class CampaignManager : NetworkBehaviour
{
    public static CampaignManager Instance;

    [SerializeField] private CampaignData _campaignData;

    private readonly NetworkVariable<int> _networkCurrentDay = new NetworkVariable<int>(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private int _currentDay = 1;

    /// <summary>The current 1-based day number.</summary>
    public int CurrentDay => _currentDay;

    /// <summary>The DayEntry config resolved for the current day.</summary>
    public DayEntry CurrentDayEntry { get; private set; }

    /// <summary>
    /// Fired when CampaignManager needs a tutorial step to run.
    /// Subscribe here from your tutorial system to handle each step.
    /// </summary>
    public static event Action<TutorialStep> OnTutorialStepRequested;

    /// <summary>Fired after AdvanceDay completes on all clients.</summary>
    public static event Action<int> OnDayChanged;

    /// <summary>Fired when the campaign's final day has been completed.</summary>
    public static event Action OnCampaignComplete;

    // Per-day child GameObjects, keyed by DayNumber.
    private readonly Dictionary<int, DayContext> _dayContexts = new Dictionary<int, DayContext>();
    private DayContext _activeDayContext;

    // ---------------------------------------------------------------------------
    // Unity Lifecycle
    // ---------------------------------------------------------------------------

    private void Awake()
    {
        Instance = this;
        CollectDayContexts();
    }

    private void OnEnable()
    {
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

    // ---------------------------------------------------------------------------
    // Initialisation
    // ---------------------------------------------------------------------------

    private void CollectDayContexts()
    {
        _dayContexts.Clear();

        foreach (Transform child in transform)
        {
            DayContext ctx = child.GetComponent<DayContext>();
            if (ctx == null) continue;

            if (_dayContexts.ContainsKey(ctx.DayNumber))
            {
                Debug.LogWarning($"[CampaignManager] Duplicate DayNumber {ctx.DayNumber} on '{child.name}' — skipping.");
                continue;
            }

            _dayContexts[ctx.DayNumber] = ctx;

            // Ensure all day children start inactive.
            child.gameObject.SetActive(false);
        }

        Debug.Log($"[CampaignManager] Collected {_dayContexts.Count} DayContext(s).");
    }

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Entry point called by GameManager when the game starts.
    /// Reads CurrentDay from the active save slot and loads the correct day config.
    /// </summary>
    public void StartCampaign()
    {
        if (_campaignData == null)
        {
            Debug.LogError("[CampaignManager] CampaignData is not assigned. Campaign cannot start.");
            return;
        }

        int savedDay = SaveDataManager.Instance.CurrentDay;
        _currentDay = Mathf.Max(1, savedDay);

        if (IsServer)
            _networkCurrentDay.Value = _currentDay;

        ApplyDay(_currentDay);

        Debug.Log($"[CampaignManager] Campaign started on Day {_currentDay}.");
    }

    /// <summary>
    /// Called when a shift ends. Persists the completed day and deactivates the current DayContext.
    /// Day advancement happens separately via AdvanceDay (called when the next shift begins).
    /// </summary>
    public void OnShiftEnded()
    {
        SaveDataManager.Instance.CurrentDay = _currentDay;
        _activeDayContext?.OnDayDeactivated();
        Debug.Log($"[CampaignManager] Shift ended — Day {_currentDay} saved.");
    }

    /// <summary>
    /// Advances to the next campaign day. Server-only; syncs to all clients via NetworkVariable.
    /// Call this when the player is ready to begin the next shift (e.g. after night-phase tasks).
    /// </summary>
    public void AdvanceDay()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[CampaignManager] AdvanceDay must only be called on the server.");
            return;
        }

        int nextDay = _currentDay + 1;

        if (nextDay > _campaignData.TotalDays)
        {
            Debug.Log("[CampaignManager] Campaign complete — all days finished.");
            OnCampaignComplete?.Invoke();
            return;
        }

        _networkCurrentDay.Value = nextDay;
        // Clients apply the day when OnNetworkDayChanged fires.
        // Apply on server immediately.
        ApplyDay(nextDay);
    }

    // ---------------------------------------------------------------------------
    // Internal
    // ---------------------------------------------------------------------------

    private void OnNetworkDayChanged(int oldDay, int newDay)
    {
        _currentDay = newDay;

        // Server already applied the day in AdvanceDay; clients apply here.
        if (!IsServer)
            ApplyDay(newDay);
    }

    private void ApplyDay(int day)
    {
        _currentDay = day;
        CurrentDayEntry = _campaignData.GetDayEntry(day);

        // Push day number to ShiftManager so its date helpers stay in sync.
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.SetCurrentDay(day);

        // Inject this day's suspect pool.
        if (DailySuspectManager.Instance != null && CurrentDayEntry.suspectSet != null)
            DailySuspectManager.Instance.SetSuspectSet(CurrentDayEntry.suspectSet);

        // Swap DayContext children.
        _activeDayContext?.OnDayDeactivated();
        _activeDayContext?.gameObject.SetActive(false);

        if (_dayContexts.TryGetValue(day, out DayContext ctx))
        {
            ctx.gameObject.SetActive(true);
            ctx.OnDayActivated();
            _activeDayContext = ctx;
        }
        else
        {
            _activeDayContext = null;
            Debug.LogWarning($"[CampaignManager] No DayContext found for Day {day}.");
        }

        // Fire tutorial steps for this day.
        FireTutorialSteps(CurrentDayEntry.tutorialStepsToFire);

        OnDayChanged?.Invoke(day);

        Debug.Log($"[CampaignManager] Day {day} applied — '{CurrentDayEntry.dayLabel}'.");
    }

    /// <summary>
    /// Fires each tutorial step as an event. The tutorial system subscribes to
    /// OnTutorialStepRequested to handle the actual presentation logic.
    /// </summary>
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
