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

    /// <summary>
    /// When true, the next suspect to spawn is initialized with no anomalies.
    /// The flag is consumed and reset automatically after use. Set by Day_01.
    /// </summary>
    public static bool ForceNextSuspectClean = false;

    /// <summary>
    /// When >= 0, the next suspect to spawn is initialized with exactly this many anomalies,
    /// bypassing the clean-chance roll. The flag is consumed and reset to -1 after use.
    /// Ignored when <see cref="ForceNextSuspectClean"/> is true. Set by Day_01.
    /// </summary>
    public static int ForceNextSuspectAnomalyCount = -1;

    /// <summary>
    /// When true, the next suspect to spawn is forced drunk regardless of their drunkChance.
    /// The flag is consumed and reset automatically by DrunkBehaviour.TryActivate().
    /// </summary>
    public static bool ForceNextSuspectDrunk = false;

    /// <summary>
    /// When true, the next suspect slot spawns as a mutant intruder regardless of spawn chance.
    /// The flag is consumed and reset automatically after use. Set by DebugConsole (F3).
    /// </summary>
    public static bool ForceNextSuspectMutant = false;

    /// <summary>
    /// Optional server-side intercept for the next suspect spawn. When set, this is invoked
    /// instead of spawning a normal or mutant suspect for that slot. Consumed and reset to null
    /// after one use. Set by day-specific controllers (e.g. Day_01 for the Alexei scripted event).
    /// </summary>
    public static System.Action InterceptNextSuspectSpawn;

    [Header("Booth")]
    [SerializeField] private ShutterController shutterController;

    [Header("Spawn Points")]
    [SerializeField] private Transform spawnPos;
    [SerializeField] private Transform standPos;
    [SerializeField] private Transform despawnPos;
    [SerializeField] private Transform gatePos;

    [Header("Suspects")] 
    private SuspectCharacter suspectCharacter;
    public SuspectCharacter CurrentSuspect => suspectCharacter;

    private MutantSuspectBehaviour _currentMutant;

    /// <summary>True when a regular suspect or a mutant intruder is currently at the booth window.</summary>
    public bool HasEntityAtWindow => suspectCharacter != null || _currentMutant != null;

    [Header("Paperwork")]
    [SerializeField] private List<PickableObject> spawnedDocuments = new List<PickableObject>();

    /// <summary>Read-only view of all documents currently on the desk for this suspect.</summary>
    public IReadOnlyList<PickableObject> SpawnedDocuments => spawnedDocuments;

    [SerializeField] private NetworkObject idCard;
    [SerializeField] private NetworkObject applicationForm;
    [SerializeField] private Transform documentSpawnStartPos;
    [SerializeField] private Transform documentSpawnEndPos;

    [Header("Quarantine")]
    [SerializeField] private PlayableDirector quarantineTimeline;
    [SerializeField] private Transform suspectQuarantineFollowPos;

    [Header("Suspect Arrival Cam")]
    [SerializeField] private GameObject suspectCam;
    private const float SuspectCamDuration = 3f;

    [Header("Mutant Intruder")]
    [Tooltip("Position on the player's side of the booth window. The mutant DOTweens here after a successful breakthrough.")]
    [SerializeField] private Transform climbThroughTargetPos;

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
    [SerializeField] int couponPerfectAnomaliesBonus = 5;
    [SerializeField] int couponPerCorrectAnomaly = 3;
    [SerializeField] int couponPenaltyPerMissedAnomaly = 2;
    [SerializeField] int couponPenaltyPerFalsePositiveAnomaly = 2;
    /// <summary>Base reward scaled by accuracy percentage. Guarantees a payout at 100% even with 0 anomalies.</summary>
    [SerializeField] int couponBaseReward = 5;
    
    private void Awake()
    {
        Instance = this;

        if (suspectCam != null)
            suspectCam.SetActive(false);
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

        if (ForceNextSuspectMutant)
        {
            ForceNextSuspectMutant = false;
            if (dailySuspectManager.TryGetRandomMutant(out MutantSuspectBehaviour forcedPrefab, out MutantIntruderData forcedData))
            {
                SpawnMutantIntruderServer(spawnPos.position, spawnPos.rotation, forcedPrefab, forcedData);
                yield break;
            }
            Debug.LogWarning("[SuspectController] ForceNextSuspectMutant: no mutant available in pool — falling back to normal suspect.");
        }

        // Check for a scripted event intercept (e.g. Alexei on Day 1).
        // Consumed before the mutant/regular spawn so no character spawns for this slot.
        if (InterceptNextSuspectSpawn != null)
        {
            var intercept = InterceptNextSuspectSpawn;
            InterceptNextSuspectSpawn = null;
            Debug.Log($"[SuspectController] Intercepting suspect spawn at index {suspectIndex.Value} — scripted event.");
            intercept.Invoke();
            yield break;
        }

        if (dailySuspectManager.IsMutantSlot(suspectIndex.Value, out MutantSuspectBehaviour mutantPrefab, out MutantIntruderData mutantData))
            SpawnMutantIntruderServer(spawnPos.position, spawnPos.rotation, mutantPrefab, mutantData);
        else if (dailySuspectManager.IsDoppelgangerSlot(suspectIndex.Value, out DoppelgangerData doppelgangerData))
            SpawnDoppelgangerServer(suspectIndex.Value, spawnPos.position, spawnPos.rotation, doppelgangerData);
        else
            SpawnSuspectServer(suspectIndex.Value, spawnPos.position, spawnPos.rotation);
    }

    [SerializeField] private DailySuspectManager dailySuspectManager;
    private void SpawnSuspectServer(int lineupIndex, Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;

        SuspectData suspectData = dailySuspectManager.shiftSuspects[lineupIndex];
        if (suspectData == null)
        {
            Debug.LogError($"[SuspectController] Null SuspectData at lineup index {lineupIndex} — expected a mutant slot branch.");
            return;
        }

        SuspectCharacter suspectPrefab = suspectData.CharacterPrefab;
        
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

        if (ForceNextSuspectClean)
        {
            ForceNextSuspectClean = false;
            suspectCharacter.InitializeClean();
        }
        else if (ForceNextSuspectAnomalyCount >= 0)
        {
            int count = ForceNextSuspectAnomalyCount;
            ForceNextSuspectAnomalyCount = -1;
            suspectCharacter.InitializeWithExactAnomalyCount(count);
        }
        else
        {
            suspectCharacter.Initialize();
        }

        _currentSuspectNetworkObjectId = netObj.NetworkObjectId;
        _currentSuspectInitialized = false;

        TryInitializeCurrentSuspect();
        AssignReferencesClientRpc(netObj.NetworkObjectId);
    }

    /// <summary>
    /// Spawns a doppelganger using the target suspect's prefab and flags it via
    /// <see cref="SuspectCharacter.InitializeAsDoppelganger"/>. The prefab is the same
    /// as a normal civilian — doppelganger identity is carried by the DoppelgangerData.
    /// </summary>
    private void SpawnDoppelgangerServer(int lineupIndex, Vector3 position, Quaternion rotation, DoppelgangerData doppelgangerData)
    {
        if (!IsServer) return;

        SuspectData targetData = doppelgangerData.targetSuspect;
        SuspectCharacter suspectPrefab = targetData.CharacterPrefab;

        if (suspectPrefab == null)
        {
            Debug.LogError($"[SuspectController] DoppelgangerData '{doppelgangerData.name}' targetSuspect has no CharacterPrefab — cannot spawn doppelganger at lineup index {lineupIndex}.");
            return;
        }

        GameObject spawnedSuspect = Instantiate(suspectPrefab.gameObject, position, rotation);
        NetworkObject netObj = spawnedSuspect.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError($"[SuspectController] Doppelganger prefab '{spawnedSuspect.name}' is missing a NetworkObject.");
            Destroy(spawnedSuspect);
            return;
        }

        netObj.Spawn();

        suspectCharacter = spawnedSuspect.GetComponent<SuspectCharacter>();
        suspectCharacter.InitializeAsDoppelganger(doppelgangerData);

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
    /// Fired on all clients after paperwork lands on the desk.
    /// Carries the spawned IDCard and the application form PickableObject so tutorial
    /// systems can reference both documents without reading the server-only SpawnedDocuments list.
    /// </summary>
    public static event Action<IDCard, PickableObject> OnPaperworkSpawned;

    /// <summary>
    /// Fired on all clients when a suspect finishes walking to the booth window.
    /// Carries the suspect index so listeners can distinguish first vs. subsequent suspects.
    /// </summary>
    public static event Action<int> OnSuspectArrived;

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

        // Ensure all non-activated anomalies (including those from locked categories that
        // were skipped during the initial activation pass) have their shader state cleaned up.
        suspectCharacter.InitializeDisabledOnArrival();

        // Broadcast arrival to all clients so tutorial systems can react locally.
        NotifySuspectArrivedClientRpc(suspectIndex.Value);

        suspectCharacter.transform
            .DORotateQuaternion(standPos.rotation, 0.5f)
            .OnComplete(OnRotationComplete);

        suspectCharacter.animator.SetBool("Walking", false);
        EnableLook();
    }

    [ClientRpc]
    private void NotifySuspectArrivedClientRpc(int index)
    {
        OnSuspectArrived?.Invoke(index);
    }

    private void OnRotationComplete()
    {
        if (suspectCharacter == null) return;

        if (IsAnyPlayerInsideBooth() && IsShutterOpen())
            SayEntryDialogue();
        else
            StartCoroutine(WaitForBoothReady());
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

    /// <summary>Returns true while the booth window shutter is open.</summary>
    private bool IsShutterOpen()
    {
        return shutterController != null && shutterController.IsOpen;
    }

    /// <summary>
    /// Polls every half-second until at least one player is inside the booth
    /// and the shutter is open, then triggers the entry dialogue.
    /// </summary>
    private IEnumerator WaitForBoothReady()
    {
        while (!IsAnyPlayerInsideBooth() || !IsShutterOpen())
            yield return new WaitForSeconds(0.5f);

        SayEntryDialogue();
    }

    private void SayEntryDialogue()
    {
        if (suspectCharacter == null) return;

        /*
        if (suspectCharacter.attackImmediately)
        {
            suspectCharacter.AimAtPlayer();
            return;
        }*/

        string entryDialogue = suspectCharacter.GetEntryDialogue();
        DialogueManager.Instance.SayDialogue(suspectCharacter, entryDialogue);

        suspectCharacter.GetComponent<SuspectBarkController>()?.BeginBarkSchedule();

        StartCoroutine(SuspectCamSequence());

        if (suspectCharacter.Data.GivesPaperwork)
        {
            suspectCharacter.GivePaperwork();
        }
    }

    /// <summary>
    /// Activates the suspect arrival cam for <see cref="SuspectCamDuration"/> seconds on any client
    /// whose local player is currently inside the booth. Booth-inside players are also made
    /// invincible on the server for the duration so neither health nor radiation can change.
    /// Only runs on the server.
    /// </summary>
    private IEnumerator SuspectCamSequence()
    {
        var boothPlayers = GetBoothInsidePlayers();

        foreach (var player in boothPlayers)
        {
            if (player.PlayerHealth != null) player.PlayerHealth.IsInvincible = true;
            if (player.PlayerRadiation != null) player.PlayerRadiation.IsInvincible = true;
        }

        ToggleSuspectCamClientRpc(true);

        yield return new WaitForSeconds(SuspectCamDuration);

        ToggleSuspectCamClientRpc(false);

        foreach (var player in boothPlayers)
        {
            if (player.PlayerHealth != null) player.PlayerHealth.IsInvincible = false;
            if (player.PlayerRadiation != null) player.PlayerRadiation.IsInvincible = false;
        }
    }

    /// <summary>Returns all connected players currently inside the booth.</summary>
    private List<PlayerInstance> GetBoothInsidePlayers()
    {
        var result = new List<PlayerInstance>();

        if (NetworkManager.Singleton == null)
            return result;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            var player = client.PlayerObject.GetComponent<PlayerInstance>();
            if (player != null && !player.IsOutside)
                result.Add(player);
        }

        return result;
    }

    /// <summary>Toggles the suspect arrival cam on clients where the local player is in the booth.</summary>
    [ClientRpc]
    private void ToggleSuspectCamClientRpc(bool active)
    {
        // Don't deactivate the cam if dialogue mode is currently holding it open.
        if (!active && DialogueChoiceSystem.IsInDialogueMode)
            return;

        if (active)
        {
            // Only show the suspect cam sequence for players currently in the booth.
            if (PlayerInstance.Instance == null || PlayerInstance.Instance.IsOutsideLocal)
                return;

            suspectCam?.SetActive(true);
            PlayerTutorialUI.Instance?.ShowBarsOnly(SuspectCamDuration);
            PlayerInstance.Instance.GetComponent<PlayerInteractionController>()?.SetSuspectCamMode(true);
        }
        else
        {
            // Always clean up cam and bars — player may have moved outside during the sequence.
            suspectCam?.SetActive(false);
            PlayerTutorialUI.Instance?.Dismiss();

            // Restore player-specific state only if still a valid local in-booth player.
            if (PlayerInstance.Instance != null && !PlayerInstance.Instance.IsOutsideLocal)
                PlayerInstance.Instance.GetComponent<PlayerInteractionController>()?.SetSuspectCamMode(false);
        }
    }

    /// <summary>
    /// Activates or deactivates the suspect cam for the local client.
    /// Called by <see cref="DialogueChoiceSystem"/> when entering or exiting dialogue mode.
    /// </summary>
    public void SetSuspectCamActive(bool active)
    {
        if (suspectCam != null)
            suspectCam.SetActive(active);

        if (!active)
            PlayerTutorialUI.Instance?.Dismiss();
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

        NotifyPaperworkSpawnedClientRpc(
            new NetworkObjectReference(newIDCard),
            new NetworkObjectReference(newApplicationForm));
    }

    /// <summary>
    /// Broadcasts to all clients that paperwork has landed on the desk.
    /// Passes references to both documents so tutorial systems can lock/unlock them locally.
    /// </summary>
    [ClientRpc]
    private void NotifyPaperworkSpawnedClientRpc(NetworkObjectReference idCardRef, NetworkObjectReference appFormRef)
    {
        if (!idCardRef.TryGet(out NetworkObject idCardObj)) return;

        IDCard card = idCardObj.GetComponent<IDCard>();

        PickableObject appForm = null;
        if (appFormRef.TryGet(out NetworkObject appFormObj))
            appForm = appFormObj.GetComponent<PickableObject>();

        OnPaperworkSpawned?.Invoke(card, appForm);
    }

    public void RespondToDialogueChoice(int choiceIndex)
    {
        if (suspectCharacter == null) return;

        string response = suspectCharacter.GetQuestionResponse(choiceIndex);
        if (response == null) return;

        DialogueManager.Instance.SayDialogue(suspectCharacter, response);
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

    /// <summary>
    /// Picks and speaks a randomised exit line for the given verdict.
    /// For Quarantined and Killed, pulls from the flat random-pool fields on SuspectData
    /// when they are populated, falling back to the day-range arrays otherwise.
    /// </summary>
    void SayExitDialogue(SuspectCharacter suspectCharacter, SuspectData.Verdict verdict)
    {
        string exitDialogue = "";
        string[] exitDialogues;

        if (verdict == SuspectData.Verdict.Quarantined && suspectCharacter.Data.quarantineExitLines?.Length > 0)
        {
            exitDialogues = suspectCharacter.Data.quarantineExitLines;
        }
        else if (verdict == SuspectData.Verdict.Killed && suspectCharacter.Data.killExitLines?.Length > 0)
        {
            exitDialogues = suspectCharacter.Data.killExitLines;
        }
        else
        {
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
    /// Calculates payout values based on percentage accuracy and anomaly count, credits the shared
    /// cash pool on the server, and broadcasts popup notifications to every connected client.
    /// Must only be called on the server.
    /// </summary>
    private void PayOutResults()
    {
        if (!IsServer) return;

        // Accuracy: 0/0 is treated as 100%. False positives widen the denominator, reducing the score.
        int totalPossible = totalAnomaliesInLastSuspect + incorrectlyMarkedAnomalies;
        int accuracyPercent = totalPossible > 0
            ? Mathf.Max(0, (correctlyMarkedAnomalies * 100) / totalPossible)
            : 100;

        // Base reward scaled linearly by accuracy — ensures a positive payout at 100% even when
        // there are 0 anomalies (e.g. clean suspect correctly passed through).
        int percentageReward = Mathf.RoundToInt(accuracyPercent / 100f * couponBaseReward);

        // Anomaly booster — each correctly identified anomaly contributes extra coupons,
        // so suspects with many anomalies yield a higher potential reward ceiling.
        int anomalyBooster = correctlyMarkedAnomalies * couponPerCorrectAnomaly;

        // Penalties for missed anomalies and false positives.
        int missedAnomalyPenalty = (totalAnomaliesInLastSuspect - correctlyMarkedAnomalies) * couponPenaltyPerMissedAnomaly;
        int falsePositivePenalty = incorrectlyMarkedAnomalies * couponPenaltyPerFalsePositiveAnomaly;

        // Combined anomaly payout shown in the first popup: percentage base + booster – penalties.
        int anomalyPayout = percentageReward + anomalyBooster - missedAnomalyPenalty - falsePositivePenalty;

        // Perfect identification bonus — only awarded when every anomaly is found and no false positives exist.
        int perfectBonusAmount = (correctlyMarkedAnomalies == totalAnomaliesInLastSuspect && incorrectlyMarkedAnomalies == 0)
            ? couponPerfectAnomaliesBonus
            : 0;

        int totalCoupons = anomalyPayout + perfectBonusAmount;

        // Credit the shared pool — server-authoritative write.
        GlobalHostVariables.Instance.AddMoney(totalCoupons);

        Debug.Log(
            $"Payout — Accuracy: {accuracyPercent}%, Base%: +{percentageReward}, Anomaly Booster: +{anomalyBooster}, Missed: -{missedAnomalyPenalty}, False Positives: -{falsePositivePenalty}, Perfect Bonus: +{perfectBonusAmount}, Total: {totalCoupons}");

        // Broadcast popup sequence to all clients.
        ShowCashPopUpSequenceClientRpc(
            anomalyPayout,
            accuracyPercent,
            correctlyMarkedAnomalies,
            totalAnomaliesInLastSuspect,
            perfectBonusAmount,
            totalCoupons);
    }

    [ClientRpc]
    private void ShowCashPopUpSequenceClientRpc(
        int anomalyAmount,
        int accuracyPercent,
        int correctCount,
        int totalCount,
        int perfectBonus,
        int totalCoupons)
    {
        StartCoroutine(ShowCashPopUpSequence(anomalyAmount, accuracyPercent, correctCount, totalCount, perfectBonus, totalCoupons));
    }

    private IEnumerator ShowCashPopUpSequence(
        int anomalyAmount,
        int accuracyPercent,
        int correctCount,
        int totalCount,
        int perfectBonus,
        int totalCoupons)
    {
        yield return new WaitForSeconds(2f);

        // Message 1: Anomaly accuracy breakdown.
        string anomalyMessage = $"Anomalies Identified: {accuracyPercent}%\n({correctCount}/{totalCount} identified)";
        UIController.Instance.ShowCashPopUpNotification(anomalyAmount, anomalyMessage);

        yield return new WaitForSeconds(2f);

        // Message 2: Perfect identification bonus (if earned).
        if (perfectBonus > 0)
        {
            UIController.Instance.ShowCashPopUpNotification(perfectBonus, "Perfect Identification Bonus");
            yield return new WaitForSeconds(2f);
        }

        // Message 3: Total payout summary.
        UIController.Instance.ShowCashPopUpNotification(totalCoupons, "Payout Issued");
    }
    
    public void Quarantine()
    {
        if (!IsServer) return;
        StartCoroutine(QuarantineSequence());
    }

    [ClientRpc]
    private void QuarantineVisualsClientRpc(double serverTimeAtTimelineStart)
    {
        if (IsServer) return;
        StartCoroutine(QuarantineSequence(serverTimeAtTimelineStart));
    }

    private IEnumerator QuarantineSequence(double serverTimeAtTimelineStart = -1)
    {
        if (suspectCharacter == null)
            yield break;

        bool isClient = !IsServer;

        ShiftManager.Instance.QuarantinedSuspect(suspectCharacter);

        if (!isClient)
        {
            suspectCharacter.animator.SetTrigger("Give");
            yield return new WaitForSeconds(1f);

            CleanupSpawnedFolder();

            // Stamp the server time right before the shared visual phase begins,
            // so the client can seek the timeline to the correct playhead position.
            QuarantineVisualsClientRpc(NetworkManager.Singleton.ServerTime.Time);
        }

        yield return new WaitForSeconds(2f);
        suspectCharacter.animator.SetTrigger("Shocked");

        if (quarantineTimeline != null)
        {
            quarantineTimeline.gameObject.SetActive(true);
            quarantineTimeline.Play();

            // Seek the client's timeline forward to compensate for network latency only.
            // Both sides wait the same 2 seconds after the RPC, so we subtract that shared
            // delay — leaving just the one-way network latency as the seek offset.
            if (isClient && serverTimeAtTimelineStart > 0)
            {
                const double sharedDelaySeconds = 2.0;
                double elapsed = NetworkManager.Singleton.ServerTime.Time - serverTimeAtTimelineStart - sharedDelaySeconds;
                if (elapsed > 0)
                {
                    quarantineTimeline.time = elapsed;
                    quarantineTimeline.Evaluate();
                }
            }
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
                break;

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

    /// <summary>Triggers the kill machine sequence on all clients simultaneously with the server.</summary>
    [ClientRpc]
    private void KillVisualsClientRpc()
    {
        if (IsServer) return;
        KillMachineController.Instance.Kill();
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

        KillVisualsClientRpc();
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

    // ── Mutant Intruder ────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a mutant from the lineup pool and starts its booth-approach sequence.
    /// Called on the server when the current lineup slot is a mutant slot.
    /// </summary>
    private void SpawnMutantIntruderServer(Vector3 position, Quaternion rotation, MutantSuspectBehaviour prefab, MutantIntruderData data)
    {
        if (!IsServer) return;

        // Clear any leftover suspect reference so verdict/interact code sees null for this slot.
        suspectCharacter = null;

        GameObject mutantObj = Instantiate(prefab.gameObject, position, rotation);
        NetworkObject netObj = mutantObj.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[SuspectController] Mutant intruder prefab is missing a NetworkObject — aborting spawn.");
            Destroy(mutantObj);
            return;
        }

        netObj.Spawn();

        MutantSuspectBehaviour behaviour = mutantObj.GetComponent<MutantSuspectBehaviour>();
        if (behaviour == null)
        {
            Debug.LogError("[SuspectController] Mutant intruder prefab is missing MutantSuspectBehaviour — aborting.");
            netObj.Despawn();
            return;
        }

        if (climbThroughTargetPos == null)
            Debug.LogWarning("[SuspectController] climbThroughTargetPos is not assigned — mutant breakthrough destination will be Vector3.zero.", this);

        behaviour.BeginLineup(data, standPos, despawnPos, climbThroughTargetPos, shutterController, this);
        _currentMutant = behaviour;

        // Reuse the existing booth-waiting notification so players are alerted.
        NotifySuspectArrivingClientRpc();
    }

    /// <summary>
    /// Called by MutantSuspectBehaviour when its lineup sequence ends.
    /// Despawns the mutant if it retreated, then advances the lineup.
    /// </summary>
    public void OnMutantIntruderComplete(MutantSuspectBehaviour mutant, bool brokeThrough, bool staysAtWindow = false)
    {
        if (!IsServer) return;

        _currentMutant = null;

        // Despawn only if the mutant retreated and isn't staying at the window as a persistent threat.
        if (!brokeThrough && !staysAtWindow && mutant != null)
        {
            NetworkObject netObj = mutant.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn();
        }

        ShiftManager.Instance.SetNextSuspectReady();
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

        // OnHandOff writes isHandedOff (a server-authoritative NetworkVariable) which fires
        // OnFolderHandedOff on all clients. When a non-host player placed the folder the call
        // in DeliverVerdict ran on the client and the NV write was silently dropped — do it
        // here on the server so the event always fires and tutorial coroutines can advance.
        folder.OnHandOff();

        ExecuteVerdict(folder);
    }

    /// <summary>
    /// Performs the full verdict sequence: accuracy calculation, payout, and suspect processing.
    /// Must only be called on the server.
    /// </summary>
    private void ExecuteVerdict(FolderController folder)
    {
        if (!IsServer) return;

        suspectCharacter?.GetComponent<SuspectBarkController>()?.StopBarks();

        spawnedFolder = folder;
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