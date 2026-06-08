/// <summary>
/// A mutant bit that can be picked up and deposited in the PostBox.
/// Dropped by mutants when the Go Hunting task is active.
///
/// Extends PickableObject to integrate with the existing item hold/drop system.
///
/// Prefab requirements (add in Inspector):
///   - NetworkObject
///   - NetworkTransform
///   - HighlightEffect  (required by Interactable)
///   - ParentConstraint (required by PickableObject)
///   - Collider on the Interactable layer
///   - PickableItemData ScriptableObject assigned in the "Item Data" field
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
public class MutantBit : PickableObject
{
    // All pickup / drop / networking behaviour is inherited from PickableObject.
    // PostBox identifies this type to accept deposits for the Go Hunting task.
}
