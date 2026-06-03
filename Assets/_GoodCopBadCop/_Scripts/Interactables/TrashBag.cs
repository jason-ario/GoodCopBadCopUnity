using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A trash bag that can be picked up and carried to a DumpsterInteractable.
/// Extends PickableObject to integrate with the existing item hold/drop system.
///
/// Prefab requirements (add in Inspector):
///   - NetworkObject
///   - HighlightEffect  (required by Interactable)
///   - ParentConstraint (required by PickableObject)
///   - Collider on the Interactable layer
///   - PickableItemData ScriptableObject assigned in the "Item Data" field
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
public class TrashBag : PickableObject
{
    // All pickup / drop / networking behaviour is inherited from PickableObject.
    // DumpsterInteractable identifies this type to accept bag deposits.

    /// <summary>
    /// Broadcasts a DOTween throw arc to all clients so onlooker clients also see
    /// the bag fly into the dumpster rather than disappearing from mid-air.
    /// Called by DumpsterInteractable after ReleaseHeldObjectForThrow() on the
    /// throwing client; runs on every client including the thrower.
    /// </summary>
    /// <param name="targetPosition">World-space landing point inside the dumpster.</param>
    /// <param name="jumpHeight">Peak height of the arc above the straight-line path.</param>
    /// <param name="jumpDuration">Total arc duration in seconds.</param>
    /// <param name="ease">DOTween Ease cast to int for RPC serialization.</param>
    [ClientRpc]
    public void PlayThrowArcClientRpc(Vector3 targetPosition, float jumpHeight, float jumpDuration, int ease)
    {
        transform.DOKill();
        transform.DOJump(targetPosition, jumpHeight, numJumps: 1, jumpDuration)
                 .SetEase((Ease)ease);
    }
}
