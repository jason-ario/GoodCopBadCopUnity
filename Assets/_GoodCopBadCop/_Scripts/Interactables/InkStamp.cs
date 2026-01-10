using UnityEngine;

public class InkStamp : Interactable
{
    [SerializeField] private PlaceObjectSlot stampPlaceObjectSlot;
    
    public override void Interact(PlayerInteractionController player)
    {
        Debug.Log("Interact");

        if (player.pickupController.HeldObject == stampPlaceObjectSlot.itemThatCanBePlaced && !stampPlaceObjectSlot.IsPlaced)
        {
            player.pickupController.DropObject(stampPlaceObjectSlot.PlaceObjectPos, false);
            stampPlaceObjectSlot.IsPlaced = true;
            Debug.Log("Place stamp");

            return;
        }

        if (player.pickupController.HeldObject == null && stampPlaceObjectSlot.IsPlaced)
        {
            stampPlaceObjectSlot.IsPlaced = false;

            Debug.Log("Picking up stamp");
            player.pickupController.PickUpObject(stampPlaceObjectSlot.itemThatCanBePlaced);
        }
    }
}
