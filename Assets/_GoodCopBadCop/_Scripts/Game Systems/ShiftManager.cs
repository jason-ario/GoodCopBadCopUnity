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

    [SerializeField] private int couponsPerPassed = 3;
    [SerializeField] private int couponsPenaltyPerPassed = 2;
    [SerializeField] private int couponsPerKilled = 3;
    [SerializeField] private int couponsPenaltyPerKilled = 3;
    [SerializeField] private int couponsPerQuarantined = 3;
    [SerializeField] private int couponsPenaltyPerQuarantined = 2;

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
        _currentDay = PlayerPrefs.GetInt("dayNumber", 1);
        Debug.Log($"Game started on {_startDate.AddDays(_currentDay - 1):dd MMMM yyyy} (Day {_currentDay})");
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

        if (_suspectSchedulerCoroutine != null)
            StopCoroutine(_suspectSchedulerCoroutine);

        _suspectSchedulerCoroutine = StartCoroutine(ScheduledSuspectArrival());
    }

    [ClientRpc]
    private void NotifyClockOutReadyClientRpc()
    {
        TutorialManager.Instance.ShowTutorialText("Your shift is over. Clock out to end the day.");
    }

    /// <summary>
    /// Waits a random interval then triggers the next suspect to approach the booth.
    /// Runs on the server only. Uses <paramref name="interval"/> if provided, otherwise falls back to <see cref="suspectArrivalInterval"/>.
    /// </summary>
    private IEnumerator ScheduledSuspectArrival(Vector2? interval = null)
    {
        Vector2 range = interval ?? suspectArrivalInterval;
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
        TutorialManager.Instance.ShowTutorialText("All inspectors must be inside the booth to begin the shift.");
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
        ResetEnvironment();
        ResetSuspectsProcessed();
        SuspectController.Instance.ResetSuspects();

        PlayBuzzerSound();
        windowLampController.TurnGreen();

        yield return new WaitForSeconds(3f);

        OnShiftStart?.Invoke();
        yield return new WaitForSeconds(0.5f);

        yield return new WaitForSeconds(3f);

        // First suspect arrives quickly on the server — use the shorter initial interval.
        if (IsServer)
        {
            if (_suspectSchedulerCoroutine != null)
                StopCoroutine(_suspectSchedulerCoroutine);

            _suspectSchedulerCoroutine = StartCoroutine(ScheduledSuspectArrival(firstSuspectArrivalInterval));
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

        CompletedShift();

        EndShiftClientRpc(
            suspectsProcessed,
            suspectsPassedCorrect,
            suspectsPassedWrong,
            suspectsQuarantined,
            suspectsKilledCorrect,
            suspectsKilledWrong,
            couponsPerPassed,
            couponsPenaltyPerPassed,
            couponsPerKilled,
            couponsPenaltyPerKilled,
            couponsPerQuarantined,
            couponsPenaltyPerQuarantined
        );
    }

    [ClientRpc]
    private void EndShiftClientRpc(
        int processed, int passedCorrect, int passedWrong,
        int quarantined, int killedCorrect, int killedWrong,
        int perPassed, int penaltyPerPassed,
        int perKilled, int penaltyPerKilled,
        int perQuarantined, int penaltyPerQuarantined)
    {
        SFXController.Instance.Play(endOfLevelSound);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().StopMoving();
        OnShiftEnd?.Invoke();

        var rows = new List<EndOfShiftReportUI.ReportRowData>
        {
            new EndOfShiftReportUI.ReportRowData("Processed: " + processed + " Citizens", 0, false, true),
            new EndOfShiftReportUI.ReportRowData("Passed: " + (passedCorrect + passedWrong), 0, false, true),
            new EndOfShiftReportUI.ReportRowData("    Non-Infected: " + passedCorrect, perPassed * passedCorrect, false),
            new EndOfShiftReportUI.ReportRowData("    Infected: " + passedWrong, perPassed * passedWrong, true),
            new EndOfShiftReportUI.ReportRowData("Quarantined: " + quarantined, 0, false, false),
            new EndOfShiftReportUI.ReportRowData("Killed: " + (killedCorrect + killedWrong), 0, false, true),
            new EndOfShiftReportUI.ReportRowData("    Infected: " + killedCorrect, perKilled * killedCorrect, false),
            new EndOfShiftReportUI.ReportRowData("    Non-Infected: " + killedWrong, penaltyPerKilled * killedWrong, true),
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
    /// Fades the screen, re-enables player movement, and begins the between-shift
    /// night phase — tasks now run concurrently throughout the whole work day.
    /// </summary>
    public void StartInBetweenShiftSequence()
    {
        StartCoroutine(InBetweenShiftSequence());

        if (IsServer && BetweenShiftTaskManager.Instance != null)
            BetweenShiftTaskManager.Instance.BeginNightPhase();
    }

    private IEnumerator InBetweenShiftSequence()
    {
        UIController.Instance.FadeIn();
        yield return new WaitForSeconds(1.5f);
        UIController.Instance.HideEndOfShiftReport();
        yield return new WaitForSeconds(0.5f);
        UIController.Instance.FadeOut();
        yield return new WaitForSeconds(1f);

        EnablePlayerControl();

        TutorialManager.Instance.SayEndOfShiftDialogue();
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

        if (DebugConsole.Instance.skipInitialShiftTransition)
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
        TutorialManager.Instance.ShowTutorialText("You may now prepare for your next shift");

        // Tasks run concurrently during the work day — shift ready fires immediately.
        if (IsServer && BetweenShiftTaskManager.Instance != null)
            BetweenShiftTaskManager.Instance.BeginNightPhase();

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
            InitiateIntroCutsceneClientRpc();
        else
            RequestInitiateIntroCutsceneServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestInitiateIntroCutsceneServerRpc()
    {
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

    public void EndIntroCutscene()
    {
        UIController.Instance.ShowPlayerUI();
        ambientAudio.DOFade(1, 2);

        if (IsServer)
            EndIntroCutsceneClientRpc();
        else
            StartCoroutine(EndIntroCutsceneSequence());
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
