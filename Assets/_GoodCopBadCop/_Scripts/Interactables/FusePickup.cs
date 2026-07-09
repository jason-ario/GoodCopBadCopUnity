using UnityEngine;

/// <summary>
/// A circuit-breaker fuse that the player can pick up and carry to the fuse box.
///
/// Each fuse has a <see cref="FuseColor"/> that determines which slot on the
/// <see cref="FuseBoxPuzzleController"/> fuse-box panel will accept it.
/// The color is also visually communicated via a colored <see cref="MeshRenderer"/>
/// material set up in the prefab.
///
/// Setup notes:
///   - Attach to a prefab that already has <see cref="PickableObject"/> requirements
///     (NetworkObject, NetworkTransform, HighlightEffect, ParentConstraint).
///   - Assign a <see cref="PickableItemData"/> in the base <see cref="PickableObject"/>
///     itemData field (one ScriptableObject per color recommended).
///   - Set <see cref="_fuseColor"/> to match the material colour used on the mesh.
/// </summary>
public class FusePickup : PickableObject
{
    [Header("Fuse")]
    [Tooltip("The color of this fuse — must match the expectedColor on the target FuseSlot.")]
    [SerializeField] private FuseColor _fuseColor;

    /// <summary>The color identity of this fuse.</summary>
    public FuseColor FuseColor => _fuseColor;
}
