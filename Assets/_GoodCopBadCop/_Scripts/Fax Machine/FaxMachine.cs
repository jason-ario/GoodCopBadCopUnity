using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class FaxMachine : NetworkBehaviour
{
    [SerializeField] Newspaper newspaper; 
    [SerializeField] Transform newspaperSpawnPoint;
    [SerializeField] private AudioSource faxAudioSource;
    [SerializeField] AudioClip faxClip;
    [SerializeField] private Animator _animator;
    [SerializeField] private MachineShake _machineShake;
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            ShiftManager.Instance.OnNightPhaseBegin += SpawnNewspaper;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (IsServer)
        {
            ShiftManager.Instance.OnNightPhaseBegin -= SpawnNewspaper;
        }
    }

    private void SpawnNewspaper()
    {
        StartCoroutine(WaitAndSpawnNewspaper());
    }
    
    IEnumerator WaitAndSpawnNewspaper()
    {
        yield return new WaitForSeconds(5);
        
        // Instantiate the newspaper on the server
        GameObject spawnedNewspaper = Instantiate(newspaper.gameObject, newspaperSpawnPoint.position, newspaperSpawnPoint.rotation);
        
        // Get the NetworkObject component
        NetworkObject networkObject = spawnedNewspaper.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError("Newspaper prefab is missing NetworkObject component!");
            Destroy(spawnedNewspaper);
            yield break;
        }

        // Spawn it on the network so all clients can see it
        _machineShake.enabled = true;
        faxAudioSource.PlayOneShot(faxClip);
        yield return new WaitForSeconds(4);
        networkObject.Spawn(true);
        networkObject.GetComponent<PickableObject>().SetParent(newspaperSpawnPoint);
        _animator.enabled = true;
        yield return new WaitForSeconds(2);
        _machineShake.enabled = false;

    }
}
