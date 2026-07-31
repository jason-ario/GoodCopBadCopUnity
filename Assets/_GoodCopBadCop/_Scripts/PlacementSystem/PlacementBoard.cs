using System;
using UnityEngine;

public class PlacementBoard : MonoBehaviour
{
    [SerializeField] private bool isHanging;
    
    public bool IsHanging => isHanging;

    [Header("Aim-to-Preview")]
    [Tooltip("When true, the placement ghost (via ObjectPlacer) is shown automatically as soon as " +
             "the player's reticle aims at this board while holding a compatible item — no need to " +
             "hold RMB first. The reticle also switches to its interact (green) hover state while " +
             "aiming. Intended for well-defined receptacles with an unambiguous drop-off point (e.g. " +
             "a mail cubby's PlacementSlot), where showing the ghost on every free-surface hover would " +
             "be noisy but showing it for this one specific target is exactly the guidance the player " +
             "needs.")]
    [SerializeField] private bool showGhostWhileAiming;

    /// <summary>See <see cref="showGhostWhileAiming"/>.</summary>
    public bool ShowGhostWhileAiming => showGhostWhileAiming;

    [Tooltip("Reticle hint text shown while aiming at this board with ShowGhostWhileAiming enabled and holding an item.")]
    [SerializeField] private string aimHoverText = "Place Item";

    /// <summary>Reticle hint text used by <see cref="showGhostWhileAiming"/>.</summary>
    public string AimHoverText => aimHoverText;

    /// <summary>
    /// Fired locally whenever an item is successfully placed on this board.
    /// Subscribe from tutorial systems that need to react to a specific board being used.
    /// </summary>
    public event Action<PickableObject> OnItemPlaced;

    public virtual void OnPlaced(PickableObject pickableObject)
    {
        Debug.Log("On Placed");
        OnItemPlaced?.Invoke(pickableObject);
    }
}
