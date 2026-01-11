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
    [SerializeField] private SuspectCharacter[] suspectCharacters;
    public SuspectCharacter suspectCharacter;

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
        if (IsHost)
        {
            RequestSpawnSuspectServerRpc(0, spawnPos.position, spawnPos.rotation);
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
        DialogueManager.Instance.SayDialogue(suspectCharacter.entryDialogue, suspectCharacter.audioSource, suspectCharacter.voiceAudioClips);
    }

    public void RespondToDialogueChoice(int choiceIndex)
    {
        DialogueManager.Instance.SayDialogue(suspectCharacter.dialogueResponses[choiceIndex].text, suspectCharacter.audioSource, suspectCharacter.voiceAudioClips);
    }
}

