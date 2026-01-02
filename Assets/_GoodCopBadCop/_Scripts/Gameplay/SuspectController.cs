using System;
using System.Collections;
using DG.Tweening;
using FIMSpace.FLook;
using Unity.Netcode;
using UnityEngine;

public class SuspectController : MonoBehaviour
{
    public static SuspectController Instance;
    public SuspectData suspectData;
    [SerializeField] private Transform spawnPos;
    [SerializeField] private Transform standPos;
    [SerializeField] private SuspectCharacter[] suspectCharacters;
    private SuspectCharacter _suspectCharacter;

    public void EnableLook()
    {
        _suspectCharacter.lookAnimator.ObjectToFollow = Camera.main.transform;
    }

    private void Start()
    {
        GameManager.Instance.OnRoundStart += StartRound;
    }

    void StartRound()
    {
        RequestSpawnSuspectServerRpc(0, spawnPos.position, spawnPos.rotation);
    }
    
    public void RequestSpawn(int suspectIndex, Vector3 position, Quaternion rotation)
    {
        RequestSpawnSuspectServerRpc(suspectIndex, position, rotation);
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
        StartCoroutine(WaitForSpawnAndAssign(networkObjectId));
    }

    private IEnumerator WaitForSpawnAndAssign(ulong networkObjectId)
    {
        while (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.ContainsKey(networkObjectId))
        {
            yield return null;
        }

        var netObj = NetworkManager.Singleton.SpawnManager.SpawnedObjects[networkObjectId];
        SuspectCharacter suspectCharacter = netObj.GetComponent<SuspectCharacter>();
        _suspectCharacter = suspectCharacter;
        yield return new WaitForSeconds(.2f);

        if (NetworkManager.Singleton.IsHost)
        {
            InitiateSuspect();
        }
    }
    
    void InitiateSuspect()
    {
        _suspectCharacter.animator.SetBool("Walking", true);
        _suspectCharacter.transform.DOMove(standPos.position, 3f).OnComplete(ArrivedAtPosition);
    }

    void ArrivedAtPosition()
    {
        _suspectCharacter.transform.DORotateQuaternion(standPos.rotation, .5f).OnComplete(SayEntryDialogue);
        _suspectCharacter.animator.SetBool("Walking", false);
    }

    void SayEntryDialogue()
    {
        Debug.Log("Saying entry dialogue");
        DialogueManager.Instance.SayDialogue(_suspectCharacter.entryDialogue, _suspectCharacter.audioSource, _suspectCharacter.voiceAudioClips);
    }
}

