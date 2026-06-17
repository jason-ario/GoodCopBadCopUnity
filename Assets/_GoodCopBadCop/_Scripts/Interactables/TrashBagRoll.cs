using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A trash bag roll that dispenses individual <see cref="TrashBag"/>s on demand.
///
/// Press E near the roll to extract one bag directly into your hands.
/// LMB (empty-handed) picks up the roll so it can be carried to a desired location.
/// After all bags are extracted the roll despawns automatically.
///
/// All extraction logic, networked item count, sound playback, and despawn are inherited
/// from <see cref="ContainerPickableObject"/>. This class only provides the reticle label.
///
/// Prefab requirements:
///   - NetworkObject
///   - NetworkTransform
///   - HighlightEffect  (required by Interactable)
///   - ParentConstraint (required by PickableObject)
///   - Collider on the Interactable layer
///   - "Item Data" field → <c>Trash Bag Roll.asset</c>  (PickableItemData for the roll itself)
///   - "_containedItemData" field → <c>Trash Bag.asset</c>  (item dispensed on extraction)
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class TrashBagRoll : ContainerPickableObject
{
    protected override string BuildInteractText(int itemsRemaining)
        => $"Extract Trash Bag ({itemsRemaining} left)";
}
