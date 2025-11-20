using UnityEngine;

public class DoorController : MonoBehaviour, IInteractable
{
    bool doorOpen = false;
    [SerializeField] private Animator _animator;
    
    public void Interact(PlayerInteractionController player)
    {
        ToggleDoor();
    }

    void ToggleDoor()
    {
        if (doorOpen)
        {
            doorOpen = false;
            _animator.SetBool("Opened", false);
        }
        else
        {
            doorOpen = true;
            _animator.SetBool("Opened", true);
        }
    }
}
