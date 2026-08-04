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

    [Header("Item Restriction")]
    [Tooltip("When non-empty, only a held item whose PickableItemData is in this list can be placed on this board (e.g. a mail slot should only accept the Small Package item, not any pickable). Leave empty to accept any item, matching the previous unrestricted behavior.")]
    [SerializeField] private PickableItemData[] acceptedItems;

    /// <summary>
    /// True if this board has no item restriction (accepts anything), or the given item is in
    /// its <see cref="acceptedItems"/> list. Checked by <see cref="PlayerInteractionController"/>
    /// both for showing the aim-ghost preview and for actually committing a placement.
    /// </summary>
    public bool AcceptsItem(PickableItemData item)
    {
        if (acceptedItems == null || acceptedItems.Length == 0) return true;
        if (item == null) return false;

        for (int i = 0; i < acceptedItems.Length; i++)
        {
            if (acceptedItems[i] == item) return true;
        }

        return false;
    }

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
