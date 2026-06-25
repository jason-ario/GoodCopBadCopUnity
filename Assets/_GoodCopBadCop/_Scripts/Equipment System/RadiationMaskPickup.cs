using UnityEngine;

/// <summary>
/// World-pickup for the radiation mask.
/// Behaves like any other pickable item — goes to the player's hand on interact or shop purchase.
/// When held in hand, pressing LMB equips the mask on the player's face.
/// </summary>
public class RadiationMaskPickup : PickableObject
{
    /// <summary>
    /// Called when the player presses LMB while holding this mask in hand.
    /// Equips the mask on the player's face and removes the pickup from hand.
    /// </summary>
    public override void OnStartUse()
    {
        base.OnStartUse();

        if (playerPickupController == null)
        {
            Debug.LogError("[RadiationMaskPickup] OnStartUse called but playerPickupController is null.");
            return;
        }

        PlayerEquipmentController equipmentController =
            playerPickupController.GetComponent<PlayerEquipmentController>();

        if (equipmentController == null)
        {
            Debug.LogError("[RadiationMaskPickup] PlayerEquipmentController not found on the holding player.");
            return;
        }

        equipmentController.EquipMask();

        if (ItemData != null && ItemData.PickupSound != null)
            SFXController.Instance.PlayAtPosition(ItemData.PickupSound, transform.position);

        // Clears all held-item state and despawns the world object.
        playerPickupController.DestroyEquippedItem();
    }
}
