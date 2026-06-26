using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A refuel cannister for the <see cref="Flamethrower"/>.
///
/// Pick this up and use it (LMB) on a Flamethrower in the world to fully refill
/// its fuel tank. The cannister is despawned once consumed.
///
/// Prefab requirements:
///   - NetworkObject
///   - NetworkTransform
///   - HighlightEffect   (required by Interactable)
///   - ParentConstraint  (required by PickableObject)
///   - Collider on the Interactable layer
///   - "Item Data" field → Flamethrower Cannister.asset
/// The Flamethrower's "itemsThatCanInteractWith" must include Flamethrower Cannister.asset.
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class FlamethrowerCannister : PickableObject
{
    protected override void Awake()
    {
        base.Awake();
        interactText = "Flamethrower Cannister";
    }
}
