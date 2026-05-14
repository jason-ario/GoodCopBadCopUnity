using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class InkStamp : Interactable, IPickupSlot
{
    [SerializeField] private PlaceObjectSlot stampPlaceObjectSlot;
    public StampContainer.StampType StampType;
    [SerializeField] private PickableObject inkStampPickup;
    private PickableObject spawnedInkStamp;

    /// <summary>
    /// Tracks the spawned stamp NetworkObject across all clients so that non-host
    /// clients can resolve <see cref="spawnedInkStamp"/> as soon as the value is set,
    /// without relying on ClientRpc ordering relative to OnNetworkSpawn.
    /// </summary>
    private NetworkVariable<NetworkObjectReference> _spawnedStampRef = new NetworkVariable<NetworkObjectReference>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        stampPlaceObjectSlot.IsPlaced = true;

        _spawnedStampRef.OnValueChanged += OnSpawnedStampRefChanged;

        if (IsServer)
        {
            SpawnInkStamp();
        }
        else
        {
            // On clients, the NetworkVariable may already be populated if we joined late.
            TryResolveSpawnedStamp(_spawnedStampRef.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _spawnedStampRef.OnValueChanged -= OnSpawnedStampRefChanged;
    }

    private void OnSpawnedStampRefChanged(NetworkObjectReference previous, NetworkObjectReference current)
    {
        TryResolveSpawnedStamp(current);
    }

    private void TryResolveSpawnedStamp(NetworkObjectReference stampRef)
    {
        if (!stampRef.TryGet(out NetworkObject stampNetObj)) return;
        PickableObject stamp = stampNetObj.GetComponent<PickableObject>();
        if (stamp == null) return;

        spawnedInkStamp = stamp;
        stamp.CanPickUpManually = false;
        stamp.SetInteractable(false);
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
        spawnedInkStamp.CanPickUpManually = false;
        spawnedInkStamp.SetInteractable(false);

        // Publishing to the NetworkVariable replicates the reference to all clients,
        // triggering OnSpawnedStampRefChanged which resolves spawnedInkStamp there too.
        _spawnedStampRef.Value = inkStamp;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (spawnedInkStamp == null)
        {
            Debug.LogWarning("InkStamp.Interact: spawnedInkStamp is not yet resolved on this client.");
            return;
        }

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
