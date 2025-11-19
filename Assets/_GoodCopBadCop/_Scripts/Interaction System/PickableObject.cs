using UnityEngine;

public class PickableObject : MonoBehaviour, IInteractable
{
    // Virtual methods allow overriding

    public virtual void OnPickedUp() { }
    public virtual void OnDropped() { }

    public void Interact(PlayerInteractionController player)
    {
        player.pickupController.PickupObject(this);
    }
}