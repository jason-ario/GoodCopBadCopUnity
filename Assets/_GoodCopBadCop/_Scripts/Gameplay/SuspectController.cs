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

    /// <summary>
    /// When true, the next suspect's entry line and bark schedule are suppressed — useful for
    /// scripted arrivals where a <see cref="ScriptedDialogueRunner"/> takes over dialogue.
    /// Consumed and reset to false inside <see cref="SayEntryDialogue"/>.
    /// Set by Day_01 for Vlad's scripted Day 1 appearance.
    /// </summary>
    public static bool ForceNextSuspectSkipEntryDialogue = false;

    /// <summary>
    /// When true, the next suspect's entry dialogue will not trigger a paperwork hand-off,
    /// regardless of <see cref="SuspectData.GivesPaperwork"/>. Consumed and reset to false
    /// inside <see cref="SayEntryDialogue"/>. Set by Day_01 for Vlad's scripted Day 1 appearance.
    /// </summary>
    public static bool ForceNextSuspectNoPaperwork = false;

    /// <summary>
    /// When true, the current suspect's exit dialogue is suppressed — no line is spoken as
    /// they leave. Consumed and reset to false inside <see cref="PassSequence"/> immediately
    /// before the <see cref="SayExitDialogue"/> call. Set by Day_01 after Vlad's closing
    /// cutscene so his goodbye line does not play on top of the scripted sequence.
    /// </summary>
    public static bool ForceNextSuspectSkipExitDialogue = false;

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

    // Category-level scoring results — populated by CalculateCategoryScores, consumed by PayOutResults.
    private int _categoriesCorrect = 0;       // player checked the box AND the category has active anomalies
    private int _categoriesFalsePositive = 0; // player checked the box but NO anomalies in that category
    private int _categoriesMissed = 0;        // category has active anomalies but player did NOT check it
    private int _totalActiveCategories = 0;   // categories that have at least one active anomaly

    /// <summary>
    /// Type-name strings (e.g. "MutationAnomaly") for every category the player correctly identified.
    /// Populated by CalculateCategoryScores; used by PayOutResults to gate evidence bonuses.
    /// </summary>
    private readonly HashSet<string> _correctCategoryTypeNames = new HashSet<string>();

    [Header("Coupon Payouts")]
    [Tooltip("Bonus coupons awarded when every active category is identified with zero false positives.")]
    [SerializeField] int couponPerfectAnomaliesBonus = 5;
    [Tooltip("Coupons earned per correctly identified category.")]
    [SerializeField] int couponPerCorrectAnomaly = 5;
    [Tooltip("Coupons deducted per active category the player failed to identify.")]
    [SerializeField] int couponPenaltyPerMissedAnomaly = 2;
    [Tooltip("Coupons deducted per category the player checked that had no active anomalies.")]
    [SerializeField] int couponPenaltyPerFalsePositiveAnomaly = 2;
    /// <summary>Base reward always paid out regardless of checklist accuracy.</summary>
    [SerializeField] int couponBaseReward = 5;
    [Tooltip("Extra coupons awarded per evidence item placed in the folder for a correctly identified category.")]
    [SerializeField] int couponPerEvidenceItem = 3;
    
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
            suspectCharacter.InitializeByInfectionStage();
        }

        _currentSuspectNetworkObjectId = netObj.NetworkObjectId;
        _currentSuspectInitialized = false;

        TryInitializeCurrentSuspect();
        AssignReferencesClientRpc(netObj.NetworkObjectId);
    }

    /// <summary>
    /// Introduces a scene-placed <see cref="SuspectCharacter"/> into the suspect flow.
    /// If the character's GameObject is inactive (e.g. it was placed in the scene but kept
    /// disabled until needed), this method activates it on the server and calls
    /// <see cref="NetworkObject.Spawn"/> so NGO propagates the activation to all clients.
    /// NGO 2.x (Unity 6) registers inactive scene NetworkObjects at scene load via
    /// <c>FindObjectsByType(FindObjectsInactive.Include)</c>, so the client-side object is
    /// always resolvable by its GlobalObjectIdHash when the spawn message arrives.
    /// The character is then teleported to the spawn point and runs the standard DOTween
    /// walk-in and arrival sequence, firing <see cref="OnSuspectArrived"/> exactly as a
    /// normally-spawned suspect would.
    /// Must only be called on the server, typically via <see cref="InterceptNextSuspectSpawn"/>.
    /// </summary>
    public void IntroduceSceneSuspect(SuspectCharacter character)
    {
        if (!IsServer) return;

        if (character == null)
        {
            Debug.LogError("[SuspectController] IntroduceSceneSuspect: character is null.");
            return;
        }

        NetworkObject netObj = character.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError($"[SuspectController] IntroduceSceneSuspect: '{character.name}' is missing a NetworkObject component.");
            return;
        }

        // Activate and spawn if the object was kept inactive in the scene.
        // NGO will send a spawn message to clients; they find the scene object by
        // GlobalObjectIdHash, activate it, and mark it as spawned on their end.
        if (!character.gameObject.activeSelf)
            character.gameObject.SetActive(true);

        if (!netObj.IsSpawned)
            netObj.Spawn();

        // Teleport to spawn point. NetworkTransform syncs the new position to clients.
        character.transform.position = spawnPos.position;
        character.transform.rotation = spawnPos.rotation;

        suspectCharacter = character;
        _currentSuspectNetworkObjectId = netObj.NetworkObjectId;
        _currentSuspectInitialized = true;

        character.InitializeClean();

        // Tell clients which NetworkObject is the current suspect.
        AssignReferencesClientRpc(netObj.NetworkObjectId);

        // Run the standard walk-in (DOTween to standPos → ArrivedAtPosition → OnSuspectArrived).
        InitiateSuspect();

        Debug.Log($"[SuspectController] IntroduceSceneSuspect: '{character.name}' activated and walking to window.");
    }

    /// <summary>
    /// Forces <see cref="suspectIndex"/> to the given value.
    /// Debug use only — call from <see cref="DebugConsole"/> before <see cref="NextSuspect"/>
    /// to inject a specific slot index, bypassing the normal sequential spawn chain.
    /// </summary>
    public void DebugSetSuspectIndex(int index)
    {
        if (!IsServer) return;
        suspectIndex.Value = index;
        Debug.Log($"[SuspectController] Debug: suspectIndex forced to {index}.");
    }

    /// <summary>
    /// Spawns the given <see cref="SuspectCharacter"/> prefab as a scripted suspect,
    /// bypassing the <see cref="DailySuspectManager"/> lineup entirely. The character
    /// goes through the full walk-in and arrival flow exactly as a normally-scheduled
    /// suspect. The character is always initialized clean (no anomalies).
    /// Pair with <see cref="ForceNextSuspectNoPaperwork"/> to suppress document hand-off.
    /// Must only be called on the server, typically via <see cref="InterceptNextSuspectSpawn"/>.
    /// </summary>
    public void SpawnScriptedSuspect(SuspectCharacter prefab)
    {
        if (!IsServer) return;

        if (prefab == null)
        {
            Debug.LogError("[SuspectController] SpawnScriptedSuspect: prefab is null.");
            return;
        }

        GameObject spawnedSuspect = Instantiate(prefab.gameObject, spawnPos.position, spawnPos.rotation);
        NetworkObject netObj = spawnedSuspect.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError($"[SuspectController] SpawnScriptedSuspect: prefab '{prefab.name}' is missing a NetworkObject component.");
            Destroy(spawnedSuspect);
            return;
        }

        netObj.Spawn();

        suspectCharacter = spawnedSuspect.GetComponent<SuspectCharacter>();
        suspectCharacter.InitializeClean();

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

        if (!ForceNextSuspectSkipEntryDialogue)
        {
            string entryDialogue = suspectCharacter.GetEntryDialogue();
            DialogueManager.Instance.SayDialogue(suspectCharacter, entryDialogue);
            suspectCharacter.GetComponent<SuspectBarkController>()?.BeginBarkSchedule();
        }

        ForceNextSuspectSkipEntryDialogue = false;

        // Suspect cam is no longer activated on standard arrivals.
        // It is activated exclusively by ScriptedDialogueRunner.EnterScriptedModeClientRpc
        // for scripted cutscene sequences.

        bool givesPaperwork = suspectCharacter.Data.GivesPaperwork && !ForceNextSuspectNoPaperwork;
        ForceNextSuspectNoPaperwork = false;

        if (givesPaperwork)
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

        
        if (IsServer)
        {
            bool skipExit = ForceNextSuspectSkipExitDialogue;
            ForceNextSuspectSkipExitDialogue = false;
            if (!skipExit) SayExitDialogue(thisCharacter, SuspectData.Verdict.Passed);
        }

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

    /// <summary>
    /// Evaluates which of the five anomaly categories the player correctly identified via the
    /// exam-page checkboxes and populates the category scoring fields used by PayOutResults.
    /// Returns an accuracy percentage (0–100) based on correct identifications vs active categories.
    /// Must only be called on the server.
    /// </summary>
    public int CalculateCategoryScores(FolderController folder, SuspectCharacter suspectCharacter)
    {
        _categoriesCorrect = 0;
        _categoriesFalsePositive = 0;
        _categoriesMissed = 0;
        _totalActiveCategories = 0;
        _correctCategoryTypeNames.Clear();

        if (suspectCharacter == null)
            return 100;

        AnomalyController ac = suspectCharacter.AnomalyController;
        HashSet<string> checkedCategories = folder.GetCheckedCategoryNames();

        // The five category base-class names that map directly to the five checklist checkboxes.
        string[] knownCategories =
        {
            "DocumentationAnomaly",
            "VitalsAnomaly",
            "BehaviorAnomaly",
            "MutationAnomaly",
            "SupernaturalAnomaly"
        };

        foreach (string category in knownCategories)
        {
            bool hasAnomaly = ac.HasActiveAnomalyOfCategory(category);
            bool wasChecked = checkedCategories.Contains(category);

            if (hasAnomaly) _totalActiveCategories++;

            if (wasChecked && hasAnomaly)
            {
                _categoriesCorrect++;
                _correctCategoryTypeNames.Add(category);
            }
            else if (wasChecked && !hasAnomaly)  _categoriesFalsePositive++;
            else if (!wasChecked && hasAnomaly)  _categoriesMissed++;
        }

        // Accuracy = fraction of active categories correctly identified.
        // A clean suspect (no active categories) is 100% only if the player made no false claims.
        if (_totalActiveCategories == 0)
            return _categoriesFalsePositive == 0 ? 100 : 0;

        return Mathf.Max(0, (_categoriesCorrect * 100) / _totalActiveCategories);
    }

    // Keep for backward compat with any external callers; routes to the new implementation.
    public int CalculatePercentAccuracy(FolderController folder, SuspectCharacter suspectCharacter)
        => CalculateCategoryScores(folder, suspectCharacter);


    /// <summary>
    /// Calculates coupons from the category scoring fields, spawns them at the ATM,
    /// and broadcasts popup notifications to every connected client.
    /// Must only be called on the server after CalculateCategoryScores has run.
    /// totalBonusAmount consolidates the perfect-identification bonus and the evidence bonus.
    /// </summary>
    private void PayOutResults()
    {
        if (!IsServer) return;

        // Reward for each correctly identified category.
        int categoryReward = _categoriesCorrect * couponPerCorrectAnomaly;

        // Penalties for missed and falsely claimed categories.
        int missedPenalty = _categoriesMissed * couponPenaltyPerMissedAnomaly;
        int falsePenalty = _categoriesFalsePositive * couponPenaltyPerFalsePositiveAnomaly;

        // Perfect bonus: every active category found and no false positives.
        int perfectBonusAmount = (_categoriesCorrect == _totalActiveCategories
                                  && _categoriesFalsePositive == 0
                                  && _totalActiveCategories > 0)
            ? couponPerfectAnomaliesBonus
            : 0;

        // Evidence bonus: extra coupons per proof document filed for a correctly identified category.
        // Placing evidence for a category with no active anomaly gives no bonus and no penalty.
        int evidenceBonus = 0;
        if (spawnedFolder != null)
        {
            System.Collections.Generic.Dictionary<AnomalyCategory, int> evidenceCounts =
                spawnedFolder.GetEvidenceCountByCategory();

            foreach (var kvp in evidenceCounts)
            {
                if (_correctCategoryTypeNames.Contains(kvp.Key.ToTypeName()))
                    evidenceBonus += kvp.Value * couponPerEvidenceItem;
            }
        }

        int totalCoupons = Mathf.Max(0,
            couponBaseReward + categoryReward - missedPenalty - falsePenalty + perfectBonusAmount + evidenceBonus);

        if (ATM.Instance != null)
            ATM.Instance.SpawnCoupons(totalCoupons);
        else
            Debug.LogError("[SuspectController] ATM.Instance is null — verdict payout coupons not dispensed.");

        Debug.Log(
            $"Payout — Correct categories: {_categoriesCorrect}/{_totalActiveCategories}, " +
            $"Missed: {_categoriesMissed}, False positives: {_categoriesFalsePositive}, " +
            $"Base: +{couponBaseReward}, Category reward: +{categoryReward}, " +
            $"Missed penalty: -{missedPenalty}, False penalty: -{falsePenalty}, " +
            $"Perfect bonus: +{perfectBonusAmount}, Evidence bonus: +{evidenceBonus}, Total: {totalCoupons}");

        ShowScoringResultsClientRpc(
            categoryReward,
            _categoriesCorrect,
            _totalActiveCategories,
            perfectBonusAmount + evidenceBonus,
            totalCoupons);
    }

    [ClientRpc]
    private void ShowScoringResultsClientRpc(
        int anomalyAmount,
        int correctCount,
        int totalCount,
        int totalBonusAmount,
        int totalCoupons)
    {
        StartCoroutine(ShowCashPopUpSequence(anomalyAmount, correctCount, totalCount, totalBonusAmount, totalCoupons));
    }

    private IEnumerator ShowCashPopUpSequence(
        int anomalyAmount,
        int correctCount,
        int totalCount,
        int totalBonusAmount,
        int totalCoupons)
    {
        yield return new WaitForSeconds(2f);

        // Message 1: Category identification breakdown.
        string anomalyMessage = totalCount > 0
            ? $"Categories Identified: {correctCount}/{totalCount}"
            : "No anomalies present";
        UIController.Instance.ShowCashPopUpNotification(anomalyAmount, anomalyMessage);

        yield return new WaitForSeconds(2f);

        // Message 2: Bonuses (perfect identification + evidence), if any were earned.
        if (totalBonusAmount > 0)
        {
            UIController.Instance.ShowCashPopUpNotification(totalBonusAmount, "Bonuses");
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
            // Flag the record for infection score reset on the next day advance (server only).
            SuspectRecord quarantineRecord = SuspectRunRecords.Instance?.GetRecord(suspectCharacter.Data);
            if (quarantineRecord != null)
                quarantineRecord.pendingVaccineReset = true;
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

        // Permanently remove this suspect from future shifts.
        SuspectRecord killRecord = SuspectRunRecords.Instance?.GetRecord(suspectCharacter.Data);
        if (killRecord != null)
            killRecord.isKilled = true;

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

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
            SuspectCharacter.OnSuspectKilledByPlayer += HandleSuspectKilledByPlayer;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        SuspectCharacter.OnSuspectKilledByPlayer -= HandleSuspectKilledByPlayer;
    }

    /// <summary>
    /// Called on the server when a suspect is killed by a player melee hit.
    /// Skips all scoring and payout, then advances the lineup after the death animation plays out.
    /// </summary>
    private void HandleSuspectKilledByPlayer(SuspectCharacter killed)
    {
        if (!IsServer) return;
        if (killed != suspectCharacter) return;

        StartCoroutine(KilledByPlayerSequence(killed));
    }

    /// <summary>
    /// Waits for the death animation, cleans up documents and the folder,
    /// despawns the suspect, and advances to the next suspect — with no payout.
    /// </summary>
    private IEnumerator KilledByPlayerSequence(SuspectCharacter killed)
    {
        killed.GetComponent<SuspectBarkController>()?.StopBarks();

        // Give the death animation time to play before cleaning up.
        yield return new WaitForSeconds(3f);

        CleanupSpawnedFolder();
        DespawnSuspect(killed);
        ShiftManager.Instance.SetNextSuspectReady();
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
        accuracyOfLastSuspectFolder = CalculateCategoryScores(folder, suspectCharacter);

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