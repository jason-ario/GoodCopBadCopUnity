using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A pistol that acts as a container for <see cref="PistolAmmo"/> clips.
///
/// Press E (empty-handed) near the pistol to extract one ammo clip directly into your hands.
/// LMB (empty-handed) picks up the pistol itself so it can be carried.
/// After all clips are extracted the pistol despawns automatically.
///
/// All extraction logic, networked item count, sound playback, and despawn are inherited
/// from <see cref="ContainerPickableObject"/>. This class only provides the reticle label.
///
/// Prefab requirements:
///   - NetworkObject
///   - NetworkTransform
///   - HighlightEffect  (required by Interactable)
///   - ParentConstraint (required by PickableObject)
///   - Collider on the Interactable layer
///   - "Item Data" field → Pistol.asset              (PickableItemData for the pistol itself)
///   - "_containedItemData" field → PistolAmmo.asset (item dispensed on extraction)
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class Pistol : ContainerPickableObject
{
    private const string ExtractLabel = "Extract Ammo Clip";

    /// <summary>Returns the reticle label showing how many clips remain.</summary>
    protected override string BuildInteractText(int itemsRemaining)
        => $"{ExtractLabel} ({itemsRemaining} left)";
}
