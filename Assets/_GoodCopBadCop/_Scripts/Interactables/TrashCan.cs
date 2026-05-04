using UnityEngine;

public class TrashCan : Interactable
{
    public override void Interact(PlayerInteractionController player)
    {
        //throw trash
    }

    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        base.InteractWithItem(playerInteractionController, item);
        playerInteractionController.pickupController.DestroyEquippedItem();
    }
}
