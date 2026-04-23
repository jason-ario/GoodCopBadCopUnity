using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Serialization;

public class SuspectController : NetworkBehaviour
{
    public static SuspectController Instance;

    [Header("Spawn Points")]
    [SerializeField] private Transform spawnPos;
    [SerializeField] private Transform standPos;
    [SerializeField] private Transform despawnPos;
    [SerializeField] private Transform gatePos;

    [Header("Suspects")] 
    [SerializeField] private SuspectSet suspectList;
    private SuspectCharacter suspectCharacter;
    public SuspectCharacter CurrentSuspect => suspectCharacter;

    [Header("Paperwork")]
    [SerializeField] private List<PickableObject> spawnedDocuments = new List<PickableObject>();

    [SerializeField] private NetworkObject idCard;
    [SerializeField] private NetworkObject applicationForm;
    [SerializeField] private Transform documentSpawnStartPos;
    [SerializeField] private Transform documentSpawnEndPos;

    [Header("Quarantine")]
    [SerializeField] private PlayableDirector quarantineTimeline;
    [SerializeField] private Transform suspectQuarantineFollowPos;

    public NetworkVariable<int> suspectIndex = new NetworkVariable<int>(-1); 
    public int SuspectIndex => suspectIndex.Value;

    private ulong _currentSuspectNetworkObjectId = ulong.MaxValue;
    private bool _currentSuspectInitialized = false;
    FolderController spawnedFolder;

    public UnityAction OnTakeFolder;
    int accuracyOfLastSuspectFolder = 0;
    private int correctlyMarkedAnomalies = 0;
    private int totalAnomaliesInLastSuspect = 0;
    private int incorrectlyMarkedAnomalies = 0;
    
    [Header("Coupon Payouts")]
    [SerializeField] int couponCorrectVerdictBonus = 10;
    [SerializeField] int incorrectVerdictPenalty = 5;
    [SerializeField] int couponPerfectAnomaliesBonus = 5;
    [SerializeField] int couponPerCorrectAnomaly = 3;
    [SerializeField] int couponPenaltyPerMissedAnomaly = 2;
    [SerializeField] int couponPenaltyPerFalsePositiveAnomaly = 2;
    private void Awake()
    {
        Instance = this;
    }

    public void EnableLook()
    {
        if (suspectCharacter == null) return;
        if (suspectCharacter.lookAnimator == null) return;
        if (Camera.main == null) return;

        suspectCharacter.lookAnimator.ObjectToFollow = Camera.main.transform;
    }

    public void NextSuspect()
    {
        if (!IsServer) return;

        StartCoroutine(WaitAndSpawnNextSuspect());
    }

    IEnumerator WaitAndSpawnNextSuspect()
    {
        yield return new WaitForSeconds(3f);
        suspectIndex.Value += 1;
        
        SpawnSuspectServer(suspectIndex.Value, spawnPos.position, spawnPos.rotation);
    }

    private void SpawnSuspectServer(int lineupIndex, Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;

        SuspectCharacter suspectPrefab = GetRandomSuspect();
        if (suspectPrefab == null)
        {
            Debug.LogError($"Could not resolve suspect prefab from SuspectRecord at lineup index {lineupIndex}.");
            return;
        }

        GameObject spawnedSuspect = Instantiate(suspectPrefab.gameObject, position, rotation);
        NetworkObject netObj = spawnedSuspect.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError($"Spawned suspect prefab '{spawnedSuspect.name}' is missing a NetworkObject.");
            Destroy(spawnedSuspect);
            return;
        }

        netObj.Spawn();

        suspectCharacter = spawnedSuspect.GetComponent<SuspectCharacter>();
        suspectCharacter.Initialize();

        _currentSuspectNetworkObjectId = netObj.NetworkObjectId;
        _currentSuspectInitialized = false;

        TryInitializeCurrentSuspect();
        AssignReferencesClientRpc(netObj.NetworkObjectId);
    }

    private SuspectCharacter GetRandomSuspect()
    {
        return suspectList.suspects[UnityEngine.Random.Range(0, suspectList.suspects.Count - 1)].CharacterPrefab;
    }


    [ClientRpc]
    private void AssignReferencesClientRpc(ulong networkObjectId)
    {
        if (IsServer)
            return;

        StartCoroutine(WaitForSpawnAndAssign(networkObjectId));
    }

    private IEnumerator WaitForSpawnAndAssign(ulong networkObjectId)
    {
        while (NetworkManager.Singleton == null ||
               NetworkManager.Singleton.SpawnManager == null ||
               !NetworkManager.Singleton.SpawnManager.SpawnedObjects.ContainsKey(networkObjectId))
        {
            yield return null;
        }

        NetworkObject netObj = NetworkManager.Singleton.SpawnManager.SpawnedObjects[networkObjectId];
        suspectCharacter = netObj.GetComponent<SuspectCharacter>();
        _currentSuspectNetworkObjectId = networkObjectId;
    }

    private void TryInitializeCurrentSuspect()
    {
        if (!IsServer) return;
        if (suspectCharacter == null) return;

        NetworkObject netObj = suspectCharacter.GetComponent<NetworkObject>();
        if (netObj == null) return;

        if (_currentSuspectInitialized && netObj.NetworkObjectId == _currentSuspectNetworkObjectId)
            return;

        _currentSuspectNetworkObjectId = netObj.NetworkObjectId;
        _currentSuspectInitialized = true;

        InitiateSuspect();
    }

    private void InitiateSuspect()
    {
        if (suspectCharacter == null)
        {
            Debug.LogWarning("InitiateSuspect called with null suspectCharacter.");
            return;
        }

        suspectCharacter.animator.SetBool("Walking", true);
        suspectCharacter.transform
            .DOMove(standPos.position + suspectCharacter.standPosOffset, 3f)
            .OnComplete(ArrivedAtPosition);
    }

    private void ArrivedAtPosition()
    {
        if (suspectCharacter == null) return;

        suspectCharacter.transform
            .DORotateQuaternion(standPos.rotation, 0.5f)
            .OnComplete(SayEntryDialogue);

        suspectCharacter.animator.SetBool("Walking", false);
        EnableLook();
    }

    private void SayEntryDialogue()
    {
        if (suspectCharacter == null) return;

        if (suspectCharacter.attackImmediately)
        {
            suspectCharacter.AimAtPlayer();
            return;
        }

        string entryDialogue = suspectCharacter.GetEntryDialogue();
        DialogueManager.Instance.SayDialogue(suspectCharacter, entryDialogue);

        if (suspectCharacter.Data.GivesPaperwork)
        {
            suspectCharacter.GivePaperwork();
        }
    }

    public void SpawnPaperwork()
    {
        if (!IsServer) return;
        if (suspectCharacter == null) return;
        if (!suspectCharacter.Data.GivesPaperwork) return;
        
        Vector3 randomPos = Vector3.Lerp(documentSpawnStartPos.position, documentSpawnEndPos.position, UnityEngine.Random.Range(0,1));
        randomPos.y = documentSpawnEndPos.position.y;
        NetworkObject newIDCard = Instantiate(idCard, randomPos, Quaternion.identity) as NetworkObject;
        newIDCard.GetComponent<IDCard>().SetInfo(suspectCharacter);
        newIDCard.Spawn();
        spawnedDocuments.Add(newIDCard.GetComponent<PickableObject>());
            
        randomPos = Vector3.Lerp(documentSpawnStartPos.position, documentSpawnEndPos.position, UnityEngine.Random.Range(0,1));
        randomPos.y = documentSpawnEndPos.position.y;
        NetworkObject newApplicationForm = Instantiate(applicationForm, randomPos, Quaternion.identity) as NetworkObject;
        newApplicationForm.GetComponent<ApplicationLetter>().SetInfo(suspectCharacter);
        newApplicationForm.Spawn();
        spawnedDocuments.Add(newApplicationForm.GetComponent<PickableObject>());
    }

    public void RespondToDialogueChoice(int choiceIndex)
    {
        if (suspectCharacter == null) return;
        /*SuspectData.QuestionDialogueSet questionDialogueSet;
        int responseIndex = 0;
        
        if (choiceIndex == 0)
        {
            questionDialogueSet = suspectCharacter.Data.whereAreYouComingFromAnswers;
            responseIndex = suspectCharacter.ChosenEntryReasonIndex;
        } else if (choiceIndex == 1)
        {
            questionDialogueSet = suspectCharacter.Data.haveYouBeenExperiencingAnySymptomsAnswers;
            responseIndex = suspectCharacter.ChosenSymptomResponseIndex;
        }
        else
        {
            questionDialogueSet = suspectCharacter.Data.whoDoYouLiveWithAnswers;
            responseIndex = suspectCharacter.ChosenWhoDoYouLiveWithIndex;
        }
        
        string[] dialogueResponses;

        if (ShiftManager.Instance.IsEarlyDays)
        {
            dialogueResponses = questionDialogueSet.earlyDaysAnswers;
        }
        else if (ShiftManager.Instance.IsMidDays)
        {
            dialogueResponses = questionDialogueSet.midDaysAnswers;
        }
        else
        {
            dialogueResponses = questionDialogueSet.finalDaysAnswers;
        }
        
        DialogueManager.Instance.SayDialogue(suspectCharacter, dialogueResponses[responseIndex]);*/
    }
    
    public void Pass()
    {
        if (!IsServer) return;
        StartCoroutine(PassSequence());
    }

    [ClientRpc]
    private void PassVisualsClientRpc()
    {
        if (IsServer) return;
        StartCoroutine(PassSequence());
    }

    void SayExitDialogue(SuspectCharacter suspectCharacter, SuspectData.Verdict verdict)
    {
        string exitDialogue = "";
        string[] exitDialogues;

        switch (verdict)
        {
            case SuspectData.Verdict.Passed when ShiftManager.Instance.IsEarlyDays:
                exitDialogues = suspectCharacter.Data.exitDialoguesPassed.dialoguesEarlyDays;
                break;
            case SuspectData.Verdict.Passed when ShiftManager.Instance.IsMidDays:
                exitDialogues = suspectCharacter.Data.exitDialoguesPassed.dialoguesMidDays;
                break;
            case SuspectData.Verdict.Passed when ShiftManager.Instance.IsEndDays:
                exitDialogues = suspectCharacter.Data.exitDialoguesPassed.dialoguesFinalDays;
                break;
            case SuspectData.Verdict.Quarantined when ShiftManager.Instance.IsEarlyDays:
                exitDialogues = suspectCharacter.Data.exitDialoguesQuarantined.dialoguesEarlyDays;
                break;
            case SuspectData.Verdict.Quarantined when ShiftManager.Instance.IsMidDays:
                exitDialogues = suspectCharacter.Data.exitDialoguesQuarantined.dialoguesMidDays;
                break;
            case SuspectData.Verdict.Quarantined when ShiftManager.Instance.IsEndDays:
                exitDialogues = suspectCharacter.Data.exitDialoguesQuarantined.dialoguesFinalDays;
                break;
            case SuspectData.Verdict.Killed when ShiftManager.Instance.IsEarlyDays:
                exitDialogues = suspectCharacter.Data.exitDialoguesKilled.dialoguesEarlyDays;
                break;
            case SuspectData.Verdict.Killed when ShiftManager.Instance.IsMidDays:
                exitDialogues = suspectCharacter.Data.exitDialoguesKilled.dialoguesMidDays;
                break;
            case SuspectData.Verdict.Killed when ShiftManager.Instance.IsEndDays:
                exitDialogues = suspectCharacter.Data.exitDialoguesKilled.dialoguesFinalDays;
                break;
            default:
                exitDialogues = suspectCharacter.Data.exitDialoguesPassed.dialoguesEarlyDays;
                break;
        }
        
        exitDialogue = exitDialogues[UnityEngine.Random.Range(0, exitDialogues.Length)];
        DialogueManager.Instance.SayDialogue(suspectCharacter, exitDialogue);
    }

    private IEnumerator PassSequence()
    {
        if (suspectCharacter == null)
            yield break;

        ShiftManager.Instance.PassedSuspect(suspectCharacter);

        SuspectCharacter thisCharacter = suspectCharacter;

        thisCharacter.animator.SetTrigger("Give");
        yield return new WaitForSeconds(1f);

        if (IsServer)
        {
            CleanupSpawnedFolder();
            PassVisualsClientRpc();
        }

        
        SayExitDialogue(thisCharacter, SuspectData.Verdict.Passed);

        yield return new WaitForSeconds(2f);

        if (thisCharacter.lookAnimator != null)
        {
            thisCharacter.lookAnimator.SetLookTarget(null);
        }

        thisCharacter.transform.DORotate(gatePos.rotation.eulerAngles, 0.5f);
        yield return new WaitForSeconds(0.5f);

        thisCharacter.animator.SetBool("Walking", true);
        thisCharacter.transform.DOMove(gatePos.position, 4f);
        yield return new WaitForSeconds(4f);
        thisCharacter.animator.SetBool("Walking", false);

        if (IsServer)
        {
            GameManager.Instance.GateController.OpenGate();
            ShiftManager.Instance.SetNextSuspectReady();
        }

        yield return new WaitForSeconds(2f);

        thisCharacter.animator.SetBool("Walking", true);
        thisCharacter.transform.DOMove(despawnPos.position, 10f).OnComplete(() =>
        {
            if (IsServer) DespawnSuspect(thisCharacter);
        });

        
        yield return new WaitForSeconds(2f);

        if (IsServer)
        {
            GameManager.Instance.GateController.CloseGate();
        }
    }

    public int CalculatePercentAccuracy(FolderController folder, SuspectCharacter suspectCharacter)
    {
        // Get actual anomalies and marked anomalies
        List<Anomaly> actualAnomalies = suspectCharacter.AnomalyController.activeAnomalies;
        Anomaly[] markedAnomalies = folder.GetAnomaliesInFolder();
        totalAnomaliesInLastSuspect = actualAnomalies.Count;
        // Count correctly identified anomalies
        correctlyMarkedAnomalies = 0;
        foreach (var anomaly in actualAnomalies)
        {
            if (folder.ExamContainsAnomaly(anomaly))
            {
                correctlyMarkedAnomalies += 1;
            }
        }
        
        // Count incorrectly marked anomalies (false positives)
        incorrectlyMarkedAnomalies = 0;
        foreach (var anomaly in markedAnomalies)
        {
            if (!actualAnomalies.Contains(anomaly))
            {
                incorrectlyMarkedAnomalies += 1;
            }
        }
        
        // Calculate total possible anomalies to check
        int totalPossibleAnomalies = actualAnomalies.Count + incorrectlyMarkedAnomalies;
        
        // Avoid division by zero
        if (totalPossibleAnomalies == 0)
        {
            return 100; // Perfect score if no anomalies exist
        }
        
        // Accuracy = (Correct - Incorrect) / Total
        // This penalizes false positives while rewarding correct identifications
        int accuracy = Mathf.Max(0, ((correctlyMarkedAnomalies - incorrectlyMarkedAnomalies) * 100) / totalPossibleAnomalies);
        
        return accuracy;
    }


    private void PayOutResults()
    {
        StartCoroutine(ShowCashPopUpSequence());
    }
    
    IEnumerator ShowCashPopUpSequence()
    {
        // Calculate coupon payouts
        int correctAnomalyBonus = correctlyMarkedAnomalies * couponPerCorrectAnomaly;
        int missedAnomalyPenalty =
            (totalAnomaliesInLastSuspect - correctlyMarkedAnomalies) * couponPenaltyPerMissedAnomaly;
        int falsePositivePenalty = incorrectlyMarkedAnomalies * couponPenaltyPerFalsePositiveAnomaly;
        int perfectBonusAmount =
            (correctlyMarkedAnomalies == totalAnomaliesInLastSuspect && incorrectlyMarkedAnomalies == 0)
                ? couponPerfectAnomaliesBonus
                : 0;

        int totalCoupons = correctAnomalyBonus - missedAnomalyPenalty - falsePositivePenalty + perfectBonusAmount +
                           couponCorrectVerdictBonus;

    // Calculate accuracy percentage
        int accuracyPercent = 100;
        if (totalAnomaliesInLastSuspect > 0 || incorrectlyMarkedAnomalies > 0)
        {
            int totalToIdentify = totalAnomaliesInLastSuspect + incorrectlyMarkedAnomalies;
            accuracyPercent = totalToIdentify > 0 ? (correctlyMarkedAnomalies * 100) / totalToIdentify : 100;
        }

        yield return new WaitForSeconds(2f);
        
        // Add money to player account
        GlobalHostVariables.Instance.AddMoney(totalCoupons);

        // Message 1: Anomalies Identified with accuracy percentage and breakdown
        string anomalyMessage =
            $"Anomalies Identified: {accuracyPercent}%\n({correctlyMarkedAnomalies}/{totalAnomaliesInLastSuspect} correct)";
        int anomalyAmount = correctAnomalyBonus - missedAnomalyPenalty - falsePositivePenalty;
        UIController.Instance.ShowCashPopUpNotification(anomalyAmount, anomalyMessage);

        yield return new WaitForSeconds(2f);

        // Message 2: Verdict message (you'll need to determine the verdict type)
        string verdictMessage = GetVerdictMessage(); // Implement based on your verdict logic
        UIController.Instance.ShowCashPopUpNotification(couponCorrectVerdictBonus, verdictMessage);

        yield return new WaitForSeconds(2f);

        // Message 3: Perfect bonus (if applicable)
        if (perfectBonusAmount > 0)
        {
            UIController.Instance.ShowCashPopUpNotification(perfectBonusAmount, "Perfect Identification Bonus");
        }

        yield return new WaitForSeconds(2f);

        // Message 4: Final payout
        UIController.Instance.ShowCashPopUpNotification(totalCoupons, "Payout Issued");



        // Debug log for verification
        Debug.Log(
            $"Anomaly Correct: +{correctAnomalyBonus}, Missed: -{missedAnomalyPenalty}, False Positive: -{falsePositivePenalty}, Perfect Bonus: +{perfectBonusAmount}, Verdict Bonus: +{couponCorrectVerdictBonus}, Total: {totalCoupons}");
    }

    private string GetVerdictMessage()
    {
        // Implement this based on your verdict logic
        // For now, returning a placeholder - adjust based on how you determine verdict
        return "Verdict: CIVILIAN CORRECTLY QUARANTINED";
    }
    
    public void Quarantine()
    {
        if (!IsServer) return;
        StartCoroutine(QuarantineSequence());
    }

    [ClientRpc]
    private void QuarantineVisualsClientRpc()
    {
        if (IsServer) return;
        StartCoroutine(QuarantineSequence());
    }

    private IEnumerator QuarantineSequence()
    {
        if (suspectCharacter == null)
            yield break;

        ShiftManager.Instance.QuarantinedSuspect();

        suspectCharacter.animator.SetTrigger("Give");
        yield return new WaitForSeconds(1f);

        if (IsServer)
        {
            CleanupSpawnedFolder();
            QuarantineVisualsClientRpc();
        }

        yield return new WaitForSeconds(2f);
        suspectCharacter.animator.SetTrigger("Shocked");

        if (quarantineTimeline != null)
        {
            quarantineTimeline.gameObject.SetActive(true);
            quarantineTimeline.Play();
        }

        DialogueManager.Instance.SayDialogue(suspectCharacter, "Wait... No... I'm healthy.. No!");

        yield return new WaitForSeconds(2f);

        if (suspectCharacter.lookAnimator != null)
        {
            suspectCharacter.lookAnimator.SetLookTarget(null);
        }

        suspectCharacter.animator.SetBool("BeingRestrained", true);

        float quarantiningTime = 9f;
        float timeElapsed = 0f;

        while (timeElapsed < quarantiningTime)
        {
            yield return new WaitForEndOfFrame();

            if (suspectCharacter == null)
                yield break;

            suspectCharacter.transform.position = suspectQuarantineFollowPos.position;
            suspectCharacter.transform.rotation = suspectQuarantineFollowPos.rotation;
            timeElapsed += Time.deltaTime;
        }

        if (IsServer)
        {
            DespawnSuspect(suspectCharacter);
            ShiftManager.Instance.SetNextSuspectReady();
        }

        if (quarantineTimeline != null)
        {
            quarantineTimeline.gameObject.SetActive(false);
        }
    }

    public void Kill()
    {
        if (!IsServer) return;
        StartCoroutine(KillSequence());
    }

    private IEnumerator KillSequence()
    {
        if (suspectCharacter == null)
            yield break;

        ShiftManager.Instance.KillSuspect(suspectCharacter);

        yield return new WaitForSeconds(1f);
        SuspectCharacter thisCharacter = suspectCharacter;

        thisCharacter.animator.SetTrigger("Give");
        yield return new WaitForSeconds(1f);

        if (IsServer)
        {
            CleanupSpawnedFolder();
        }

        yield return new WaitForSeconds(1f);

        DialogueManager.Instance.SayDialogue(thisCharacter, "Wait... NO!!!");
        thisCharacter.animator.SetTrigger("ShotUp");
        yield return new WaitForSeconds(1f);

        KillMachineController.Instance.Kill();

        yield return new WaitForSeconds(8f);

        if (IsServer)
        {
            DespawnSuspect(thisCharacter);
            ShiftManager.Instance.SetNextSuspectReady();
        }
    }

    public void SetCanInteract(bool canInteract)
    {
        if (suspectCharacter == null) return;
        suspectCharacter.SetCanInteract(canInteract);
    }

    public void GrabSuspect()
    {
        if (suspectCharacter == null) return;
        suspectCharacter.animator.SetBool("Restrained", true);
    }

    private void CleanupSpawnedFolder()
    {
        if (spawnedFolder != null && spawnedFolder.IsSpawned)
        {
            NetworkHelper.DespawnWithChildren(spawnedFolder.GetComponent<NetworkObject>());
        }

        foreach (PickableObject pickableObject in spawnedFolder.documents)
        {
            NetworkHelper.Despawn(pickableObject.GetComponent<NetworkObject>());
        }

        spawnedFolder = null;
        OnTakeFolder?.Invoke();
    }

    private void DespawnSuspect(SuspectCharacter suspectToDespawn)
    {
        if (suspectToDespawn == null) return;

        NetworkObject netObj = suspectToDespawn.GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
        {
            if (netObj.NetworkObjectId == _currentSuspectNetworkObjectId)
            {
                _currentSuspectInitialized = false;
                _currentSuspectNetworkObjectId = ulong.MaxValue;
            }

            netObj.Despawn();
        }
        else
        {
            Destroy(suspectToDespawn.gameObject);
        }

        if (suspectCharacter == suspectToDespawn)
        {
            suspectCharacter = null;
        }
    }

    public void ResetSuspects()
    {
        suspectIndex.Value = -1;
        _currentSuspectInitialized = false;
        _currentSuspectNetworkObjectId = ulong.MaxValue;
        suspectCharacter = null;
        spawnedFolder = null;
    }

    public void DeliverVerdict(FolderController folder)
    {
        SetCanInteract(false);
        spawnedFolder = folder;
        folder.OnHandOff();
        accuracyOfLastSuspectFolder = CalculatePercentAccuracy(folder, suspectCharacter); 
        PayOutResults();

        switch (folder.StampType)
        {
            case StampContainer.StampType.Pass:
                Pass();
                break;
            case StampContainer.StampType.Quarantine:
                Quarantine();
                break;
            case StampContainer.StampType.Kill:
                Kill();
                break;
        }
    }
    
}