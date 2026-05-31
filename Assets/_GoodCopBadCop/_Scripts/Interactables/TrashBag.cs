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
}
