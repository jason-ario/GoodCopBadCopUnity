using System.Collections;
using UnityEngine;

public class InkStampPickup : PickableObject
{
    public StampContainer.StampType StampType;

    /// <summary>Ink stamps always spawn at their authored location; they are not saved or restored.</summary>
    public override bool IsPersistedInSave => false;

    /// <summary>
    /// The stamp is only ever grabbed through its <see cref="InkStamp"/> slot. SetReleased re-enables
    /// colliders on every holder release (including the remote NV update), which would let the pickup
    /// steal the interaction raycast from the slot. Re-apply the networked lock whenever it is released
    /// and manual pickup is disallowed on this machine.
    /// </summary>
    protected override void OnHolderCollidersRefreshed(bool isHeld)
    {
        if (!isHeld && !CanPickUpManually)
            ApplyNetworkInteractableState();
    }

    /// <summary>Same re-lock for the local drop path, which calls SetReleased before the NV round-trip.</summary>
    public override void OnDropped()
    {
        base.OnDropped();
        if (!CanPickUpManually)
            ApplyNetworkInteractableState();
    }
}
