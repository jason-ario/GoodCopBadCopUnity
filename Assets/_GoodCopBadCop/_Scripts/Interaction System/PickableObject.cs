using UnityEngine;

public class PickableObject : Interactable
{
    // Virtual methods allow overriding

    public virtual void OnPickedUp() { }
    public virtual void OnDropped() { }
    public virtual void OnEquipped() { }

    [SerializeField] PickableItemData itemData;
    public PickableItemData ItemData => itemData;

    public override void Interact(PlayerInteractionController player)
    {
        player.pickupController.PickUpObject(this);
    }
}