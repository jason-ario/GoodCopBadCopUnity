using UnityEngine;

public class InkStampPickup : PickableObject
{
    public StampContainer.StampType StampType;

    /// <summary>Ink stamps always spawn at their authored location; they are not saved or restored.</summary>
    public override bool IsPersistedInSave => false;

    /// <summary>
    /// The stamp itself is never a target for anyone. It is only grabbed and returned through
    /// its <see cref="InkStamp"/> holder, which calls PlayerPickupController.PickUpObject directly.
    /// Decided here rather than by collider state, which network holder callbacks re-enable.
    /// </summary>
    public override bool IsInteractable => false;

    /// <summary>Lets the interaction ray pass through the stamp to reach its holder.</summary>
    public override bool PassesInteractionRayThrough => true;

    /// <summary>Hover highlighting never applies to the stamp itself.</summary>
    public override void Highlight(bool highlight) => base.Highlight(false);
}
