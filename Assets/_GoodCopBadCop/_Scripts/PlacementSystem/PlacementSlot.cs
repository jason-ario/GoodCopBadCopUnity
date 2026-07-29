using UnityEngine;

/// <summary>
/// A specialized <see cref="PlacementBoard"/> that snaps the held/placed object to an exact
/// fixed pose (this component's <see cref="SnapPoint"/> position and rotation) instead of
/// following the raycast hit point and surface normal like a generic placement surface.
///
/// Generic free-surface placement (see <see cref="PlayerInteractionController.CheckActivatePlacer"/>)
/// positions the ghost at wherever the aim raycast lands and orients it to the surface normal —
/// fine for setting something down on a table, but wrong for a receptacle like a mail bin's
/// opening or a mail cubby slot: aiming slightly off-center (e.g. at the side wall of the bin)
/// makes the object appear to stick sideways to that wall instead of dropping into the slot.
///
/// Attaching a <see cref="PlacementSlot"/> to the receptacle's trigger collider (or a nearby
/// collider, since it is found the same way as any other <see cref="PlacementBoard"/> — directly
/// under the raycast hit or within the placement snap radius) forces the ghost/placed object to
/// always snap to <see cref="SnapPoint"/> regardless of exactly where on the receptacle the
/// player is aiming, while still respecting the normal in-range/out-of-range placement feedback.
///
/// Setup: attach to the same GameObject as (or a child of) the receptacle's trigger collider.
/// Optionally assign <see cref="_snapPoint"/> to a dedicated child Transform positioned/rotated
/// exactly where the object should end up; if left unassigned, this component's own Transform is
/// used.
/// </summary>
[AddComponentMenu("Good Cop Bad Cop/Placement Slot")]
public class PlacementSlot : PlacementBoard
{
    [Tooltip("Exact pose the placed object should snap to. Defaults to this GameObject's own Transform if unassigned.")]
    [SerializeField] private Transform _snapPoint;

    /// <summary>The exact pose the placed object should snap to.</summary>
    public Transform SnapPoint => _snapPoint != null ? _snapPoint : transform;

    private void OnDrawGizmos()
    {
        Transform snap = SnapPoint;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(snap.position, Vector3.one * 0.1f);
        Gizmos.DrawLine(snap.position, snap.position + snap.up * 0.25f);
    }
}
