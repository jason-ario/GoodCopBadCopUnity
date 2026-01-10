using UnityEngine;

public class InkStamp : Interactable
{
    [SerializeField] private PlaceObjectSlot stampPlaceObjectSlot;
    
    public override void Interact(PlayerInteractionController player)
    {
        if (player.pickupController.HeldObject == stampPlaceObjectSlot.itemThatCanBePlaced)
        {
            player.pickupController.DropObject(stampPlaceObjectSlot.PlaceObjectPos);
        }
    }
}
