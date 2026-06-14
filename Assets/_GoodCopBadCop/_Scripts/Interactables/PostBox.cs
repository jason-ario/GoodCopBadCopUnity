using UnityEngine;

/// <summary>
/// PostBox interactable for the Mutant Activity threat.
/// Players deposit MutantBits here to fill the box, then call HQ for pickup and a coupon reward.
///
/// Setup requirements:
///   - NetworkObject on the PostBox GameObject (already present in scene).
///   - HighlightEffect (required by Interactable).
///   - Collider set to the Interactable layer.
///   - The MutantBit PickableItemData asset added to itemsThatCanInteractWith in the Inspector.
///   - Set _capacity to the desired max bits per collection (default 10).
///   - Set _couponRewardPerCollection to the desired coupon payout.
/// </summary>
public class PostBox : CollectableContainer
{
    protected override void Awake()
    {
        base.Awake();
    }

    /// <summary>
    /// Called by PlayerInteractionController when the player left-clicks the PostBox
    /// while holding a MutantBit. Despawns the bit and deposits it into the container.
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController player, PickableObject item)
    {
        MutantBit bit = item as MutantBit;

        if (IsFull || IsAwaitingPickup || bit == null) return;

        base.InteractWithItem(player, item);

        // Release the bit from the player's hand (skips DropServerRpc so the bit
        // is not re-enabled as an interactable before it is despawned).
        player.pickupController.ReleaseHeldObjectForThrow();

        // Despawn the bit network-wide.
        bit.DespawnServerRpc();

        // Increment the fill counter on the server.
        DepositServerRpc();
    }

    protected override string GetDefaultInteractText() => $"Post Box ({FillCount}/{Capacity})";
    protected override string GetFullInteractText()    => "Call HQ for Pickup";
}
