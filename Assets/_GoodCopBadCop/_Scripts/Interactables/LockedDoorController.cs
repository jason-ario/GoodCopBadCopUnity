using System.Collections;
using UnityEngine;

public class LockedDoorController : Interactable
{
    [SerializeField] private MachineShake _machineShake; 
    [SerializeField] AudioSource doorAudio; 
    [SerializeField] AudioClip doorShakeClip;

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        StartCoroutine(TryToOpenDoor());
    }

    IEnumerator TryToOpenDoor()
    {
        doorAudio.PlayOneShot(doorShakeClip);
        _machineShake.isRunning = true;
        yield return new WaitForSeconds(0.7f);
        _machineShake.isRunning = false;
    }

    
}
