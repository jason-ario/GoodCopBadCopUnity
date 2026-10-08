using UnityEngine;

/// <summary>
/// Marker: every collider on this GameObject or its children is see-through for weapons
/// (pistol/shotgun hitscan, tracers, melee line of sight). Use on open-mesh geometry such as
/// decorative chain-link fence segments that aren't a <see cref="PerimiterFence"/>.
/// See <see cref="CombatHitUtility.IsBulletPassthrough"/>.
/// </summary>
[DisallowMultipleComponent]
public class BulletPassthrough : MonoBehaviour
{
}
