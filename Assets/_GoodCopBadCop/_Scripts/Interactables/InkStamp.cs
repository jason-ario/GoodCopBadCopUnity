using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class InkStamp : Interactable, IPickupSlot
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

        // Disable direct interaction on all clients — pickup is only via the InkStamp slot.
        SetSlottedStampInteractableClientRpc(spawnedInkStamp.NetworkObject, false);
    }

    /// <summary>
    /// Enables or disables direct interaction with the slotted stamp pickup on all clients.
    /// Call with false after spawning (slot owns pickup), and true only if the stamp needs
    /// to be released back into the world as a free-standing interactable.
    /// </summary>
    [ClientRpc]
    private void SetSlottedStampInteractableClientRpc(NetworkObjectReference stampRef, bool interactable)
    {
        if (!stampRef.TryGet(out NetworkObject stampNetObj)) return;
        PickableObject stamp = stampNetObj.GetComponent<PickableObject>();
        if (stamp == null) return;

        stamp.CanPickUpManually = interactable;
        stamp.SetInteractable(interactable);
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        if (player.pickupController.HeldObject == null && stampPlaceObjectSlot.IsPlaced)
        {
            stampPlaceObjectSlot.IsPlaced = false;

            // Re-enable the pickup so PickUpObject can claim it; it will immediately disable
            // interactability again as its optimistic lock before the ownership RPC lands.
            spawnedInkStamp.CanPickUpManually = true;
            spawnedInkStamp.SetInteractable(true);

            player.pickupController.PickUpObject(spawnedInkStamp);
        }
    }
    

    public override void InteractWithItem(PlayerInteractionController player, PickableObject item)
    {
        if(stampPlaceObjectSlot == null) return;
        if (item.ItemData != stampPlaceObjectSlot.itemThatCanBePlaced.ItemData) return;
        
        base.InteractWithItem(player, item);
        player.pickupController.DropObject(stampPlaceObjectSlot.PlaceObjectPos);
        stampPlaceObjectSlot.IsPlaced = true;

        return;
    }
}
