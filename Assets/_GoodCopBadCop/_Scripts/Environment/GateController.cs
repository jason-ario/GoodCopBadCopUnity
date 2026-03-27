using System.Collections;
using UnityEngine;

public class GateController : Interactable
{
    bool gateOpen = false;
    [SerializeField] private Animator _animator;
    bool beingInteractedWith = false;
    [SerializeField] private float waitDelay = .5f;
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip doorOpenClip;
    [SerializeField] AudioClip doorCloseClip;
    [SerializeField] private Transform forwardMarker;

    public override void Interact(PlayerInteractionController player)
    {
        if (beingInteractedWith == false)
        {
            StartCoroutine(WaitAndToggleDoor(player));
        }
    }

    void ToggleDoor(PlayerInteractionController player)
    {
        if (gateOpen)
        {
            gateOpen = false;
            _animator.SetBool("OpenedIn", false);
            _animator.SetBool("OpenedOut", false);
            interactText = "Open";
            audioSource.PlayOneShot(doorCloseClip);
        }
        else
        {
            gateOpen = true;
            interactText = "Close";

            // Calculate if player is in front or behind the door
            Vector3 doorForward = forwardMarker.forward;
            Vector3 playerToDoor = transform.position - player.transform.position;
            
            // Dot product tells us which side the player is on
            float side = Vector3.Dot(doorForward, playerToDoor);
            
            // Set the appropriate bool based on player position
            if (side > 0)
            {
                _animator.SetBool("OpenedIn", true);
                _animator.SetBool("OpenedOut", false);
            }
            else
            {
                _animator.SetBool("OpenedIn", false);
                _animator.SetBool("OpenedOut", true);
            }
        }
    }

    IEnumerator WaitAndToggleDoor(PlayerInteractionController player)
    {
        if (gateOpen == false)
        {
            audioSource.PlayOneShot(doorOpenClip);
        }
        
        beingInteractedWith = true;
        player.playerAnimationController.OpenDoor();
        yield return new WaitForSeconds(waitDelay);
    
        ToggleDoor(player);
        yield return new WaitForSeconds(waitDelay);
        beingInteractedWith = false;
    }

    public void Reset()
    {
        _animator.SetBool("OpenedIn", false);
        _animator.SetBool("OpenedOut", false);
        gateOpen = false;
    }

    public void OpenGate()
    {
        _animator.SetBool("OpenedIn", true);
    }

    public void CloseGate()
    {
        Reset();
    }
}
