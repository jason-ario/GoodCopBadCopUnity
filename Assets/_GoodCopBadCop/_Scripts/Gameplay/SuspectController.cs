using System;
using System.Collections;
using DG.Tweening;
using FIMSpace.FLook;
using Unity.Netcode;
using UnityEngine;

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
    [SerializeField] Transform applicationSpawnPos;
    public Transform ApplicationSpawnPos => applicationSpawnPos;

    [Header("Pass")] [SerializeField] private Transform gatePos;

    private NetworkObject spawnedFolder;
    public NetworkVariable<int> suspectIndex = new NetworkVariable<int>(-1);

    private void Awake()
    {
        Instance = this;
    }

    public void EnableLook()
    {
        suspectCharacter.lookAnimator.ObjectToFollow = Camera.main.transform;
    }

    private void Start()
    {
        GameManager.Instance.OnRoundStart += StartRound;
    }

    void StartRound()
    {
        if (suspectIndex.Value >= suspectCharacters.Length - 1)
        {
            Debug.Log("No more suspects to spawn");
            GameManager.Instance.LevelComplete();
            return;
        }
        if (IsHost)
        {
            suspectIndex.Value += 1;
           
            RequestSpawnSuspectServerRpc(suspectIndex.Value, spawnPos.position, spawnPos.rotation);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSpawnSuspectServerRpc(int suspectIndex, Vector3 position, Quaternion rotation)
    {
        // Lookup the data on the server using the index
        SuspectCharacter suspect = suspectCharacters[suspectIndex];

        GameObject spawnedSuspect = Instantiate(suspect.gameObject, position, rotation);
        NetworkObject netObj = spawnedSuspect.GetComponent<NetworkObject>();
        netObj.Spawn();

        // Pass the index to clients so they know which references to set up
        AssignReferencesClientRpc(netObj.NetworkObjectId);
    }

    [ClientRpc]
    private void AssignReferencesClientRpc(ulong networkObjectId)
    {
        Debug.Log("Assigning references");
        StartCoroutine(WaitForSpawnAndAssign(networkObjectId));
    }

    private IEnumerator WaitForSpawnAndAssign(ulong networkObjectId)
    {
        while (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.ContainsKey(networkObjectId))
        {
            yield return null;
        }

        var netObj = NetworkManager.Singleton.SpawnManager.SpawnedObjects[networkObjectId];
        SuspectCharacter newSuspectCharacter = netObj.GetComponent<SuspectCharacter>();
        suspectCharacter = newSuspectCharacter;
        yield return new WaitForSeconds(.2f);
        if (NetworkManager.Singleton.IsHost)
        {
            InitiateSuspect();
        }
    }

    void InitiateSuspect()
    {
        suspectCharacter.animator.SetBool("Walking", true);
        suspectCharacter.transform.DOMove(standPos.position, 3f).OnComplete(ArrivedAtPosition);
    }

    void ArrivedAtPosition()
    {
        suspectCharacter.transform.DORotateQuaternion(standPos.rotation, .5f).OnComplete(SayEntryDialogue);
        suspectCharacter.animator.SetBool("Walking", false);
        EnableLook();
    }

    void SayEntryDialogue()
    {
        Debug.Log("Saying entry dialogue");
        DialogueManager.Instance.SayDialogue(suspectCharacter.entryDialogue, suspectCharacter.audioSource,
            suspectCharacter.voiceAudioClips);
        StartCoroutine(GivePaperworkCoroutine());
    }

    IEnumerator GivePaperworkCoroutine()
    {
        suspectCharacter.animator.SetTrigger("Give");
        yield return new WaitForSeconds(1f);
        SpawnPaperwork();
    }

    public void SpawnPaperwork()
    {
        if (!IsServer) return;

        NetworkObject spawnedApp =
            Instantiate(applicationPrefab, applicationSpawnPos.position, applicationSpawnPos.rotation);
        spawnedApp.Spawn();
        spawnedFolder = spawnedApp;
    }

    public void RespondToDialogueChoice(int choiceIndex)
    {
        DialogueManager.Instance.SayDialogue(suspectCharacter.dialogueResponses[choiceIndex].text,
            suspectCharacter.audioSource, suspectCharacter.voiceAudioClips);
    }

    public void Pass()
    {
        if (IsServer)
        {
            StartCoroutine(PassSequence());
        }
    }

    [ClientRpc]
    private void PassVisualsClientRpc()
    {
        if (IsServer) return; // Host already runs this via Coroutine logic if desired, or we separate logic.
        // To keep it simple and synchronized, we'll trigger the sequence on all clients.
        StartCoroutine(PassSequence());
    }

    IEnumerator PassSequence()
    {
        SuspectCharacter thisCharacter = suspectCharacter;

        // Visual: Animation
        thisCharacter.animator.SetTrigger("Give");
        yield return new WaitForSeconds(1f);

        // Logic: Despawn folder (Server only)
        if (IsServer)
        {
            if (spawnedFolder != null && spawnedFolder.IsSpawned)
                spawnedFolder.Despawn();

            // Trigger visuals for other clients
            PassVisualsClientRpc();
        }

        yield return new WaitForSeconds(2f);

        // Visual: Look and Rotation
        thisCharacter.lookAnimator.SetLookTarget(null);
        thisCharacter.transform.DORotate(gatePos.rotation.eulerAngles, .5f);
        yield return new WaitForSeconds(.5f);

        // Visual: Walking to gate
        thisCharacter.animator.SetBool("Walking", true);
        thisCharacter.transform.DOMove(gatePos.position, 4f);
        yield return new WaitForSeconds(4);
        thisCharacter.animator.SetBool("Walking", false);

        // Logic: Open Gate (Handled by GameManager/GateController - usually server-authoritative)
        if (IsServer)
        {
            GameManager.Instance.GateController.OpenGate();
            GameManager.Instance.NextRound();
        }

        yield return new WaitForSeconds(2);

        // Visual: Walking to despawn
        thisCharacter.animator.SetBool("Walking", true);
        thisCharacter.transform.DOMove(despawnPos.position, 10f).OnComplete(() =>
        {
            if (IsServer) DespawnSuspect(thisCharacter);
        });

        yield return new WaitForSeconds(2);

        // Logic: Close Gate (Server only)
        if (IsServer)
        {
            GameManager.Instance.GateController.CloseGate();
        }
    }

    void DespawnSuspect(SuspectCharacter suspectCharacter)
    {
        if (suspectCharacter == null) return;

        NetworkObject netObj = suspectCharacter.GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
        {
            netObj.Despawn(); // Proper Netcode despawn
        }
        else
        {
            Destroy(suspectCharacter.gameObject);
        }
    }
    
    public void Quarantine()
    {
        if (IsServer)
        {
            StartCoroutine(PassSequence());
        }
    }

    public void Kill()
    {

    }
}

