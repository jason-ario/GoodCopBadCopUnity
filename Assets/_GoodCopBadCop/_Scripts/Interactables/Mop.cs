/// <summary>
/// A mop that can be picked up and used to scrub graffiti off checkpoint walls.
/// Used as the required tool by <see cref="GraffitiInteractable"/> for the Clean Graffiti task.
///
/// All pickup, drop, and networking behaviour is inherited from <see cref="PickableObject"/>.
/// <see cref="GraffitiInteractable"/> identifies this type via the cast <c>item is Mop</c>.
///
/// Prefab requirements:
///   - NetworkObject
///   - NetworkTransform
///   - HighlightEffect  (required by Interactable base)
///   - ParentConstraint (required by PickableObject)
///   - Collider on the Interactable layer
///   - <see cref="PickableItemData"/> ScriptableObject assigned in the "Item Data" field
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
public class Mop : PickableObject
{
    // All pickup / drop / networking behaviour is inherited from PickableObject.
    // GraffitiInteractable identifies this type to accept scrub interactions.
}
