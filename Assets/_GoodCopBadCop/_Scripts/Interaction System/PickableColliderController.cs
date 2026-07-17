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
    /// Switches all physics colliders to trigger mode.
    /// Called when the object is picked up so it no longer physically blocks world geometry.
    /// </summary>
    public void SetHeld()
    {
        foreach (Collider col in _physicsColliders)
        {
            if (col == null) continue;
            col.enabled = true;
            col.isTrigger = true;
        }
    }

    /// <summary>
    /// Reverts all physics colliders to solid (non-trigger).
    /// Does a live scan of the full hierarchy rather than relying solely on the cached array,
    /// so any child colliders that were triggers by default or added after Awake are also caught.
    /// Called when the object is dropped or thrown so it interacts correctly with the world.
    /// </summary>
    public void SetReleased()
    {
        foreach (Collider col in GetComponentsInChildren<Collider>(true))
        {
            if (col == null) continue;
            if (col.GetComponent<InteractableCollider>() != null) continue;
            col.isTrigger = false;
        }
    }
}
