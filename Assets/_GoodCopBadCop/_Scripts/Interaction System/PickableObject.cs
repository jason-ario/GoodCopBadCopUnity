using UnityEngine;

public class PickableObject : MonoBehaviour, IInteractable
{
    // Virtual methods allow overriding

    public virtual void OnPickedUp() { }
    public virtual void OnDropped() { }
    public virtual void OnEquipped() { }

    [SerializeField] PickableItemData itemData;
    public PickableItemData ItemData => itemData;

    public void Interact(PlayerInteractionController player)
    {
        player.pickupController.PickUpObject(this);
    }
}