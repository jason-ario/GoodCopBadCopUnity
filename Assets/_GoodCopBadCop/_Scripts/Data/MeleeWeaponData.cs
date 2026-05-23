using UnityEngine;

/// <summary>
/// ScriptableObject that holds tunable stats for a melee weapon.
/// Create one asset per weapon type and assign it in the Inspector.
/// </summary>
[CreateAssetMenu(fileName = "NewMeleeWeaponData", menuName = "GoodCopBadCop/Melee Weapon Data")]
public class MeleeWeaponData : ScriptableObject
{
    [Tooltip("Damage dealt to an enemy per successful hit.")]
    [Min(0f)]
    public float damagePerHit = 25f;

    [Tooltip("Seconds after OnStartUse() until the hitbox sweep fires.")]
    [Min(0f)]
    public float hitDelay = 1f;

    [Tooltip("Radius of the OverlapSphere used to detect enemies.")]
    [Min(0f)]
    public float hitRadius = 0.8f;

    [Tooltip("Tag used to identify enemy GameObjects. Must match the enemy prefab tag.")]
    public string enemyTag = "Enemy";

    [Header("Durability")]
    [Tooltip("Total number of hits the weapon can land (on enemies or environment) before breaking.")]
    [Min(1)]
    public int maxDurability = 10;

    [Tooltip("How much durability is lost per hit.")]
    [Min(1)]
    public int durabilityLossPerHit = 1;
}
