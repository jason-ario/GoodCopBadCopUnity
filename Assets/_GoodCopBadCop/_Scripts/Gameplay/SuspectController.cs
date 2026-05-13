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

    private StampContainer.StampType _lastVerdictStampType;
    private bool _lastVerdictWasCorrect;
    
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

    [SerializeField] private DailySuspectManager dailySuspectManager;
    private void SpawnSuspectServer(int lineupIndex, Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;

        SuspectCharacter suspectPrefab = dailySuspectManager.shiftSuspects[lineupIndex].CharacterPrefab;
        
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

        // Notify all clients so they can show the booth-waiting notification if needed.
        NotifySuspectArrivingClientRpc();
    }

    /// <summary>
    /// Fires on every client when a new suspect begins walking to the window.
    /// Shows the booth-waiting notification only if the local player is away from the booth.
    /// Uses <see cref="PlayerInstance.IsOutsideLocal"/> to avoid a false negative caused
    /// by the NetworkVariable server round-trip not yet completing.
    /// </summary>
    [ClientRpc]
    private void NotifySuspectArrivingClientRpc()
    {
        if (PlayerInstance.Instance != null && PlayerInstance.Instance.IsOutsideLocal)
            UIController.Instance.ShowBoothWaitingNotification();
    }

    private void ArrivedAtPosition()
    {
        if (suspectCharacter == null) return;

        suspectCharacter.transform
            .DORotateQuaternion(standPos.rotation, 0.5f)
            .OnComplete(OnRotationComplete);

        suspectCharacter.animator.SetBool("Walking", false);
        EnableLook();
    }

    private void OnRotationComplete()
    {
        if (suspectCharacter == null) return;

        if (IsAnyPlayerInsideBooth())
            SayEntryDialogue();
        else
            StartCoroutine(WaitForPlayerInsideBooth());
    }

    /// <summary>
    /// Returns true if at least one connected player is currently inside the booth.
    /// Runs on the server, reading the replicated IsOutside NetworkVariable.
    /// </summary>
    private bool IsAnyPlayerInsideBooth()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            var player = client.PlayerObject.GetComponent<PlayerInstance>();
            if (player != null && !player.IsOutside)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Polls every half-second until at least one player is inside the booth,
    /// then triggers the entry dialogue.
    /// </summary>
    private IEnumerator WaitForPlayerInsideBooth()
    {
        while (!IsAnyPlayerInsideBooth())
            yield return new WaitForSeconds(0.5f);

        SayEntryDialogue();
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
        newIDCard.Spawn();
        newIDCard.GetComponent<IDCard>().SetInfo(suspectCharacter);
        spawnedDocuments.Add(newIDCard.GetComponent<PickableObject>());
            
        randomPos = Vector3.Lerp(documentSpawnStartPos.position, documentSpawnEndPos.position, UnityEngine.Random.Range(0,1));
        randomPos.y = documentSpawnEndPos.position.y;
        NetworkObject newApplicationForm = Instantiate(applicationForm, randomPos, Quaternion.identity) as NetworkObject;
        newApplicationForm.Spawn();
        newApplicationForm.GetComponent<ApplicationLetter>().SetInfo(suspectCharacter);
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

        
        if (IsServer) SayExitDialogue(thisCharacter, SuspectData.Verdict.Passed);

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


    /// <summary>
    /// Calculates payout values, credits the shared cash pool on the server, and
    /// broadcasts popup notifications to every connected client.
    /// Must only be called on the server.
    /// </summary>
    private void PayOutResults()
    {
        if (!IsServer) return;

        int correctAnomalyBonus = correctlyMarkedAnomalies * couponPerCorrectAnomaly;
        int missedAnomalyPenalty = (totalAnomaliesInLastSuspect - correctlyMarkedAnomalies) * couponPenaltyPerMissedAnomaly;
        int falsePositivePenalty = incorrectlyMarkedAnomalies * couponPenaltyPerFalsePositiveAnomaly;
        int perfectBonusAmount = (correctlyMarkedAnomalies == totalAnomaliesInLastSuspect && incorrectlyMarkedAnomalies == 0)
            ? couponPerfectAnomaliesBonus
            : 0;

        int verdictAmount = _lastVerdictWasCorrect ? couponCorrectVerdictBonus : -incorrectVerdictPenalty;
        int totalCoupons = correctAnomalyBonus - missedAnomalyPenalty - falsePositivePenalty + perfectBonusAmount + verdictAmount;

        int accuracyPercent = 100;
        if (totalAnomaliesInLastSuspect > 0 || incorrectlyMarkedAnomalies > 0)
        {
            int totalToIdentify = totalAnomaliesInLastSuspect + incorrectlyMarkedAnomalies;
            accuracyPercent = totalToIdentify > 0 ? (correctlyMarkedAnomalies * 100) / totalToIdentify : 100;
        }

        // Credit the shared pool — server-authoritative write.
        GlobalHostVariables.Instance.AddMoney(totalCoupons);

        Debug.Log(
            $"Payout — Correct: +{correctAnomalyBonus}, Missed: -{missedAnomalyPenalty}, False Positives: -{falsePositivePenalty}, Perfect Bonus: +{perfectBonusAmount}, Verdict: {verdictAmount}, Total: {totalCoupons}");

        // Broadcast popup sequence to all clients.
        ShowCashPopUpSequenceClientRpc(
            correctAnomalyBonus - missedAnomalyPenalty - falsePositivePenalty,
            accuracyPercent,
            correctlyMarkedAnomalies,
            totalAnomaliesInLastSuspect,
            verdictAmount,
            perfectBonusAmount,
            totalCoupons,
            _lastVerdictStampType,
            _lastVerdictWasCorrect);
    }

    [ClientRpc]
    private void ShowCashPopUpSequenceClientRpc(
        int anomalyAmount,
        int accuracyPercent,
        int correctCount,
        int totalCount,
        int verdictAmount,
        int perfectBonus,
        int totalCoupons,
        StampContainer.StampType stampType,
        bool verdictWasCorrect)
    {
        StartCoroutine(ShowCashPopUpSequence(anomalyAmount, accuracyPercent, correctCount, totalCount, verdictAmount, perfectBonus, totalCoupons, stampType, verdictWasCorrect));
    }

    private IEnumerator ShowCashPopUpSequence(
        int anomalyAmount,
        int accuracyPercent,
        int correctCount,
        int totalCount,
        int verdictAmount,
        int perfectBonus,
        int totalCoupons,
        StampContainer.StampType stampType,
        bool verdictWasCorrect)
    {
        yield return new WaitForSeconds(2f);

        // Message 1: Anomaly accuracy breakdown.
        string anomalyMessage = $"Anomalies Identified: {accuracyPercent}%\n({correctCount}/{totalCount} identified)";
        UIController.Instance.ShowCashPopUpNotification(anomalyAmount, anomalyMessage);

        yield return new WaitForSeconds(2f);

        // Message 2: Verdict bonus or penalty.
        UIController.Instance.ShowCashPopUpNotification(verdictAmount, GetVerdictMessage(stampType, verdictWasCorrect));

        yield return new WaitForSeconds(2f);

        // Message 3: Perfect identification bonus (if earned).
        if (perfectBonus > 0)
        {
            UIController.Instance.ShowCashPopUpNotification(perfectBonus, "Perfect Identification Bonus");
            yield return new WaitForSeconds(2f);
        }

        // Message 4: Total payout summary.
        UIController.Instance.ShowCashPopUpNotification(totalCoupons, "Payout Issued");
    }

    /// <summary>Builds a human-readable verdict result message for the cash popup.</summary>
    private string GetVerdictMessage(StampContainer.StampType stampType, bool wasCorrect)
    {
        string verdictLabel = stampType switch
        {
            StampContainer.StampType.Pass => "PASSED",
            StampContainer.StampType.Kill => "EXECUTED",
            StampContainer.StampType.Quarantine => "QUARANTINED",
            _ => "PROCESSED"
        };

        string outcomeLabel = wasCorrect ? "CORRECT" : "INCORRECT";
        return $"Verdict: {outcomeLabel} — {verdictLabel}";
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

        ShiftManager.Instance.QuarantinedSuspect(suspectCharacter);

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

        if (IsServer) DialogueManager.Instance.SayDialogue(suspectCharacter, "Wait... No... I'm healthy.. No!");

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

        if (IsServer) DialogueManager.Instance.SayDialogue(thisCharacter, "Wait... NO!!!");
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
        if (spawnedFolder == null) return;

        // Despawn documents first — they are not hierarchy children of the folder
        // (ParentConstraint tracks them without reparenting), so DespawnWithChildren
        // won't reach them. DespawnTrackedDocuments uses the server-authoritative list
        // populated via RegisterDocumentServerRpc, since InteractWithItem only fires
        // on the local client and never populates documents on the server.
        spawnedFolder.DespawnTrackedDocuments();

        if (spawnedFolder.IsSpawned)
        {
            NetworkHelper.DespawnWithChildren(spawnedFolder.GetComponent<NetworkObject>());
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

    /// <summary>
    /// Entry point called by any client when a stamped folder is handed off.
    /// Routes to the server for authoritative payout calculation and verdict execution.
    /// </summary>
    public void DeliverVerdict(FolderController folder)
    {
        SetCanInteract(false);
        folder.OnHandOff();

        if (IsServer)
        {
            ExecuteVerdict(folder);
        }
        else
        {
            DeliverVerdictServerRpc(folder.GetComponent<NetworkObject>().NetworkObjectId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void DeliverVerdictServerRpc(ulong folderNetworkObjectId)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(folderNetworkObjectId, out NetworkObject folderNetObj))
        {
            Debug.LogError($"DeliverVerdictServerRpc: could not find NetworkObject with id {folderNetworkObjectId}.");
            return;
        }

        FolderController folder = folderNetObj.GetComponent<FolderController>();
        if (folder == null)
        {
            Debug.LogError("DeliverVerdictServerRpc: NetworkObject does not have a FolderController.");
            return;
        }

        ExecuteVerdict(folder);
    }

    /// <summary>
    /// Performs the full verdict sequence: accuracy calculation, payout, and suspect processing.
    /// Must only be called on the server.
    /// </summary>
    private void ExecuteVerdict(FolderController folder)
    {
        if (!IsServer) return;

        spawnedFolder = folder;
        accuracyOfLastSuspectFolder = CalculatePercentAccuracy(folder, suspectCharacter);

        _lastVerdictStampType = folder.StampType;
        _lastVerdictWasCorrect = DetermineVerdictCorrectness(folder.StampType, suspectCharacter);

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

    /// <summary>
    /// Returns true when the given stamp verdict matches what should have been done for this suspect.
    /// Pass is correct for non-infected suspects; Kill is correct for infected suspects;
    /// Quarantine is always considered a neutral/safe choice and treated as correct.
    /// </summary>
    private bool DetermineVerdictCorrectness(StampContainer.StampType stampType, SuspectCharacter suspect)
    {
        bool isInfected = suspect != null && suspect.IsInfected;

        return stampType switch
        {
            StampContainer.StampType.Pass => !isInfected,
            StampContainer.StampType.Kill => isInfected,
            StampContainer.StampType.Quarantine => true,
            _ => false
        };
    }
    
}