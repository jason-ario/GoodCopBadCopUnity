using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;

public class ShiftManager : NetworkBehaviour
{
    public static ShiftManager Instance;

    [Header("Network Variables")]
    public NetworkVariable<bool> shiftStarted = new NetworkVariable<bool>(false);

    [Header("Settings")]
    [SerializeField] private int suspectsPerShift = 6;

    [Header("Set Up")]
    private int _currentDay = 1;
    public int CurrentDay => _currentDay;
    [SerializeField] private StartShiftScreen _startShiftScreen;
    [SerializeField] private FaxMachine _faxMachine;
    [SerializeField] private float faxMachineDelay = 4f;
    [SerializeField] private AudioSource bellSound;
    [SerializeField] private AudioClip endOfLevelSound;
    [SerializeField] private AudioClip knockOnDoorSound;
    [SerializeField] private GameObject cardboardBox;
    [SerializeField] private MachineShake doorShake;
    [SerializeField] private PlayableDirector introCutscene;
    [SerializeField] private AudioSource ambientAudio;
    [SerializeField] private AudioSource buzzerSound;

    public int SuspectsPerShift => suspectsPerShift;

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
    [SerializeField] private Animator rollingShutter;
    [SerializeField] private SwitchButton _switchButton;
    [SerializeField] private WindowLampController windowLampController;
    [SerializeField] DoorController _doorController;
    [SerializeField] private Lever lever;
    [SerializeField] private TextMeshPro calendarText;

    #region Events
    public UnityAction OnShiftStart { get; set; }
    public UnityAction OnShiftReady { get; set; }

    #endregion

    private void Awake()
    {
        Instance = this;
    }


    public void SetNextSuspectReady()
    {
        if (SuspectController.Instance.SuspectIndex >= ShiftManager.Instance.SuspectsPerShift)
        {
            EndShift();
            return;
        }
        
        _switchButton.SetReady(true);
    }
    
    private IEnumerator StartShiftSequence()
    {
        bellSound.Play();
        _startShiftScreen.ShowDayNumber(_currentDay);
        OnShiftStart?.Invoke();
        
        yield return new WaitForSeconds(faxMachineDelay);

        //_faxMachine.OnShiftStart();

        //yield return new WaitForSeconds(10);

        //GiveBonusBox();
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
        Debug.Log("Try Start Shift");

        if (IsServer)
            StartShiftServer();
        else
            RequestStartShiftServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStartShiftServerRpc()
    {
        StartShiftServer();
    }

    private void StartShiftServer()
    {
        if (!IsServer) return;
        if (shiftStarted.Value) return;

        shiftStarted.Value = true;
        StartShiftClientRpc();
    }

    [ClientRpc]
    private void StartShiftClientRpc()
    {
        StartCoroutine(OpenWindowSequence(true));
    }

    public void OpenWindow()
    {
        StartCoroutine(OpenWindowSequence(false));
    }

    public void PlayBuzzerSound()
    {
        buzzerSound.Play();
    }

    private IEnumerator OpenWindowSequence(bool startRoundAfterOpening)
    {
        PlayBuzzerSound();
        yield return new WaitForSeconds(0.5f);
        windowLampController.TurnGreen();

        yield return new WaitForSeconds(3f);
        rollingShutter.SetBool("Open", true);

        yield return new WaitForSeconds(3f);

        SuspectController.Instance.NextSuspect();
    }

    public void EndShift()
    {
        SFXController.Instance.Play(endOfLevelSound);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().StopMoving();

        var rows = new List<EndOfShiftReportUI.ReportRowData>
        {
            new EndOfShiftReportUI.ReportRowData("Processed: " + suspectsProcessed + " Citizens", 0, false, true),
            new EndOfShiftReportUI.ReportRowData("Passed: " + (suspectsPassedCorrect + suspectsPassedWrong), 0, false, true),
            new EndOfShiftReportUI.ReportRowData("    Non-Infected: " + suspectsPassedCorrect, couponsPerPassed * suspectsPassedCorrect, false),
            new EndOfShiftReportUI.ReportRowData("    Infected: " + suspectsPassedWrong, couponsPerPassed * suspectsPassedWrong, true),
            new EndOfShiftReportUI.ReportRowData("Quarantined: " + suspectsQuarantined, 0, false, false),
            new EndOfShiftReportUI.ReportRowData("Killed: " + (suspectsKilledCorrect + suspectsKilledWrong), 0, false, true),
            new EndOfShiftReportUI.ReportRowData("    Infected: " + suspectsKilledCorrect, couponsPerKilled * suspectsKilledCorrect, false),
            new EndOfShiftReportUI.ReportRowData("    Non-Infected: " + suspectsKilledWrong, couponsPenaltyPerKilled * suspectsKilledWrong, true),
        };

        UIController.Instance.ShowEndShiftReport(rows);
    }

    public void PassedSuspect(SuspectCharacter suspectCharacter)
    {
        suspectsProcessed += 1;

        if (suspectCharacter.IsInfected)
            suspectsPassedWrong += 1;
        else
            suspectsPassedCorrect += 1;
    }

    public void KillSuspect(SuspectCharacter suspectCharacter)
    {
        suspectsProcessed += 1;

        if (suspectCharacter.IsInfected)
            suspectsKilledCorrect += 1;
        else
            suspectsKilledWrong += 1;
    }

    public void QuarantinedSuspect()
    {
        suspectsProcessed += 1;
        suspectsQuarantined += 1;
    }

    public void StartNewShift()
    {
        ResetEverything();
        StartCoroutine(NewShiftSequence());
    }

    public void ResetEnvironment()
    {
        _switchButton.Reset();
        windowLampController.TurnRed();
        rollingShutter.SetBool("Open", false); 
        rollingShutter.SetTrigger("Reset");
        //_doorController.Reset();
        lever.Reset();
    }

    void ResetShiftData()
    {
        shiftStarted.Value = false;
    }

    private void ResetEverything()
    {
        ResetShiftData();
        ResetEnvironment();
        ResetSuspectsProcessed();
        PlayerInstance.Instance.SetPosition(PlayerSpawner.Instance.GetBoothSpawnPoint(PlayerInstance.Instance.OwnerClientId));
    }

    private IEnumerator NewShiftSequence()
    {
        _currentDay += 1;
        SuspectDatabase.Instance.AdvanceToDay(_currentDay);

        PlayerPrefs.SetInt("dayNumber", _currentDay);
        calendarText.text = _currentDay.ToString("D2");

        if (DebugConsole.Instance.skipInitialShiftTransition)
        {
            PlayerInstance.Instance.CanControl = true;
            PlayerInstance.Instance.SetCanInteract(true);
            PlayerInstance.Instance.SetCanMove(true);
            introCutscene.gameObject.SetActive(false);
            UIController.Instance.HideEndOfShiftReport();
            SuspectController.Instance.ResetSuspects();
            yield return new WaitForEndOfFrame();
            OnShiftReady?.Invoke();
            StartCoroutine(StartShiftSequence());
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
        StartCoroutine(StartShiftSequence());

        PlayerInstance.Instance.CanControl = true;
        PlayerInstance.Instance.SetCanInteract(true);
        PlayerInstance.Instance.SetCanMove(true);
        
        OnShiftReady?.Invoke();
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

    public void InitiateIntroCutscene()
    {
        UIController.Instance.FadeIn();
        PlayerInstance.Instance.DisableReticle();
        StartCoroutine(PlayIntroCutscene());
    }

    IEnumerator PlayIntroCutscene()
    {
        ambientAudio.DOFade(0, 2);
        yield return new WaitForSeconds(2f);
        ResetEverything();
        yield return new WaitForSeconds(1);
        ResetEverything(); // Did this twice because I was having a situation where player wasnt resetting properly
        introCutscene.gameObject.SetActive(true); 
        yield return new WaitForSeconds(.5f);
        UIController.Instance.FadeOut();
    }

    public void EndIntroCutscene()
    {
        StartNewShift();
        ambientAudio.DOFade(1, 2);
    }


}