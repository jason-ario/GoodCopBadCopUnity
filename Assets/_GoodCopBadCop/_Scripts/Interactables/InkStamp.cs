using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class InkStamp : Interactable
{
    [SerializeField] private PlaceObjectSlot stampPlaceObjectSlot;
    public StampContainer.StampType StampType;
    [SerializeField] private PickableObject inkStampPickup;
    private PickableObject spawnedInkStamp;
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Sync visual state immediately on spawn
        stampPlaceObjectSlot.IsPlaced = true;

        if (IsServer)
        {
            SpawnInkStamp();
        }
    }
    
    
    private void SpawnInkStamp()
    {
        NetworkObject inkStampNetObj = inkStampPickup.GetComponent<NetworkObject>();
        if (inkStampNetObj == null)
        {
            Debug.LogError("InkStamp: inkStampPickup prefab is missing a NetworkObject component.");
            return;
        }

        NetworkObject inkStamp = Instantiate(
            inkStampNetObj,
            stampPlaceObjectSlot.PlaceObjectPos.position,
            stampPlaceObjectSlot.PlaceObjectPos.rotation
        );
        inkStamp.Spawn();

        spawnedInkStamp = inkStamp.GetComponent<PickableObject>();
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        if (player.pickupController.HeldObject == null && stampPlaceObjectSlot.IsPlaced)
        {
            stampPlaceObjectSlot.IsPlaced = false;

            player.pickupController.PickUpObject(spawnedInkStamp);
        }
    }
    

    public override void InteractWithItem(PlayerInteractionController player, PickableItemData itemData)
    {
        if(stampPlaceObjectSlot == null) return;
        if (itemData != stampPlaceObjectSlot.itemThatCanBePlaced.ItemData) return;
        
        base.InteractWithItem(player, itemData);
        player.pickupController.DropObject(stampPlaceObjectSlot.PlaceObjectPos);
        stampPlaceObjectSlot.IsPlaced = true;

        return;
    }
}
