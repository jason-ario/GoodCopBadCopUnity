using UnityEngine;

/// <summary>
/// Trigger volume that marks a player as inside or outside the booth.
/// Attach this to a trigger-collider GameObject covering the booth interior.
/// When a player enters, their IsOutside flag is set to false.
/// When a player exits, their IsOutside flag is set to true.
/// </summary>
public class BoothBoundaryTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        var player = other.GetComponentInParent<PlayerInstance>();
        if (player != null && player.IsLocalPlayer)
        {
            player.RequestSetIsOutside(false);

            // Dismiss the booth-waiting notification when the player returns.
            if (UIController.Instance != null)
                UIController.Instance.HideBoothWaitingNotification();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        var player = other.GetComponentInParent<PlayerInstance>();
        if (player != null && player.IsLocalPlayer)
            player.RequestSetIsOutside(true);
    }
}
