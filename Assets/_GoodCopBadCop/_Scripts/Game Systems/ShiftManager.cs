using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Playables;

public class ShiftManager : NetworkBehaviour
{
    public static ShiftManager Instance;

    /// <summary>Fired on the server whenever a suspect is killed.</summary>
    public static event System.Action OnSuspectKilled;

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

    public int suspectsProcessed = 0;
    public int suspectsPassedCorrect = 0;
    public int suspectsPassedWrong = 0;
    public int suspectsQuarantined = 0;
    public int suspectsKilledCorrect = 0;
    public int suspectsKilledWrong = 0;

    [Header("End of Shift Rewards")]
    [Tooltip("Coupons earned for correctly passing a non-infected citizen.")]
    [SerializeField] private int rewardPerCorrectPass = 10;
    [Tooltip("Coupons deducted for incorrectly passing an infected suspect.")]
    [SerializeField] private int penaltyPerWrongPass = 15;
    [Tooltip("Coupons earned for correctly eliminating an infected suspect.")]
    [SerializeField] private int rewardPerCorrectKill = 10;
    [Tooltip("Coupons deducted for incorrectly eliminating a non-infected citizen.")]
    [SerializeField] private int penaltyPerWrongKill = 20;

    private int _taskCompletedCount = 0;

    [Header("Environment Set Up")]
    [SerializeField] private SwitchButton _switchButton;
    [SerializeField] private WindowLampController windowLampController;
    [SerializeField] private DoorController _doorController;
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

    private Coroutine _suspectSchedulerCoroutine;

    #region Events & Date Helpers
    public Action OnShiftStart { get; set; }
    public Action OnShiftEnd { get; set; }
    public Action OnShiftReady { get; set; }
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
        BetweenShiftTaskManager.OnAllTasksComplete += HandleAllTasksComplete;
    }

    private void OnDisable()
    {
        BetweenShiftTaskManager.OnAllTasksComplete -= HandleAllTasksComplete;
    }

    /// <summary>
    /// Called on all clients when every between-shift task has been completed.
    /// Fires <see cref="OnShiftReady"/> so the switch button becomes pressable,
    /// and prompts the player to return to the booth.
    /// </summary>
    private void HandleAllTasksComplete()
    {
        OnShiftReady?.Invoke();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _networkCurrentDay.OnValueChanged += OnCurrentDayChanged;

        if (IsServer && DebugConsole.Instance != null && DebugConsole.Instance.skipToBoothReady)
            SkipToBoothReady();

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

        if (SuspectController.Instance.SuspectIndex >= DailySuspectManager.Instance.shiftSuspects.Count - 1)
        {
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

        if (_suspectSchedulerCoroutine != null)
            StopCoroutine(_suspectSchedulerCoroutine);

        _suspectSchedulerCoroutine = StartCoroutine(ScheduledSuspectArrival());
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

        if (!AreAllPlayersInsideBooth())
        {
            NotifyNotAllInsideClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { requestingClientId } }
            });
            return;
        }

        shiftStarted.Value = true;
        StartShiftClientRpc();
    }

    /// <summary>Returns true when every connected player has IsOutside == false.</summary>
    private bool AreAllPlayersInsideBooth()
    {
        foreach (var player in FindObjectsByType<PlayerInstance>(FindObjectsSortMode.None))
        {
            if (player.IsOutside) return false;
        }
        return true;
    }

    [ClientRpc]
    private void NotifyNotAllInsideClientRpc(ClientRpcParams rpcParams = default)
    {
        MegaphoneDialogueManager.Instance.SayNotAllInside();
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

    private IEnumerator OpenWindowSequence()
    {
        ResetSuspectsProcessed();
        SuspectController.Instance.ResetSuspects();

        PlayBuzzerSound();
        windowLampController.TurnGreen();

        // Lock the exit door for the full shift when the campaign day requires it.
        if (CampaignManager.Instance != null && CampaignManager.Instance.IsDoorLockedForShift)
            OnDoorLock?.Invoke();

        yield return new WaitForSeconds(3f);

        OnShiftStart?.Invoke();
        yield return new WaitForSeconds(0.5f);

        yield return new WaitForSeconds(3f);

        // First suspect arrives quickly on the server — use the shorter initial interval.
        if (IsServer)
        {
            if (_suspectSchedulerCoroutine != null)
                StopCoroutine(_suspectSchedulerCoroutine);

            Vector2 firstInterval = OverrideFirstArrivalInterval ?? firstSuspectArrivalInterval;
            OverrideFirstArrivalInterval = null;
            _suspectSchedulerCoroutine = StartCoroutine(ScheduledSuspectArrival(firstInterval));
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

        CompletedShift();

        EndShiftClientRpc(
            suspectsProcessed,
            suspectsPassedCorrect,
            suspectsPassedWrong,
            suspectsQuarantined,
            suspectsKilledCorrect,
            suspectsKilledWrong
        );
    }

    [ClientRpc]
    private void EndShiftClientRpc(
        int processed, int passedCorrect, int passedWrong,
        int quarantined, int killedCorrect, int killedWrong)
    {
        SFXController.Instance.Play(endOfLevelSound);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().StopMoving();
        OnShiftEnd?.Invoke();

        var rows = new List<EndOfShiftReportUI.ReportRowData>
        {
            new EndOfShiftReportUI.ReportRowData("Processed: " + processed + " Citizens",           0,                                   false, true),
            new EndOfShiftReportUI.ReportRowData("Passed: " + (passedCorrect + passedWrong),        0,                                   false, true),
            new EndOfShiftReportUI.ReportRowData("    Non-Infected: " + passedCorrect,              passedCorrect  * rewardPerCorrectPass, false),
            new EndOfShiftReportUI.ReportRowData("    Infected: " + passedWrong,                    passedWrong    * penaltyPerWrongPass,  true),
            new EndOfShiftReportUI.ReportRowData("Quarantined: " + quarantined,                     0,                                   false),
            new EndOfShiftReportUI.ReportRowData("Killed: " + (killedCorrect + killedWrong),        0,                                   false, true),
            new EndOfShiftReportUI.ReportRowData("    Infected: " + killedCorrect,                  killedCorrect  * rewardPerCorrectKill, false),
            new EndOfShiftReportUI.ReportRowData("    Non-Infected: " + killedWrong,                killedWrong    * penaltyPerWrongKill,  true),
        };

        UIController.Instance.ShowEndShiftReport(rows);
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
    /// Called when the player presses Continue on the end-of-shift report.
    /// Fades the screen, resets all shift state, advances to the next campaign day,
    /// and places the player back at their outside spawn so they walk into the booth
    /// to start the next shift.
    /// </summary>
    public void StartInBetweenShiftSequence()
    {
        StartCoroutine(InBetweenShiftSequence());
    }

    /// <summary>
    /// Registers tasks in GuidebookTaskRegistry and notifies all clients.
    /// Called mid-shift to activate tasks in the guidebook and HUD.
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

    /// <summary>Routes a task-complete notification to the server for authoritative counting.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void NotifyTaskCompleteServerRpc()
    {
        _taskCompletedCount++;
        Debug.Log($"[ShiftManager] Tasks completed: {_taskCompletedCount} / {BetweenShiftTaskManager.Instance?.Tasks.Length}");

        int total = BetweenShiftTaskManager.Instance?.Tasks.Length ?? 0;
        if (_taskCompletedCount >= total)
            AllTasksCompleteClientRpc();
    }

    [ClientRpc]
    private void AllTasksCompleteClientRpc()
    {
        BetweenShiftTaskManager.Instance?.HandleAllTasksComplete();
    }

    /// <summary>Forces all tasks to complete, bypassing individual task state. Debug only.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void ForceCompleteAllTasksServerRpc()
    {
        AllTasksCompleteClientRpc();
    }

    private IEnumerator InBetweenShiftSequence()
    {
        UIController.Instance.FadeIn();
        yield return new WaitForSeconds(1.5f);
        UIController.Instance.HideEndOfShiftReport();

        // Reset all shift state for the new day.
        ResetShiftData();
        ResetEnvironment();
        ResetSuspectsProcessed();
        SuspectController.Instance.ResetSuspects();

        // Advance to the next campaign day — server-only; propagates to clients via NetworkVariable.
        if (IsServer)
            CampaignManager.Instance.AdvanceDay();

        // Teleport the local player to their outside spawn while the screen is dark.
        if (PlayerInstance.Instance != null)
        {
            Transform outsideSpawn = PlayerSpawner.Instance.GetOutsideSpawnPoint(PlayerInstance.Instance.OwnerClientId);
            PlayerInstance.Instance.SetPosition(outsideSpawn);
            PlayerInstance.Instance.SetIsOutside(true);
        }

        yield return new WaitForSeconds(0.5f);
        UIController.Instance.FadeOut();
        yield return new WaitForSeconds(1f);

        EnablePlayerControl();
        OnDoorUnlock?.Invoke();
        OnShiftReady?.Invoke();
        OnDayStart?.Invoke();
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

    public void ResetEnvironment()
    {
        windowLampController.TurnRed();
        lever.Reset();

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

    private void ResetEverything()
    {
        ResetShiftData();
        ResetEnvironment();
        ResetSuspectsProcessed();
        if (PlayerInstance.Instance != null)
        {
            PlayerInstance.Instance.SetPosition(PlayerSpawner.Instance.GetBoothSpawnPoint(PlayerInstance.Instance.OwnerClientId));
            PlayerInstance.Instance.SetIsOutside(false);
        }
    }

    private IEnumerator NewShiftSequence()
    {
        PlayerPrefs.SetInt("dayNumber", _currentDay);

        if (DebugConsole.Instance.skipInitialShiftTransition || DebugConsole.Instance.autoStart)
        {
            introCutscene.gameObject.SetActive(false);
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
        introCutscene.gameObject.SetActive(false);

        UIController.Instance.HideEndOfShiftReport();
        SuspectController.Instance.ResetSuspects();

        yield return new WaitForSeconds(1f);
        UIController.Instance.FadeOut();
        yield return new WaitForSeconds(1f);

        EnablePlayerControl();

        // OnShiftReady is deferred until all between-shift tasks are complete.
        // BetweenShiftTaskManager.OnAllTasksComplete → HandleAllTasksComplete will fire it.
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
    private void PlayShiftStartFanfare()
    {
        bellSound.Play();
        _startShiftScreen.ShowDayNumber(_currentDay);
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
        if (introCutscene == null) return;
        introCutscene.Stop();
        introCutscene.gameObject.SetActive(false);
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
        UIController.Instance.FadeIn();
        UIController.Instance.ClosePlayerUI();
        PlayerInstance.Instance.SetCanInteract(false);
        PlayerInstance.Instance.SetCanMove(false);
        PlayerInstance.Instance.DisableReticle();
        StartCoroutine(PlayIntroCutscene());
    }

    private IEnumerator PlayIntroCutscene()
    {
        ambientAudio.DOFade(0, 2);
        yield return new WaitForSeconds(2f);
        ResetEverything();
        yield return new WaitForSeconds(1);
        ResetEverything(); // Called twice — player position was not resetting reliably in a single call
        introCutscene.gameObject.SetActive(true);
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

    [ClientRpc]
    private void SkipToBoothReadyClientRpc()
    {
        StartCoroutine(SkipToBoothReadySequence());
    }

    private IEnumerator SkipToBoothReadySequence()
    {
        MainMenuController.Instance.TransitionToGameplay();
        AudioManager.Instance.StartAmbientAudio();

        yield return new WaitForEndOfFrame();

        introCutscene.gameObject.SetActive(false);

        ResetShiftData();
        ResetSuspectsProcessed();
        ResetEnvironment();
        SuspectController.Instance.ResetSuspects();

        if (PlayerInstance.Instance != null)
        {
            PlayerInstance.Instance.SetPosition(PlayerSpawner.Instance.GetBoothSpawnPoint(PlayerInstance.Instance.OwnerClientId));
            PlayerInstance.Instance.SetIsOutside(false);
        }

        UIController.Instance.ShowPlayerUI();
        EnablePlayerControl();
        GameManager.Instance.OnGameStart?.Invoke();
        OnShiftReady?.Invoke();
        OnDayStart?.Invoke();
        PlayShiftStartFanfare();
    }

    /// <summary>
    /// Debug shortcut: runs the full booth-ready setup then immediately ends the shift
    /// and auto-dismisses the end-of-shift report, landing directly in the night phase
    /// with tasks assigned.
    /// </summary>
    private IEnumerator DebugSkipToAfterShift()
    {
        SkipToBoothReadyClientRpc();

        yield return new WaitForEndOfFrame();
        yield return null;

        EndShift();

        yield return new WaitForSeconds(1f);

        StartInBetweenShiftSequence();
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
        introCutscene.gameObject.SetActive(false);

        ResetShiftData();
        ResetSuspectsProcessed();
        ResetEnvironment();
        SuspectController.Instance.ResetSuspects();

        if (PlayerInstance.Instance != null)
        {
            PlayerInstance.Instance.SetPosition(PlayerSpawner.Instance.GetBoothSpawnPoint(PlayerInstance.Instance.OwnerClientId));
            PlayerInstance.Instance.SetIsOutside(false);
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
