using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A junk item spawned by TakeOutTrashTask. Collected by a player who is holding a non-full
/// TrashBag — either by pressing E (Interact) or by left-clicking while holding the bag
/// (InteractWithItem). Despawns on collection and fills the bag by one unit.
///
/// Prefab requirements:
///   - NetworkObject (or a parent NetworkObject when used on a SuspectCharacter)
///   - HighlightEffect  (required by Interactable)
///   - Collider on the Interactable layer
///   - Trash Bag PickableItemData assigned to itemsThatCanInteractWith in the Inspector
/// Standalone junk prefabs must be registered as Network Prefabs in the NetworkManager.
/// When added to a SuspectCharacter, keep the component disabled — it is enabled on death.
/// </summary>
public class JunkItem : Interactable
{
    /// <summary>Default interaction label shown when targeting a junk item.</summary>
    public const string DefaultInteractText = "Collect Junk";

    /// <summary>
    /// Fired on the server each time a JunkItem is successfully collected and despawned.
    /// Subscribe server-side to track how many items remain in the scene.
    /// </summary>
    public static event Action OnAnyJunkItemCollected;

    protected override void Awake()
    {
        base.Awake();

        if (string.IsNullOrEmpty(interactText))
            interactText = DefaultInteractText;
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    /// <summary>
    /// Triggered by the E key. If the player is holding a non-full TrashBag, collects
    /// this item. Does nothing when empty-handed or when the bag is already full.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        TrashBag bag = player.pickupController.HeldObject as TrashBag;
        if (bag == null || bag.IsFull) return;

        CollectServerRpc(bag.NetworkObject);
    }

    /// <summary>
    /// Triggered by left-click while holding a compatible item (TrashBag). Collects this
    /// junk item into the bag.
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController player, PickableObject heldItem)
    {
        TrashBag bag = heldItem as TrashBag;
        if (bag == null || bag.IsFull) return;

        CollectServerRpc(bag.NetworkObject);
    }

    // ── Server RPC ────────────────────────────────────────────────────────────

    /// <summary>
    /// Server-side collection: re-validates bag capacity to guard against race conditions,
    /// increments the bag's junk count, then despawns this item.
    /// RequireOwnership = false so any client can trigger collection.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void CollectServerRpc(NetworkObjectReference bagRef)
    {
        if (!bagRef.TryGet(out NetworkObject bagNetObj))
        {
            Debug.LogWarning("[JunkItem] CollectServerRpc: bag NetworkObject not found.");
            return;
        }

        TrashBag bag = bagNetObj.GetComponent<TrashBag>();

        if (bag == null)
        {
            Debug.LogWarning("[JunkItem] CollectServerRpc: NetworkObject has no TrashBag component.");
            return;
        }

        if (bag.IsFull) return;

        bag.AddJunk();
        OnAnyJunkItemCollected?.Invoke();
        NetworkObject.Despawn(destroy: true);
    }
}
