using UnityEngine;
using UnityEngine.Events;

public class InkStamp : Interactable
{
    [SerializeField] private PlaceObjectSlot stampPlaceObjectSlot;
    public StampContainer.StampType StampType;

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        if (player.pickupController.HeldObject == null && stampPlaceObjectSlot.IsPlaced)
        {
            stampPlaceObjectSlot.IsPlaced = false;

            Debug.Log("Picking up stamp");
            player.pickupController.PickUpObject(stampPlaceObjectSlot.itemThatCanBePlaced);
        }
    }

    public override void InteractWithItem(PlayerInteractionController player, PickableItemData itemData)
    {
        if (itemData == stampPlaceObjectSlot.itemThatCanBePlaced && !stampPlaceObjectSlot.IsPlaced)
        {
            base.InteractWithItem(player, itemData);
            player.pickupController.DropObject(stampPlaceObjectSlot.PlaceObjectPos, false);
            stampPlaceObjectSlot.IsPlaced = true;
            Debug.Log("Place stamp");

            return;
        }
    }
}
