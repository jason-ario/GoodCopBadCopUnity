using System;
using System.Collections;
using DG.Tweening;
using FIMSpace.FLook;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Playables;

public class SuspectController : NetworkBehaviour
{
    public static SuspectController Instance;
    public SuspectData suspectData;

    [SerializeField] private Transform spawnPos;
    [SerializeField] private Transform standPos;
    [SerializeField] private Transform despawnPos;

    [SerializeField] private SuspectCharacter[] suspectCharacters;
    public SuspectCharacter suspectCharacter;

    [SerializeField] private NetworkObject applicationPrefab;
    [SerializeField] private Transform applicationSpawnPos;
    public Transform ApplicationSpawnPos => applicationSpawnPos;

    [Header("Pass")]
    [SerializeField] private Transform gatePos;

    private NetworkObject spawnedFolder;
    public NetworkVariable<int> suspectIndex = new NetworkVariable<int>(-1);

    [SerializeField] private PlayableDirector quarantineTimeline;
    [SerializeField] private Transform suspectQuarantineFollowPos;

    // Guards against duplicate init for the same spawned suspect
    private ulong _currentSuspectNetworkObjectId = ulong.MaxValue;
    private bool _currentSuspectInitialized = false;
    private bool _subscribedToRoundStart = false;

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

        if (suspectIndex.Value >= ShiftManager.Instance.SuspectsPerShift)
        {
            Debug.Log("No more suspects to spawn");
            ShiftManager.Instance.EndShift();
            return;
        }
        
        suspectIndex.Value += 1;

        if (suspectIndex.Value < 0 || suspectIndex.Value >= suspectCharacters.Length)
        {
            Debug.LogError($"Suspect index {suspectIndex.Value} is out of bounds.");
            return;
        }

        SpawnSuspectServer(suspectIndex.Value, spawnPos.position, spawnPos.rotation);
    }

    private void SpawnSuspectServer(int newSuspectIndex, Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;

        SuspectCharacter suspectPrefab = suspectCharacters[newSuspectIndex];
        GameObject spawnedSuspect = Instantiate(suspectPrefab.gameObject, position, rotation);
        NetworkObject netObj = spawnedSuspect.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("Spawned suspect is missing a NetworkObject.");
            Destroy(spawnedSuspect);
            return;
        }

        netObj.Spawn();

        suspectCharacter = spawnedSuspect.GetComponent<SuspectCharacter>();
        if (suspectCharacter == null)
        {
            Debug.LogError("Spawned suspect is missing SuspectCharacter.");
            return;
        }

        _currentSuspectNetworkObjectId = netObj.NetworkObjectId;
        _currentSuspectInitialized = false;

        // Server/host initializes locally exactly once
        TryInitializeCurrentSuspect();

        // Remote clients just resolve references
        AssignReferencesClientRpc(netObj.NetworkObjectId);
    }

    [ClientRpc]
    private void AssignReferencesClientRpc(ulong networkObjectId)
    {
        // Host/server already has the direct reference and already initialized locally.
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

        // Clients do NOT call InitiateSuspect.
    }

    private void TryInitializeCurrentSuspect()
    {
        if (!IsServer) return;
        if (suspectCharacter == null) return;

        NetworkObject netObj = suspectCharacter.GetComponent<NetworkObject>();
        if (netObj == null) return;

        if (_currentSuspectInitialized && netObj.NetworkObjectId == _currentSuspectNetworkObjectId)
        {
            Debug.Log($"Skipping duplicate suspect init for {netObj.NetworkObjectId}");
            return;
        }

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

        Debug.Log($"Initiate suspect | IsServer={IsServer} IsClient={IsClient} IsHost={IsHost} NetId={suspectCharacter.NetworkObjectId}");

        suspectCharacter.animator.SetBool("Walking", true);
        suspectCharacter.transform
            .DOMove(standPos.position + suspectCharacter.standPosOffset, 3f)
            .OnComplete(ArrivedAtPosition);

        int anomalyCount = AnomalyManager.Instance.AnomalyCountThisRound();
        suspectCharacter.PrepareAnomalies();
        anomalyCount = Mathf.Min(anomalyCount, suspectCharacter.AnomalyController.AvailableAnomalyCount);

        for (int i = 0; i < anomalyCount; i++)
        {
            suspectCharacter.TriggerAnomaly();
        }
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

        Debug.Log("Saying entry dialogue");

        if (suspectCharacter.attackImmediately)
        {
            suspectCharacter.AimAtPlayer();
            return;
        }

        DialogueManager.Instance.SayDialogue(suspectCharacter, suspectCharacter.entryDialogue);

        if (suspectCharacter.givesFolder)
        {
            suspectCharacter.GivePaperwork();
        }
    }

    public void SpawnPaperwork()
    {
        if (!IsServer) return;

        Debug.Log("Spawning paperwork");
        NetworkObject spawnedApp =
            Instantiate(applicationPrefab, applicationSpawnPos.position, applicationSpawnPos.rotation);

        spawnedApp.Spawn();
        spawnedFolder = spawnedApp;
    }

    public void RespondToDialogueChoice(int choiceIndex)
    {
        if (suspectCharacter == null) return;
        DialogueManager.Instance.SayDialogue(suspectCharacter, suspectCharacter.dialogueResponses[choiceIndex].text);
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

    [ClientRpc]
    private void QuarantineVisualsClientRpc()
    {
        if (IsServer) return;
        StartCoroutine(QuarantineSequence());
    }

    private IEnumerator PassSequence()
    {
        ShiftManager.Instance.PassedSuspect(suspectCharacter);
        SuspectCharacter thisCharacter = suspectCharacter;

        thisCharacter.animator.SetTrigger("Give");
        yield return new WaitForSeconds(1f);

        if (IsServer)
        {
            if (spawnedFolder != null && spawnedFolder.IsSpawned)
            {
                NetworkHelper.DespawnWithChildren(spawnedFolder);
            }

            PassVisualsClientRpc();
        }

        DialogueManager.Instance.SayDialogue(suspectCharacter, "Thanks, comrade. I owe ya one.");

        yield return new WaitForSeconds(2f);

        thisCharacter.lookAnimator.SetLookTarget(null);
        thisCharacter.transform.DORotate(gatePos.rotation.eulerAngles, 0.5f);
        yield return new WaitForSeconds(0.5f);

        thisCharacter.animator.SetBool("Walking", true);
        thisCharacter.transform.DOMove(gatePos.position, 4f);
        yield return new WaitForSeconds(4f);
        thisCharacter.animator.SetBool("Walking", false);

        if (IsServer)
        {
            GameManager.Instance.GateController.OpenGate();
            GameManager.Instance.NextRound();
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

    public void Quarantine()
    {
        if (!IsServer) return;
        StartCoroutine(QuarantineSequence());
    }

    private IEnumerator QuarantineSequence()
    {
        ShiftManager.Instance.QuarantinedSuspect();

        suspectCharacter.animator.SetTrigger("Give");
        yield return new WaitForSeconds(1f);

        if (IsServer)
        {
            if (spawnedFolder != null && spawnedFolder.IsSpawned)
            {
                NetworkHelper.DespawnWithChildren(spawnedFolder);
            }

            QuarantineVisualsClientRpc();
        }

        yield return new WaitForSeconds(2f);
        suspectCharacter.animator.SetTrigger("Shocked");

        quarantineTimeline.gameObject.SetActive(true);
        quarantineTimeline.Play();
        DialogueManager.Instance.SayDialogue(suspectCharacter, "Wait... No... I'm healthy.. No!");

        yield return new WaitForSeconds(2f);
        suspectCharacter.lookAnimator.SetLookTarget(null);
        suspectCharacter.animator.SetBool("BeingRestrained", true);

        float quarantiningTime = 9f;
        float timeElapsed = 0f;

        while (timeElapsed < quarantiningTime)
        {
            yield return new WaitForEndOfFrame();
            suspectCharacter.transform.position = suspectQuarantineFollowPos.position;
            suspectCharacter.transform.rotation = suspectQuarantineFollowPos.rotation;
            timeElapsed += Time.deltaTime;
        }

        if (IsServer)
        {
            DespawnSuspect(suspectCharacter);
            GameManager.Instance.NextRound();
        }

        quarantineTimeline.gameObject.SetActive(false);
    }

    public void Kill()
    {
        if (!IsServer) return;
        StartCoroutine(KillSequence());
    }

    private IEnumerator KillSequence()
    {
        ShiftManager.Instance.KillSuspect(suspectCharacter);

        yield return new WaitForSeconds(1f);
        SuspectCharacter thisCharacter = suspectCharacter;

        thisCharacter.animator.SetTrigger("Give");
        yield return new WaitForSeconds(1f);

        if (IsServer)
        {
            if (spawnedFolder != null && spawnedFolder.IsSpawned)
            {
                NetworkHelper.DespawnWithChildren(spawnedFolder);
            }
        }

        yield return new WaitForSeconds(1f);

        DialogueManager.Instance.SayDialogue(suspectCharacter, "Wait... NO!!!");
        suspectCharacter.animator.SetTrigger("ShotUp");
        yield return new WaitForSeconds(1f);

        KillMachineController.Instance.Kill();

        yield return new WaitForSeconds(8f);

        if (IsServer)
        {
            DespawnSuspect(thisCharacter);
            NextSuspect();
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

    public void SpawnAndThrowPaperwork(Transform handSpawnPos)
    {
        if (!IsServer) return;

        NetworkObject spawnedApp =
            Instantiate(applicationPrefab, handSpawnPos.position, handSpawnPos.rotation);

        spawnedApp.Spawn();

        if (spawnedApp.TryGetComponent<Rigidbody>(out Rigidbody rb))
        {
            rb.isKinematic = false;
            Vector3 throwDirection = handSpawnPos.forward + Vector3.up * 0.5f;
            rb.linearVelocity = throwDirection.normalized * 10f;
            rb.angularVelocity = new Vector3(
                UnityEngine.Random.Range(-3f, 3f),
                UnityEngine.Random.Range(-3f, 3f),
                UnityEngine.Random.Range(-3f, 3f)
            );
        }

        spawnedFolder = spawnedApp;
    }

    public void ResetSuspects()
    {
        suspectIndex.Value = -1;
        _currentSuspectInitialized = false;
        _currentSuspectNetworkObjectId = ulong.MaxValue;
        suspectCharacter = null;
    }
}