using System;
using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

public class FaxMachine : NetworkBehaviour
{
    [SerializeField] private GameObject paper;
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private MachineShake machineShake;
    [SerializeField] private Animator faxMachineAnimator;
    [SerializeField] NetworkObject paperPickupNetworkObject;
    private void Start()
    {
        paper.SetActive(false);
    }
    
    public void OnShiftStart()
    {
        if (IsHost)
        {
            RunFaxMachineClientRpc();
        }
    }
    
    [ClientRpc]
    private void RunFaxMachineClientRpc()
    {
        StartCoroutine(RunFaxMachine());
    }

    IEnumerator RunFaxMachine()
    {
        _audioSource.Play();
        machineShake.enabled = true;
        yield return new WaitForSeconds(4f);
        paper.gameObject.SetActive(true);
        faxMachineAnimator.SetBool("On", true);
        yield return new WaitForSeconds(3);
        machineShake.enabled = false;
        paper.gameObject.SetActive(false);

        if (IsServer)
        {
            // 1. Instantiate the object on the server
            NetworkObject spawnedPaper = Instantiate(paperPickupNetworkObject, paper.transform.position, paper.transform.rotation);
            
            // 2. Set the scale to match the paper's lossy scale
            spawnedPaper.transform.localScale = paper.transform.lossyScale;

            // 3. Spawn it into the network
            spawnedPaper.Spawn();
        }
    }
}
