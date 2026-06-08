using UnityEngine;

/// <summary>
/// PostBox interactable for the Go Hunting task.
/// Players deposit MutantBits here to progress toward the task goal.
///
/// Setup requirements:
///   - NetworkObject on the PostBox GameObject (already present in scene).
///   - HighlightEffect (required by Interactable).
///   - Collider set to the Interactable layer.
///   - The MutantBit PickableItemData asset added to itemsThatCanInteractWith in the Inspector.
/// </summary>
public class PostBox : Interactable
{
    private const string InteractTextDefault  = "Post Box";
    private const string InteractTextComplete = "Post Box (Task Complete)";

    protected override void Awake()
    {
        base.Awake();
        interactText = InteractTextDefault;
    }

    /// <summary>
    /// Called by PlayerInteractionController when the player left-clicks the PostBox
    /// while holding a MutantBit. Despawns the bit and forwards the deposit to GoHuntingTask.
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController player, PickableObject item)
    {
        MutantBit bit = item as MutantBit;

        if (bit == null) return;
        if (GoHuntingTask.Instance == null || GoHuntingTask.Instance.IsComplete) return;

        base.InteractWithItem(player, item);

        // Release the bit from the player's hand (skips DropServerRpc so the bit
        // is not re-enabled as an interactable before it is despawned).
        player.pickupController.ReleaseHeldObjectForThrow();

        // Despawn the bit network-wide.
        bit.DespawnServerRpc();

        // Increment the task counter on the server.
        GoHuntingTask.Instance.DepositBitServerRpc();

        RefreshInteractText();
    }

    private void RefreshInteractText()
    {
        bool complete = GoHuntingTask.Instance != null && GoHuntingTask.Instance.IsComplete;
        interactText = complete ? InteractTextComplete : InteractTextDefault;
    }
}
