using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using GoodCopBadCop.Population;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Playables;

public class ShiftManager : NetworkBehaviour
{
    public static ShiftManager Instance;

    /// <summary>Fired on the server whenever a suspect is killed.</summary>
    public static event System.Action OnSuspectKilled;

    [VContainer.Inject] private IPopulationModel populationModel;

    [Header("Network Variables")]
    public NetworkVariable<bool> shiftStarted = new NetworkVariable<bool>(false);
    private NetworkVariable<int> _networkCurrentDay = new NetworkVariable<int>(1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    [Header("Set Up")]
    private int _currentDay = 1;
    public int CurrentDay => _currentDay;
    private readonly DateTime _startDate = new DateTime(1989, 10, 20);
    public DateTime CurrentGameDate => _startDate.AddDays(_currentDay - 1);
    
    [SerializeField] private StartShiftScreen _startShiftScreen;
    [SerializeField] private AudioSource bellSound;
    [SerializeField] private AudioClip endOfLevelSound;
    [SerializeField] private AudioClip knockOnDoorSound;
    [SerializeField] private GameObject cardboardBox;
    [SerializeField] private MachineShake doorShake;
    [SerializeField] private PlayableDirector introCutscene;
    [SerializeField] private AudioSource ambientAudio;
    [SerializeField] private AudioSource buzzerSound;

    private PlayableDirector ActiveIntroCutscene => 
        (CampaignManager.Instance != null && CampaignManager.Instance.ActiveDay != null && CampaignManager.Instance.ActiveDay.IntroCutscene != null) 
        ? CampaignManager.Instance.ActiveDay.IntroCutscene 
        : introCutscene;

    private PlayableDirector _playingDirector;

    public int suspectsProcessed = 0;
    public int suspectsPassedCorrect = 0;
    public int suspectsPassedWrong = 0;
    public int suspectsQuarantined = 0;
    public int suspectsKilledCorrect = 0;
    public int suspectsKilledWrong = 0;

    [Header("End of Shift Rewards")]
    [Tooltip("Coupons earned for each citizen correctly passed (non-infected).")]
    [SerializeField] private int rewardPerCorrectPass = 10;
    [Tooltip("Coupons deducted for each infected citizen incorrectly passed (Non-Effected).")]
    [SerializeField] private int penaltyPerWrongPass = 15;
    [Tooltip("Coupons earned for correctly eliminating an infected suspect. (Unused — kills are now always penalised.)")]
    [SerializeField] private int rewardPerCorrectKill = 10;
    [Tooltip("Coupons deducted for incorrectly eliminating a non-infected citizen. (Unused — replaced by penaltyPerKill.)")]
    [SerializeField] private int penaltyPerWrongKill = 20;
    [Tooltip("Coupons earned for each citizen successfully quarantined.")]
    [SerializeField] private int rewardPerQuarantine = 8;
    [Tooltip("Coupons deducted per kill, regardless of whether the target was infected. Killing is always penalised.")]
    [SerializeField] private int penaltyPerKill = 12;

    private int _taskCompletedCount = 0;
    private bool _campaignAdvancedForCurrentReport;

    [Header("Environment Set Up")]
    [SerializeField] private SwitchButton _switchButton;
    [SerializeField] private WindowLampController windowLampController;
    [SerializeField] private DoorController _doorController;
    [SerializeField] private BunkerDoorController _bunkerDoorController;
    [SerializeField] private Lever lever;
    [SerializeField] private TimecardMachine _timecardMachine;

    [Header("Suspect Scheduling")]
    [Tooltip("Min and max seconds before the very first suspect arrives after a shift starts.")]
    [SerializeField] private Vector2 firstSuspectArrivalInterval = new Vector2(5f, 10f);
    [Tooltip("Min and max seconds to wait between subsequent suspects during a shift.")]
    [SerializeField] private Vector2 suspectArrivalInterval = new Vector2(30f, 90f);

    /// <summary>
    /// When set, overrides <see cref="firstSuspectArrivalInterval"/> for the current shift only.
    /// Consumed and reset to null automatically after the first suspect is scheduled.
    /// Set by day-specific classes (e.g. Day_01) before the shift starts.
    /// </summary>
    public static Vector2? OverrideFirstArrivalInterval = null;

    /// <summary>
    /// When set, overrides <see cref="suspectArrivalInterval"/> for every subsequent suspect
    /// arrival for the duration of the current shift.
    /// Reset to null automatically when the shift ends.
    /// Set by day-specific classes (e.g. Day_01) to compress suspect pacing during tutorials.
    /// </summary>
    public static Vector2? OverrideSuspectArrivalInterval = null;

    /// <summary>
    /// When true, <see cref="SetNextSuspectReady"/> queues the next suspect instead of scheduling
    /// it immediately. Call <see cref="ResumeScheduledSuspect"/> on the server to release the
    /// held suspect. Set by day-specific classes (e.g. Day_01) for tutorial gates.
    /// </summary>
    public static bool PauseSuspectScheduling = false;

    /// <summary>True when a suspect arrival is queued and waiting for <see cref="PauseSuspectScheduling"/> to clear.</summary>
    private bool _pendingNextSuspect = false;

    /// <summary>
    /// Fired on the server when the next suspect is ready to be summoned by ringing the table bell.
    /// Replaces the automatic arrival timer — the player must ring the bell to call the next suspect.
    /// </summary>
    public static event Action OnNextSuspectReadyForBell;

    /// <summary>
    /// True on the server when a suspect is queued and waiting for the table bell to be rung.
    /// Cleared by <see cref="SuspectController.NextSuspect"/> when the suspect is actually spawned,
    /// and by <see cref="EndShift"/> when the shift ends.
    /// </summary>
    public static bool NextSuspectReadyForBell = false;

    private Coroutine _suspectSchedulerCoroutine;

    #region Events & Date Helpers
    public Action OnShiftStart { get; set; }
    public Action OnShiftEnd { get; set; }
    public Action OnShiftReady { get; set; }

    /// <summary>
    /// Fired on the server immediately after the last suspect for the current shift is
    /// processed and before clock-out is enabled.
    /// Subscribe in day-specific classes (e.g. Day_03) to trigger end-of-shift events
    /// that should occur right after the final visitor walks away.
    /// </summary>
    public static event Action OnLastSuspectProcessed;
    /// <summary>
    /// Fired once per workday when the player enters the booth and the day officially starts
    /// (after the intro cutscene or between-shift transition). Use this for day-start effects
    /// such as the fax machine newspaper spawn. Not fired again when the shift button is pressed.
    /// </summary>
    public Action OnDayStart { get; set; }
    /// <summary>Fired when the booth door should force-close and lock. Subscribe in DoorController.</summary>
    public Action OnDoorLock { get; set; }
    /// <summary>Fired after the end-of-shift dialogue finishes, signalling the door should unlock.</summary>
    public Action OnDoorUnlock { get; set; }
    /// <summary>
    /// Fired on all clients when the shift ends and the night phase begins — door unlocked,
    /// player is free to roam, and tasks (fax, etc.) become available.
    /// </summary>
    public Action OnNightPhaseBegin { get; set; }

    private DateTime CurrentGameDateTime => _startDate.AddDays(_currentDay - 1);
    public string currentMonth => CurrentGameDateTime.ToString("MMMM");
    public string currentDay => CurrentGameDateTime.ToString("dd");
    public string currentYear => CurrentGameDateTime.ToString("yyyy");
    public bool IsEarlyDays => CurrentDay < 11;
    public bool IsMidDays => CurrentDay is >= 11 and < 21;
    public bool IsEndDays => CurrentDay >= 21;

    /// <summary>
    /// Sets the current day directly. Called by CampaignManager on startup and day advance
    /// so ShiftManager always reflects the save-data day number.
    /// </summary>
    public void SetCurrentDay(int day)
    {
        _currentDay = day;
        if (IsServer)
            _networkCurrentDay.Value = day;
        Debug.Log($"[ShiftManager] Day set to {_currentDay} ({_startDate.AddDays(_currentDay - 1):dd MMMM yyyy})");
    }
    #endregion

    private void Awake()
    {
        Instance = this;
        InitializeDateSystem();
    }

    private void OnEnable()
    {
    }

    private void OnDisable()
    {
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _networkCurrentDay.OnValueChanged += OnCurrentDayChanged;

        if (IsServer && DebugConsole.Instance != null && DebugConsole.Instance.skipToBoothReady)
            SkipToBoothReady();

        if (IsServer && DebugConsole.Instance != null && DebugConsole.Instance.skipToDay1Booth)
            SkipToBoothReadyOnDay(1);

        if (IsServer && DebugConsole.Instance != null && DebugConsole.Instance.skipToAfterShift)
            StartCoroutine(DebugSkipToAfterShift());
    }

    public override void OnNetworkDespawn()
    {
        _networkCurrentDay.OnValueChanged -= OnCurrentDayChanged;
    }

    private void OnCurrentDayChanged(int oldValue, int newValue)
    {
        _currentDay = newValue;
    }

    private void InitializeDateSystem()
    {
        _currentDay = 1;
        Debug.Log($"[ShiftManager] Date system initialised. CampaignManager will push the correct day on StartCampaign.");
    }

    /// <summary>
    /// Called by SuspectController after a suspect is resolved. Schedules the next suspect
    /// to arrive after a random interval, or signals the player to clock out if all suspects
    /// have been processed.
    /// Must only be called on the server.
    /// </summary>
    public void SetNextSuspectReady()
    {
        if (!IsServer) return;

        // Only signal clock-out when the lineup is exhausted AND no scripted intercept is
        // waiting. An armed intercept (e.g. the Day 1 Soldier) must fire even if the
        // random-suspect list has fewer slots than the intercept's index.
        bool interceptPending = SuspectController.InterceptNextSuspectSpawn != null;
        if (!interceptPending &&
            SuspectController.Instance.SuspectIndex >= DailySuspectManager.Instance.shiftSuspects.Count - 1)
        {
            OnLastSuspectProcessed?.Invoke();

            if (_timecardMachine != null)
                _timecardMachine.EnableClockOut();

            NotifyClockOutReadyClientRpc();
            return;
        }

        if (PauseSuspectScheduling)
        {
            _pendingNextSuspect = true;
            Debug.Log("[ShiftManager] SetNextSuspectReady: scheduling paused — next suspect queued.");
            return;
        }

        NextSuspectReadyForBell = true;
        OnNextSuspectReadyForBell?.Invoke();
    }

    /// <summary>
    /// Releases a suspect arrival that was held by <see cref="PauseSuspectScheduling"/>.
    /// Clears the pause flag and immediately starts the arrival scheduler if a suspect was queued.
    /// Must only be called on the server.
    /// </summary>
    public void ResumeScheduledSuspect()
    {
        if (!IsServer) return;
        PauseSuspectScheduling = false;
        if (!_pendingNextSuspect) return;

        _pendingNextSuspect = false;
        Debug.Log("[ShiftManager] ResumeScheduledSuspect: releasing held suspect arrival.");
        if (_suspectSchedulerCoroutine != null)
            StopCoroutine(_suspectSchedulerCoroutine);
        _suspectSchedulerCoroutine = StartCoroutine(ScheduledSuspectArrival());
    }

    [ClientRpc]
    private void NotifyClockOutReadyClientRpc()
    {
        MegaphoneDialogueManager.Instance.SayClockOutReady();
    }

    /// <summary>
    /// Waits a random interval then triggers the next suspect to approach the booth.
    /// Runs on the server only. Uses <paramref name="interval"/> if provided, then
    /// <see cref="OverrideSuspectArrivalInterval"/> if set, otherwise falls back to <see cref="suspectArrivalInterval"/>.
    /// </summary>
    private IEnumerator ScheduledSuspectArrival(Vector2? interval = null)
    {
        Vector2 range = interval ?? OverrideSuspectArrivalInterval ?? suspectArrivalInterval;
        float delay = UnityEngine.Random.Range(range.x, range.y);
        yield return new WaitForSeconds(delay);
        SuspectController.Instance.NextSuspect();
    }

    public void GiveBonusBox()
    {
        StartCoroutine(BonusBoxSequence());
    }

    private IEnumerator BonusBoxSequence()
    {
        yield return new WaitForSeconds(1f);
        SFXController.Instance.Play(knockOnDoorSound);
        doorShake.enabled = true;
        yield return new WaitForSeconds(1.5f);
        doorShake.enabled = false;
        cardboardBox.SetActive(true);
    }

    public void TryStartShift()
    {
        if (IsServer)
            StartShiftServer(NetworkManager.Singleton.LocalClientId);
        else
            RequestStartShiftServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStartShiftServerRpc(ServerRpcParams rpcParams = default)
    {
        StartShiftServer(rpcParams.Receive.SenderClientId);
    }

    private void StartShiftServer(ulong requestingClientId)
    {
        if (!IsServer) return;
        if (shiftStarted.Value) return;

        shiftStarted.Value = true;
        StartShiftClientRpc();
    }

    [ClientRpc]
    private void StartShiftClientRpc()
    {
        StartCoroutine(OpenWindowSequence());
    }

    public void OpenWindow()
    {
        StartCoroutine(OpenWindowSequence());
    }

    public void PlayBuzzerSound()
    {
        buzzerSound.Play();
    }

    /// <summary>
    /// Plays the buzzer sound on every client. Must be called on the server.
    /// </summary>
    public void PlayBuzzerSoundNetworked()
    {
        if (!IsServer) return;
        PlayBuzzerSoundClientRpc();
    }

    [ClientRpc]
    private void PlayBuzzerSoundClientRpc()
    {
        buzzerSound.Play();
    }

    private IEnumerator OpenWindowSequence()
    {
        ResetSuspectsProcessed();
        SuspectController.Instance.ResetSuspects();

        PlayBuzzerSound();
        windowLampController.TurnGreen();


        yield return new WaitForSeconds(3f);

        OnShiftStart?.Invoke();
        yield return new WaitForSeconds(0.5f);

        yield return new WaitForSeconds(3f);

        // Notify the table bell that the first suspect is ready to be called.
        if (IsServer)
        {
            if (_suspectSchedulerCoroutine != null)
                StopCoroutine(_suspectSchedulerCoroutine);

            OverrideFirstArrivalInterval = null;
            NextSuspectReadyForBell = true;
            OnNextSuspectReadyForBell?.Invoke();
        }
    }

    public void EndShift()
    {
        if (!IsServer) return;

        // Stop any pending suspect arrival.
        if (_suspectSchedulerCoroutine != null)
        {
            StopCoroutine(_suspectSchedulerCoroutine);
            _suspectSchedulerCoroutine = null;
        }

        // Clear per-shift arrival overrides so they don't bleed into the next shift.
        OverrideFirstArrivalInterval    = null;
        OverrideSuspectArrivalInterval  = null;
        PauseSuspectScheduling          = false;
        _pendingNextSuspect             = false;
        NextSuspectReadyForBell         = false;

        // Reset the shift flag so the bed interaction becomes available.
        shiftStarted.Value = false;

        // Open the booth door so the player can leave.
        _doorController?.ForceOpen();

        // Notify all clients: shift is over, booth door is unlocked.
        SignalShiftEndClientRpc();
    }

    /// <summary>
    /// Runs on all clients when the shift ends. Fires <see cref="OnShiftEnd"/> and
    /// <see cref="OnDoorUnlock"/> so the bed becomes usable and the booth door opens.
    /// Night phase has been removed — the player walks to bed to end the day.
    /// </summary>
    [ClientRpc]
    private void SignalShiftEndClientRpc()
    {
        OnShiftEnd?.Invoke();
        OnDoorUnlock?.Invoke();
    }

    /// <summary>Records a passed suspect and updates the correct/wrong tally.</summary>
    public void PassedSuspect(SuspectCharacter suspectCharacter)
    {
        suspectsProcessed += 1;

        if (suspectCharacter.IsInfected)
            suspectsPassedWrong += 1;
        else
            suspectsPassedCorrect += 1;
    }

    /// <summary>Records a killed suspect and updates the correct/wrong tally.</summary>
    public void KillSuspect(SuspectCharacter suspectCharacter)
    {
        suspectsProcessed += 1;

        if (suspectCharacter.IsInfected)
            suspectsKilledCorrect += 1;
        else
            suspectsKilledWrong += 1;

        OnSuspectKilled?.Invoke();
    }

    /// <summary>Records a quarantined suspect and updates the correct/wrong tally.</summary>
    public void QuarantinedSuspect(SuspectCharacter suspectCharacter)
    {
        suspectsProcessed += 1;
        suspectsQuarantined += 1;
    }

    public void StartNewShift()
    {
        ResetEverything();
        if (IsServer)
        {
            StartNewShiftClientRpc();
        }
        StartCoroutine(NewShiftSequence());
    }

    /// <summary>
    /// Builds the <see cref="EndOfShiftReportUI.ReportRowData"/> list for the current shift
    /// using all tracked suspect stats and the configured reward / penalty values.
    /// Call this before <see cref="StartInBetweenShiftSequence"/> resets the counters.
    /// </summary>
    public List<EndOfShiftReportUI.ReportRowData> BuildEndOfShiftReport()
    {
        var reportData = new List<EndOfShiftReportUI.ReportRowData>
        {
            new EndOfShiftReportUI.ReportRowData(
                $"Citizens Processed: {suspectsProcessed}", 0, false, isHeader: true),

            new EndOfShiftReportUI.ReportRowData(
                $"Correctly Passed: {suspectsPassedCorrect}",
                suspectsPassedCorrect * rewardPerCorrectPass),

            new EndOfShiftReportUI.ReportRowData(
                $"Incorrectly Passed: {suspectsPassedWrong}",
                suspectsPassedWrong * penaltyPerWrongPass, isPenalty: true),

            new EndOfShiftReportUI.ReportRowData(
                $"Quarantined: {suspectsQuarantined}", 0),

            new EndOfShiftReportUI.ReportRowData(
                $"Correctly Eliminated: {suspectsKilledCorrect}",
                suspectsKilledCorrect * rewardPerCorrectKill),

            new EndOfShiftReportUI.ReportRowData(
                $"Wrongly Eliminated: {suspectsKilledWrong}",
                suspectsKilledWrong * penaltyPerWrongKill, isPenalty: true),
        };

        AppendPopulationRows(reportData,
            populationModel.PopulationAlive.CurrentValue,
            populationModel.DeadOvernight.CurrentValue);

        return reportData;
    }

    /// <summary>
    /// Called when the player presses Continue on the end-of-shift report.
    /// Fades the screen, resets all shift state, advances to the next campaign day,
    /// and places the player back at their outside spawn so they walk into the booth
    /// to start the next shift.
    /// </summary>
    public void StartInBetweenShiftSequence()
    {
        Debug.Log($"[ShiftManager] StartInBetweenShiftSequence called.\n{System.Environment.StackTrace}");
        StartCoroutine(InBetweenShiftSequence());
    }

    /// <summary>
    /// Registers tasks in TaskRegistry and notifies all clients.
    /// Called mid-shift to activate tasks in the HUD task list.
    /// </summary>
    public void TriggerAddShiftTasks()
    {
        if (IsServer)
            AddShiftTasksOnServer();
        else
            AddShiftTasksServerRpc();
    }

    private void AddShiftTasksOnServer()
    {
        _taskCompletedCount = 0;

        if (BetweenShiftTaskManager.Instance != null)
            BetweenShiftTaskManager.Instance.ActivateTasks();

        AddShiftTasksClientRpc();
    }

    [ClientRpc]
    private void AddShiftTasksClientRpc()
    {
        if (IsServer) return; // Host already ran ActivateTasks above.
        if (BetweenShiftTaskManager.Instance != null)
            BetweenShiftTaskManager.Instance.ActivateTasks();
    }

    [ServerRpc(RequireOwnership = false)]
    private void AddShiftTasksServerRpc()
    {
        AddShiftTasksOnServer();
    }

    /// <summary>Routes a task-complete notification to the server. Obsolete — no-op in the new system.</summary>
    [System.Obsolete("NotifyTaskCompleteServerRpc is obsolete. The night phase is now gated by a minimum duration timer.")]
    [ServerRpc(RequireOwnership = false)]
    public void NotifyTaskCompleteServerRpc()
    {
        Debug.LogWarning("[ShiftManager] NotifyTaskCompleteServerRpc is obsolete and should not be called.");
    }

    [ClientRpc]
    private void AllTasksCompleteClientRpc()
    {
        BetweenShiftTaskManager.Instance?.HandleNightPhaseReady();
    }

    /// <summary>Forces all tasks to complete, bypassing individual task state. Debug only.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void ForceCompleteAllTasksServerRpc()
    {
        AllTasksCompleteClientRpc();
    }

    /// <summary>
    /// Called by any client when a player confirms going to bed.
    /// Broadcasts the end-of-shift report to all clients so both players see it simultaneously.
    /// The tracked counters are passed as ints (NGO-serializable); each client rebuilds
    /// the report rows using its own reward config and server-authored population values.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void TriggerEndOfShiftReportServerRpc()
    {
        if (!_campaignAdvancedForCurrentReport && CampaignManager.Instance != null)
        {
            CampaignManager.Instance.AdvanceDay();
            _campaignAdvancedForCurrentReport = true;
        }

        ShowEndOfShiftReportClientRpc(
            suspectsProcessed,
            suspectsPassedCorrect,
            suspectsPassedWrong,
            suspectsQuarantined,
            suspectsKilledCorrect,
            suspectsKilledWrong,
            populationModel.PopulationAlive.CurrentValue,
            populationModel.DeadOvernight.CurrentValue);
    }

    /// <summary>
    /// Runs on all clients to display the end-of-shift report UI built from the provided counters.
    /// </summary>
    [ClientRpc]
    private void ShowEndOfShiftReportClientRpc(
        int processed, int passedCorrect, int passedWrong,
        int quarantined, int killedCorrect, int killedWrong,
        int populationAlive, int deadOvernight)
    {
        int totalKilled = killedCorrect + killedWrong;

        var reportData = new List<EndOfShiftReportUI.ReportRowData>
        {
            // Header — informational only, no reward.
            new EndOfShiftReportUI.ReportRowData(
                $"Citizens Processed: {processed}", 0, false, isHeader: true),

            // Green rows — positive outcomes that earn money.
            new EndOfShiftReportUI.ReportRowData(
                $"Passed: {passedCorrect}",
                passedCorrect * rewardPerCorrectPass),

            new EndOfShiftReportUI.ReportRowData(
                $"Quarantined: {quarantined}",
                quarantined * rewardPerQuarantine),

            // Red rows — negative outcomes that cost money.
            new EndOfShiftReportUI.ReportRowData(
                $"Killed: {totalKilled}",
                totalKilled * penaltyPerKill, isPenalty: true),

            new EndOfShiftReportUI.ReportRowData(
                $"Non-Effected: {passedWrong}",
                passedWrong * penaltyPerWrongPass, isPenalty: true),
        };

        UIController.Instance.ShowEndShiftReport(reportData, deadOvernight);
    }

    private static void AppendPopulationRows(
        List<EndOfShiftReportUI.ReportRowData> reportData,
        int populationAlive,
        int deadOvernight)
    {
        if (reportData == null || populationAlive < 0)
            return;

        reportData.Add(new EndOfShiftReportUI.ReportRowData(
            $"Population Alive: {populationAlive}", 0, false, isHeader: true));

        reportData.Add(new EndOfShiftReportUI.ReportRowData(
            $"Dead Overnight: {Mathf.Max(0, deadOvernight)}", 0));
    }

    private IEnumerator InBetweenShiftSequence()
    {
        // End the previous night phase and score it before resetting the world.
        BetweenShiftTaskManager.Instance?.EndNightPhase();

        // Close the bunker door immediately so it is never seen open during the transition.
        _bunkerDoorController?.Reset();

        UIController.Instance.FadeIn();

        // Wait for the screen to reach full black (fade duration = 0.64s).
        // The end-of-shift report BG stays visible as an overlay during this window
        // so the world never flashes through before the fade completes.
        yield return new WaitForSeconds(0.64f);

        // Screen is now fully black — safe to tear down the report overlay.
        UIController.Instance.HideEndOfShiftReport();

        // Reset all shift state for the new day.
        ResetShiftData();
        ResetEnvironment();
        ResetSuspectsProcessed();
        SuspectController.Instance.ResetSuspects();

        // Advance the campaign day — server-only; propagates to all clients via NetworkVariable.
        // Usually this already happened before the report was shown so "Dead Overnight" can
        // include the just-simulated population losses. Keep the fallback for older/debug paths
        // that enter this transition without first broadcasting the report.
        if (IsServer)
        {
            if (_campaignAdvancedForCurrentReport)
                _campaignAdvancedForCurrentReport = false;
            else
                CampaignManager.Instance.AdvanceDay();
        }

        // Teleport the local player to their outside-bunker spawn while the screen is dark.
        if (PlayerInstance.Instance != null)
        {
            Transform bunkerSpawn = PlayerSpawner.Instance.GetOutsideBunkerSpawnPoint(PlayerInstance.Instance.OwnerClientId);
            PlayerInstance.Instance.SetPosition(bunkerSpawn);
            PlayerInstance.Instance.SetIsOutside(false);
        }

        yield return new WaitForSeconds(0.5f);
        UIController.Instance.FadeOut();
        yield return new WaitForSeconds(1f);

        // When the final demo day ends, hand off to the thanks-for-playing screen
        // instead of starting the next shift.
        if (CampaignManager.Instance != null && CampaignManager.Instance.IsCampaignComplete)
        {
            UIController.Instance.ShowThanksForPlayingScreen();
            yield break;
        }

        EnablePlayerControl();
        OnDoorUnlock?.Invoke();
        OnShiftReady?.Invoke();
        OnDayStart?.Invoke();
        PlayShiftStartFanfare();
    }

    [ClientRpc]
    private void StartNewShiftClientRpc()
    {
        if (IsServer) return;
        ResetEverything();
        StartCoroutine(NewShiftSequence());
    }

    public void CompletedShift()
    {
        _currentDay += 1;
        if (IsServer)
        {
            _networkCurrentDay.Value = _currentDay;
        }
    }

    public void ResetEnvironment(bool silent = false)
    {
        windowLampController.TurnRed();
        lever.Reset();
        _doorController?.Reset(silent);
        _bunkerDoorController?.Reset();

        if (_timecardMachine != null)
            _timecardMachine.Reset();
    }

    /// <summary>
    /// Opens the booth window shutter programmatically (server only).
    /// Safe to call when the shutter is already open — the lever will not toggle closed.
    /// Used by tutorial scripts that need to open the window automatically.
    /// </summary>
    public void OpenBoothShutter()
    {
        if (!IsServer) return;
        if (lever.IsUp) return;
        lever.OpenServerSide();
    }

    private void ResetShiftData()
    {
        shiftStarted.Value = false;
    }

    private void ResetEverything(bool silent = false)
    {
        ResetShiftData();
        ResetEnvironment(silent);
        ResetSuspectsProcessed();
        if (PlayerInstance.Instance != null)
        {
            PlayerInstance.Instance.SetPosition(PlayerSpawner.Instance.GetBoothSpawnPoint(PlayerInstance.Instance.OwnerClientId));
            PlayerInstance.Instance.RequestSetIsOutside(false);
        }
    }

    private IEnumerator NewShiftSequence()
    {
        PlayerPrefs.SetInt("dayNumber", _currentDay);

        if (DebugConsole.Instance.skipInitialShiftTransition || DebugConsole.Instance.autoStart)
        {
            if (_playingDirector != null)
            {
                _playingDirector.gameObject.SetActive(false);
                _playingDirector = null;
            }
            else if (introCutscene != null)
            {
                introCutscene.gameObject.SetActive(false);
            }
            UIController.Instance.HideEndOfShiftReport();
            SuspectController.Instance.ResetSuspects();
            if (PlayerInstance.Instance != null)
                PlayerInstance.Instance.SetIsOutside(false);
            yield return new WaitForEndOfFrame();
            EnablePlayerControl();
            OnShiftReady?.Invoke();
            OnDayStart?.Invoke();
            PlayShiftStartFanfare();
            yield break;
        }

        UIController.Instance.FadeIn();
        yield return new WaitForSeconds(2f);
        
        if (_playingDirector != null)
        {
            _playingDirector.gameObject.SetActive(false);
            _playingDirector = null;
        }
        else if (introCutscene != null)
        {
            introCutscene.gameObject.SetActive(false);
        }

        UIController.Instance.HideEndOfShiftReport();
        SuspectController.Instance.ResetSuspects();

        yield return new WaitForSeconds(1f);
        UIController.Instance.FadeOut();
        yield return new WaitForSeconds(1f);

        EnablePlayerControl();

        // On Day 1 there is no prior night phase, so the shift button is immediately ready.
        OnShiftReady?.Invoke();
        OnDayStart?.Invoke();
    }

    private void ResetSuspectsProcessed()
    {
        suspectsProcessed = 0;
        suspectsPassedCorrect = 0;
        suspectsPassedWrong = 0;
        suspectsQuarantined = 0;
        suspectsKilledCorrect = 0;
        suspectsKilledWrong = 0;
    }

    /// <summary>Plays the bell and shows the day number banner at the start of a shift.</summary>
    /// <summary>
    /// Set to true before a debug skip to prevent the day-number overlay from appearing.
    /// Automatically cleared after the first call to <see cref="PlayShiftStartFanfare"/>.
    /// </summary>
    public static bool SuppressFanfare;

    private void PlayShiftStartFanfare()
    {
        if (_currentDay == 3)
            StartCoroutine(PlayCreepyBell());
        else
            bellSound.Play();

        if (!SuppressFanfare)
            _startShiftScreen.ShowDayNumber(_currentDay);
        SuppressFanfare = false;
    }

    /// <summary>Plays the bell with a creepy, unstable pitch shift for Day 3.</summary>
    private IEnumerator PlayCreepyBell()
    {
        const float driftDelay   = 2f;
        const float sinkSpeed    = 0.20f;  // semitone descent rate (pitch units per second)
        const float sinkFloor    = 0.42f;  // lowest pitch before it bottoms out
        const float wobbleSpeed  = 1.1f;   // slow, low-frequency irregularity
        const float wobbleAmount = 0.05f;  // subtle so it doesn't fight the downward pull
        const float reverseSpeed = -3f;    // negative pitch = backwards; magnitude sets playback speed

        bellSound.pitch = 1f;
        bellSound.Play();

        // Phase 1: normal ring for the first 2 seconds
        float elapsed = 0f;
        while (bellSound.isPlaying && elapsed < driftDelay)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Phase 2: pitch steadily sinks with a slow, uneasy low-frequency wobble
        float sinkElapsed = 0f;
        while (bellSound.isPlaying)
        {
            sinkElapsed += Time.deltaTime;
            float sink   = Mathf.Max(sinkFloor, 1f - sinkElapsed * sinkSpeed);
            float wobble = Mathf.Sin(sinkElapsed * wobbleSpeed)        * wobbleAmount
                         + Mathf.Sin(sinkElapsed * wobbleSpeed * 1.6f) * (wobbleAmount * 0.5f);
            bellSound.pitch = sink + wobble;
            yield return null;
        }

        // Phase 3: rapidly play the clip backwards (negative pitch, seeded from end of clip)
        bellSound.timeSamples = bellSound.clip.samples - 1;
        bellSound.pitch = reverseSpeed;
        bellSound.Play();
        while (bellSound.isPlaying)
            yield return null;

        bellSound.pitch = 1f;
    }

    /// <summary>Restores full player control.</summary>
    private void EnablePlayerControl()
    {
        if (PlayerInstance.Instance == null)
        {
            Debug.LogWarning("[ShiftManager] EnablePlayerControl: PlayerInstance not ready yet.");
            return;
        }

        PlayerInstance.Instance.CanControl = true;
        PlayerInstance.Instance.SetCanInteract(true);
        PlayerInstance.Instance.SetCanMove(true);
    }

    /// <summary>
    /// Immediately stops the intro cutscene and hides its GameObject.
    /// Call this on clients that join while the cutscene is already playing on the host.
    /// </summary>
    public void StopIntroCutscene()
    {
        if (_playingDirector != null)
        {
            _playingDirector.Stop();
            _playingDirector.gameObject.SetActive(false);
            _playingDirector = null;
        }
        else if (introCutscene != null)
        {
            introCutscene.Stop();
            introCutscene.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Call this from anywhere — server or client. Routes through the server so
    /// the cutscene is triggered on all connected clients simultaneously.
    /// </summary>
    public void InitiateIntroCutscene()
    {
        if (IsServer)
        {
            GameManager.Instance.SetIntroCutsceneStarted();
            InitiateIntroCutsceneClientRpc();
        }
        else
            RequestInitiateIntroCutsceneServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestInitiateIntroCutsceneServerRpc()
    {
        GameManager.Instance.SetIntroCutsceneStarted();
        InitiateIntroCutsceneClientRpc();
    }

    [ClientRpc]
    private void InitiateIntroCutsceneClientRpc()
    {
        RunInitiateIntroCutscene();
    }

    private void RunInitiateIntroCutscene()
    {
        UIController.Instance.ClosePlayerUI();
        PlayerInstance.Instance.SetCanInteract(false);
        PlayerInstance.Instance.SetCanMove(false);
        // Freeze all camera look input for the duration of the cutscene.
        // SetCanMove(false) only stops movement — Rotate() still runs unless CanControl
        // is also disabled. Without this, Player 2 can look around freely during the
        // cutscene, leaving the camera at a stale angle when the cutscene VCam releases.
        PlayerInstance.Instance.CanControl = false;
        PlayerInstance.Instance.DisableReticle();
        StartCoroutine(PlayIntroCutscene());
    }

    private IEnumerator PlayIntroCutscene()
    {
        // Start the audio fade immediately so it runs concurrently with the screen fade.
        ambientAudio.DOFade(0, 2);

        // Wait until the screen is fully dark before teleporting the player.
        yield return StartCoroutine(UIController.Instance.FadeInAndWait());

        ResetEverything(true);
        yield return new WaitForSeconds(1);
        ResetEverything(true); // Called twice — player position was not resetting reliably in a single call

        _playingDirector = ActiveIntroCutscene;

        if (_playingDirector != null)
        {
            _playingDirector.gameObject.SetActive(true);
        }

        yield return new WaitForSeconds(.5f);
        UIController.Instance.FadeOut();
    }

    /// <summary>
    /// Debug shortcut: skips the main menu, lobby, and all cutscenes, then spawns the player
    /// directly in the booth with the shift switch primed and ready.
    /// Mirrors <see cref="EndIntroCutsceneSequence"/> but also handles the menu-to-gameplay
    /// transition since we're entering from the title screen.
    /// Call this from <see cref="DebugConsole"/> after starting the host and creating the lobby.
    /// </summary>
    public void SkipToBoothReady()
    {
        if (IsServer)
            SkipToBoothReadyClientRpc();
        else
            StartCoroutine(SkipToBoothReadySequence());
    }

    /// <summary>
    /// Debug shortcut. Runs the full booth-ready setup and then immediately activates
    /// <paramref name="targetDay"/> on the server before <see cref="OnDayStart"/> fires,
    /// so the day's <see cref="DayBase.DayActivated"/> is subscribed in time to catch it.
    /// </summary>
    public void SkipToBoothReadyOnDay(int targetDay)
    {
        if (IsServer)
            SkipToBoothReadyOnDayClientRpc(targetDay);
        else
            StartCoroutine(SkipToBoothReadySequence(targetDay));
    }

    /// <summary>
    /// Debug shortcut. Runs the full environment-reset setup and places the player at the
    /// outside-bunker spawn point instead of the booth — simulating the start of a new day
    /// from outside the bunker. Call <see cref="CampaignManager.JumpToDay"/> before this so
    /// the NetworkVariable propagates to all clients before <see cref="PlayShiftStartFanfare"/>
    /// fires (mirrors the <see cref="SkipToBoothReady"/> / <see cref="DebugConsole.SkipToDay"/>
    /// pattern).
    /// </summary>
    public void SkipToOutsideBunker()
    {
        if (IsServer)
            SkipToOutsideBunkerClientRpc();
        else
            StartCoroutine(SkipToOutsideBunkerSequence());
    }

    [ClientRpc]
    private void SkipToBoothReadyClientRpc()
    {
        StartCoroutine(SkipToBoothReadySequence());
    }

    [ClientRpc]
    private void SkipToBoothReadyOnDayClientRpc(int targetDay)
    {
        StartCoroutine(SkipToBoothReadySequence(targetDay));
    }

    [ClientRpc]
    private void SkipToOutsideBunkerClientRpc()
    {
        StartCoroutine(SkipToOutsideBunkerSequence());
    }

    private IEnumerator SkipToOutsideBunkerSequence()
    {
        MainMenuController.Instance.TransitionToGameplay();
        AudioManager.Instance.StartAmbientAudio();

        yield return new WaitForEndOfFrame();

        if (_playingDirector != null)
        {
            _playingDirector.gameObject.SetActive(false);
            _playingDirector = null;
        }
        else if (introCutscene != null)
        {
            introCutscene.gameObject.SetActive(false);
        }

        ResetShiftData();
        ResetSuspectsProcessed();
        ResetEnvironment();
        SuspectController.Instance.ResetSuspects();

        // Wait for a valid PlayerInstance — same guard as SkipToBoothReadySequence.
        yield return new WaitUntil(() => PlayerInstance.Instance != null && PlayerSpawner.Instance != null);

        Transform bunkerSpawn = PlayerSpawner.Instance.GetOutsideBunkerSpawnPoint(PlayerInstance.Instance.OwnerClientId);
        PlayerInstance.Instance.SetPosition(bunkerSpawn);
        PlayerInstance.Instance.SetIsOutside(false);

        UIController.Instance.ShowPlayerUI();
        EnablePlayerControl();
        GameManager.Instance.OnGameStart?.Invoke();
        OnShiftReady?.Invoke();
        OnDayStart?.Invoke();
        PlayShiftStartFanfare();
    }

    private IEnumerator SkipToBoothReadySequence(int targetDay = -1)
    {
        MainMenuController.Instance.TransitionToGameplay();
        AudioManager.Instance.StartAmbientAudio();

        yield return new WaitForEndOfFrame();

        if (_playingDirector != null)
        {
            _playingDirector.gameObject.SetActive(false);
            _playingDirector = null;
        }
        else if (introCutscene != null)
        {
            introCutscene.gameObject.SetActive(false);
        }

        ResetShiftData();
        ResetSuspectsProcessed();
        ResetEnvironment();
        SuspectController.Instance.ResetSuspects();

        // Wait for a valid PlayerInstance — on repeated Play sessions without domain reload
        // the previous session's destroyed instance lingers as null-equivalent, so a bare
        // != null check silently skips SetPosition. WaitUntil correctly blocks until a fresh
        // instance is available regardless of static state from the previous session.
        yield return new WaitUntil(() => PlayerInstance.Instance != null && PlayerSpawner.Instance != null);

        PlayerInstance.Instance.SetPosition(PlayerSpawner.Instance.GetBoothSpawnPoint(PlayerInstance.Instance.OwnerClientId));
        PlayerInstance.Instance.SetIsOutside(false);

        // Jump to a specific day (server only) after the player is in position and before
        // OnDayStart fires, so DayActivated subscribes to OnDayStart in time to catch it.
        if (IsServer && targetDay > 0)
            CampaignManager.Instance?.JumpToDay(targetDay);

        UIController.Instance.ShowPlayerUI();
        EnablePlayerControl();
        GameManager.Instance.OnGameStart?.Invoke();
        OnShiftReady?.Invoke();
        OnDayStart?.Invoke();
        PlayShiftStartFanfare();
    }

    /// <summary>
    /// Debug shortcut: runs the full booth-ready setup then immediately ends the shift,
    /// landing directly in the night phase with tasks assigned.
    /// </summary>
    private IEnumerator DebugSkipToAfterShift()
    {
        SkipToBoothReadyClientRpc();

        yield return new WaitForEndOfFrame();
        yield return null;

        EndShift();
        // Night phase begins automatically via EndShift() — no manual transition needed.
    }

    /// <summary>
    /// Ends the intro cutscene for all connected clients regardless of who calls it.
    /// Safe to call from any client or the server.
    /// </summary>
    /// <summary>
    /// Guards against <see cref="EndIntroCutscene"/> being called by both host and client UI
    /// simultaneously, which would dispatch <see cref="EndIntroCutsceneClientRpc"/> twice and
    /// fire <see cref="OnDayStart"/> twice on the server — starting two tutorial coroutines.
    /// </summary>
    private bool _introCutsceneEnded = false;

    public void EndIntroCutscene()
    {
        ambientAudio.DOFade(1, 2);

        if (IsServer)
        {
            if (_introCutsceneEnded) return;
            _introCutsceneEnded = true;
            EndIntroCutsceneClientRpc();
        }
        else
            EndIntroCutsceneServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void EndIntroCutsceneServerRpc()
    {
        if (_introCutsceneEnded) return;
        _introCutsceneEnded = true;
        ambientAudio.DOFade(1, 2);
        EndIntroCutsceneClientRpc();
    }

    [ClientRpc]
    private void EndIntroCutsceneClientRpc()
    {
        StartCoroutine(EndIntroCutsceneSequence());
    }

    /// <summary>
    /// Transitions from the intro cutscene directly into a "shift ready" state.
    /// Resets the environment and player, then fires <see cref="OnShiftReady"/> so
    /// the switch button is primed — without starting the shift or triggering the
    /// night-phase task system.
    /// </summary>
    private IEnumerator EndIntroCutsceneSequence()
    {
        PlayerPrefs.SetInt("dayNumber", _currentDay);

        UIController.Instance.FadeIn();
        yield return new WaitForSeconds(2f);
        
        if (_playingDirector != null)
        {
            _playingDirector.gameObject.SetActive(false);
            _playingDirector = null;
        }
        else if (introCutscene != null)
        {
            introCutscene.gameObject.SetActive(false);
        }

        ResetShiftData();
        ResetSuspectsProcessed();
        ResetEnvironment(true);
        SuspectController.Instance.ResetSuspects();

        if (PlayerInstance.Instance != null)
        {
            PlayerInstance.Instance.SetPosition(PlayerSpawner.Instance.GetBoothSpawnPoint(PlayerInstance.Instance.OwnerClientId));
            PlayerInstance.Instance.RequestSetIsOutside(false);
            // Reset the camera to a neutral forward orientation so the view doesn't snap
            // to whatever angle the player was looking at before or during the cutscene.
            PlayerInstance.Instance.ResetCameraOrientation();
        }

        yield return new WaitForSeconds(1f);
        UIController.Instance.FadeOut();
        yield return new WaitForSeconds(1f);

        UIController.Instance.ShowPlayerUI();
        EnablePlayerControl();
        OnShiftReady?.Invoke();
        OnDayStart?.Invoke();
        PlayShiftStartFanfare();
    }

    /// <summary>
    /// Debug helper — enables clock-out on the timecard machine as if all suspects had been
    /// processed, without ending the shift or skipping any suspects. Server only.
    /// </summary>
    public void DebugEnableClockOut()
    {
        if (!IsServer) return;

        if (_timecardMachine != null)
            _timecardMachine.EnableClockOut();

        NotifyClockOutReadyClientRpc();
        Debug.Log("[ShiftManager] DebugEnableClockOut: timecard machine primed for clock-out.");
    }

    public void SetNextShiftReady()
    {
        if (IsServer)
        {
            SetNextShiftReadyClientRpc();
        }
        RunSetNextShiftReady();
    }

    [ClientRpc]
    private void SetNextShiftReadyClientRpc()
    {
        if (IsServer) return;
        RunSetNextShiftReady();
    }

    private void RunSetNextShiftReady()
    {
        UIController.Instance.HideEndOfShiftReport();
        UIController.Instance.FadeIn();

        ResetShiftData();
        ResetSuspectsProcessed();
        SuspectController.Instance.ResetSuspects();

        PlayerPrefs.SetInt("dayNumber", _currentDay);

        EnablePlayerControl();
        OnShiftReady?.Invoke();
    }
}
