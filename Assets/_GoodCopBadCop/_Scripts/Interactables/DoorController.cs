using UnityEngine;

public class DoorController : MonoBehaviour, IInteractable
{
    bool doorOpen = false;
    [SerializeField] private Animator _animator;
    
    public void Interact(PlayerInteractionController player)
    {
        ToggleDoor(player);
    }

    void ToggleDoor(PlayerInteractionController player)
    {
        if (doorOpen)
        {
            doorOpen = false;
            _animator.SetBool("OpenedIn", false);
            _animator.SetBool("OpenedOut", false);
        }
        else
        {
            doorOpen = true;
            
            // Calculate if player is in front or behind the door
            Vector3 doorForward = transform.forward;
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
}