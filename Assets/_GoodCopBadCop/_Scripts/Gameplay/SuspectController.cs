using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
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
    [SerializeField] private DailySuspectManager dailySuspectManager;
    private SuspectCharacter suspectCharacter;
    public SuspectCharacter CurrentSuspect => suspectCharacter;

    [Header("Paperwork")]
    [FormerlySerializedAs("applicationPrefab")]
    [SerializeField] private NetworkObject folderPrefab;
    [SerializeField] private Transform applicationSpawnPos;
    public Transform ApplicationSpawnPos => applicationSpawnPos;

    [Header("Quarantine")]
    [SerializeField] private PlayableDirector quarantineTimeline;
    [SerializeField] private Transform suspectQuarantineFollowPos;

    private NetworkObject spawnedFolder;
    public NetworkVariable<int> suspectIndex = new NetworkVariable<int>(-1); 
    public int SuspectIndex => suspectIndex.Value;

    private ulong _currentSuspectNetworkObjectId = ulong.MaxValue;
    private bool _currentSuspectInitialized = false;


    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (ShiftManager.Instance != null)
        {
            ShiftManager.Instance.OnShiftStart += PopulateSuspectsForShift;
        }
    }

    private void OnDestroy()
    {
        if (ShiftManager.Instance != null)
        {
            ShiftManager.Instance.OnShiftStart -= PopulateSuspectsForShift;
        }
    }

    private void PopulateSuspectsForShift()
    {
        if (!IsServer)
            return;

        if (dailySuspectManager == null)
        {
            Debug.LogError("SuspectController is missing DailySuspectManager reference.");
            return;
        }

        int currentDay = 1;

        if (ShiftManager.Instance != null)
        {
            currentDay = ShiftManager.Instance.CurrentDay;
        }

        dailySuspectManager.GenerateDay(currentDay);
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
        if (dailySuspectManager == null)
        {
            Debug.LogError("Cannot spawn next suspect because DailySuspectManager is missing.");
            return;
        }

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

        SuspectRecord record = GetRecordForLineupIndex(lineupIndex);
        if (record == null)
        {
            Debug.LogError($"No SuspectRecord found for lineup index {lineupIndex}.");
            return;
        }

        SuspectCharacter suspectPrefab = GetPrefabFromRecord(record);
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
        if (suspectCharacter == null)
        {
            Debug.LogError($"Spawned suspect prefab '{spawnedSuspect.name}' is missing SuspectCharacter.");
            return;
        }

        suspectCharacter.Initialize(record);

        _currentSuspectNetworkObjectId = netObj.NetworkObjectId;
        _currentSuspectInitialized = false;

        TryInitializeCurrentSuspect();
        AssignReferencesClientRpc(netObj.NetworkObjectId);
    }

    private SuspectRecord GetRecordForLineupIndex(int lineupIndex)
    {
        if (dailySuspectManager == null)
            return null;

        return dailySuspectManager.GetSuspectForSlot(lineupIndex);
    }

    private SuspectCharacter GetPrefabFromRecord(SuspectRecord record)
    {
        if (record == null || record.Data == null || record.Data.CharacterPrefab == null)
            return null;

        return record.Data.CharacterPrefab;
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

        if (suspectCharacter.givesFolder)
        {
            suspectCharacter.GivePaperwork();
        }
    }

    public void SpawnPaperwork()
    {
        if (!IsServer) return;
        if (suspectCharacter == null) return;

        NetworkObject folder = Instantiate(folderPrefab, applicationSpawnPos.position, applicationSpawnPos.rotation);
        folder.GetComponent<FolderController>().SetInfo(suspectCharacter);
        folder.Spawn();
        spawnedFolder = folder;
    }

    public void SpawnAndThrowPaperwork(Transform handSpawnPos)
    {
        if (!IsServer) return;
        if (suspectCharacter == null) return;

        NetworkObject folder = Instantiate(folderPrefab, handSpawnPos.position, handSpawnPos.rotation);
        folder.Spawn();
        folder.GetComponent<FolderController>().SetInfo(suspectCharacter);

        if (folder.TryGetComponent<Rigidbody>(out Rigidbody rb))
        {
            rb.isKinematic = false;
            Vector3 throwDirection = handSpawnPos.forward + Vector3.up * 0.5f;
            rb.linearVelocity = throwDirection.normalized * 10f;
            rb.angularVelocity = new Vector3(
                Random.Range(-3f, 3f),
                Random.Range(-3f, 3f),
                Random.Range(-3f, 3f)
            );
        }

        spawnedFolder = folder;
    }

    public void RespondToDialogueChoice(int choiceIndex)
    {
        if (suspectCharacter == null) return;
        DialogueManager.Instance.SayDialogue(suspectCharacter, suspectCharacter.Data.dialogueResponses[choiceIndex].text);
    }

    private void ApplyCurrentSuspectJudgment(JudgmentResult judgmentResult)
    {
        if (!IsServer) return;
        if (suspectCharacter == null) return;
        if (suspectCharacter.Data == null) return;
        if (SuspectDatabase.Instance == null) return;

        int currentDay = 1;

        if (ShiftManager.Instance != null)
        {
            currentDay = ShiftManager.Instance.CurrentDay;
        }

        SuspectDatabase.Instance.ApplyJudgment(suspectCharacter.Data, judgmentResult, currentDay);
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

    private IEnumerator PassSequence()
    {
        if (suspectCharacter == null)
            yield break;

        ShiftManager.Instance.PassedSuspect(suspectCharacter);
        ApplyCurrentSuspectJudgment(JudgmentResult.Passed);

        SuspectCharacter thisCharacter = suspectCharacter;

        thisCharacter.animator.SetTrigger("Give");
        yield return new WaitForSeconds(1f);

        if (IsServer)
        {
            CleanupSpawnedFolder();
            PassVisualsClientRpc();
        }

        DialogueManager.Instance.SayDialogue(thisCharacter, "Thanks, comrade. I owe ya one.");

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
        ApplyCurrentSuspectJudgment(JudgmentResult.Quarantined);

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
        ApplyCurrentSuspectJudgment(JudgmentResult.Killed);

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
            NetworkHelper.DespawnWithChildren(spawnedFolder);
        }

        spawnedFolder = null;
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
}