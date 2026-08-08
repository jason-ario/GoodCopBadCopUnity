using System.Linq;
using UnityEngine;

/// <summary>
/// Manages the trigger state of physics colliders on a <see cref="PickableObject"/>.
///
/// While held   → colliders are disabled: the carried item passes through world geometry
///                without physically blocking the player or catching on door frames, and
///                without blocking the player's interaction raycasts.
/// While free   → colliders are solid: the object interacts correctly when thrown or dropped.
///
/// Only root-level and child colliders that are NOT already intentional triggers and are NOT
/// <see cref="InteractableCollider"/> raycast markers are managed — those are owned by
/// <see cref="PickableObject.SetInteractable"/>.
///
/// Colliders on GameObjects that implement <see cref="IClickable"/> (e.g. the checkbox trigger
/// on exam-page checklist items) are also excluded: they must stay enabled while the item is
/// held so the <see cref="ClickDetector"/> can raycast through them.
///
/// Integration is handled inside <see cref="PickableObject"/>:
/// • <see cref="SetHeld"/>     — called from OnEquipped (local) and OnHoldingClientChanged (remote).
/// • <see cref="SetReleased"/> — called from OnDropped  (local) and OnHoldingClientChanged (remote).
/// </summary>
public class PickableColliderController : MonoBehaviour
{
    private Collider[] _physicsColliders;

    // Null when this controller belongs to the folder itself; non-null when it belongs to a
    // document (ID card, Application, exam page) picked up on its own outside a folder.
    private FolderItem _ownFolderItem;

    private void Awake()
    {
        _ownFolderItem = GetComponent<FolderItem>();

        // Collect every collider that is NOT an InteractableCollider raycast marker and NOT
        // a click-detection trigger (IClickable on the same GameObject).
        // Colliders on IClickable objects must stay enabled while held so the ClickDetector
        // can still raycast through them; they don't cause physical obstruction because they
        // are already triggers and don't interact with geometry.
        // When this controller belongs to the folder itself (_ownFolderItem == null), skip any
        // collider belonging to a filed FolderItem (ID card, Application, exam pages): those
        // are parented under the folder while filed, so a naive GetComponentsInChildren scan
        // here would otherwise pick them up and force-solidify them whenever the folder is
        // held/released — clobbering the open/closed/held state FolderController manages
        // directly. A document's own controller must still manage its own root collider when
        // picked up standalone, so this exclusion only ever applies to the folder's controller.
        _physicsColliders = GetComponentsInChildren<Collider>(true)
            .Where(c => c.GetComponent<InteractableCollider>() == null
                     && c.GetComponent<IClickable>() == null
                     && (_ownFolderItem != null || c.GetComponentInParent<FolderItem>() == null))
            .ToArray();

        // Ensure solid state at spawn regardless of prefab-default trigger settings.
        SetReleased();
    }

    /// <summary>
    /// Disables all managed physics colliders while the object is held.
    /// Disabled colliders are invisible to all raycasts regardless of layer mask or
    /// QueryTriggerInteraction mode, preventing the held item from blocking the
    /// player's interaction raycast (e.g. placing a paper into a folder).
    /// Also prevents physical contact with world geometry while carried.
    /// </summary>
    public void SetHeld()
    {
        foreach (Collider col in _physicsColliders)
        {
            if (col == null) continue;
            col.enabled = false;
        }
    }

    /// <summary>
    /// Re-enables and restores all managed physics colliders to solid (non-trigger).
    /// Does a live scan of the full hierarchy rather than relying solely on the cached array,
    /// so any child colliders that were triggers by default or added after Awake are also caught.
    /// Called when the object is dropped, thrown, or placed so it interacts correctly with the world.
    /// </summary>
    public void SetReleased()
    {
        foreach (Collider col in GetComponentsInChildren<Collider>(true))
        {
            if (col == null) continue;
            if (col.GetComponent<InteractableCollider>() != null) continue;
            if (col.GetComponent<IClickable>() != null) continue;
            // Skip filed documents' colliders when this is the folder's own controller —
            // see the exclusion note in Awake above.
            if (_ownFolderItem == null && col.GetComponentInParent<FolderItem>() != null) continue;
            col.enabled = true;
            col.isTrigger = false;
        }
    }
}
