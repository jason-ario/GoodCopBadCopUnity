using UnityEngine;

/// <summary>
/// Interactable component for the final destination of the "Follow the Trail" threat.
/// When interacted with, it notifies the FollowTrailThreat system that the investigation is complete.
/// </summary>
public class TrailDestinationInteractable : Interactable
{
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        
        // Notify the threat system. We'll implement FollowTrailThreat as a singleton.
        if (FollowTrailThreat.Instance != null)
        {
            FollowTrailThreat.Instance.OnDestinationDiscovered();
        }
    }
}
