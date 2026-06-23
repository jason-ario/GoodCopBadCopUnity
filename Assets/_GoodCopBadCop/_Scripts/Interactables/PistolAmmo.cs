using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A pistol ammo clip that can be picked up from a <see cref="Pistol"/> container or found
/// loose in the world.
///
/// All pickup, drop, ownership, and slot logic is inherited from <see cref="PickableObject"/>.
///
/// Prefab requirements:
///   - NetworkObject
///   - NetworkTransform
///   - HighlightEffect  (required by Interactable)
///   - ParentConstraint (required by PickableObject)
///   - Collider on the Interactable layer
///   - "Item Data" field → PistolAmmo.asset
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PistolAmmo : PickableObject
{
}
