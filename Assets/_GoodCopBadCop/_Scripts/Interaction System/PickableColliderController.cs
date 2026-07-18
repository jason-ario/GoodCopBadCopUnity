using System.Linq;
using UnityEngine;

/// <summary>
/// Manages the trigger state of physics colliders on a <see cref="PickableObject"/>.
///
/// While held   → colliders are triggers: the carried item passes through world geometry
///                without physically blocking the player or catching on door frames.
/// While free   → colliders are solid: the object interacts correctly when thrown or dropped.
///
/// Only root-level and child colliders that are NOT already intentional triggers and are NOT
/// <see cref="InteractableCollider"/> raycast markers are managed — those are owned by
/// <see cref="PickableObject.SetInteractable"/>.
///
/// Integration is handled inside <see cref="PickableObject"/>:
/// • <see cref="SetHeld"/>     — called from OnEquipped (local) and OnHoldingClientChanged (remote).
/// • <see cref="SetReleased"/> — called from OnDropped  (local) and OnHoldingClientChanged (remote).
/// </summary>
public class PickableColliderController : MonoBehaviour
{
    private Collider[] _physicsColliders;

    private void Awake()
    {
        // Collect every collider that is NOT an InteractableCollider raycast marker.
        // Intentional trigger zones (e.g. overlap sensors) are included and reset to
        // non-trigger here — the held/released state is what drives trigger mode.
        _physicsColliders = GetComponentsInChildren<Collider>(true)
            .Where(c => c.GetComponent<InteractableCollider>() == null)
            .ToArray();

        // Ensure solid state at spawn regardless of prefab-default trigger settings.
        SetReleased();
    }

    /// <summary>
    /// Disables all physics colliders while the object is held.
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
    /// Re-enables and restores all physics colliders to solid (non-trigger).
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
            col.enabled = true;
            col.isTrigger = false;
        }
    }
}
