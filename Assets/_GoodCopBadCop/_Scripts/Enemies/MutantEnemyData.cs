using UnityEngine;

/// <summary>
/// ScriptableObject that defines all tunable parameters for a mutant enemy variant.
/// Create via: Assets > Create > GoodCopBadCop > Enemy > Mutant Enemy Data
/// </summary>
[CreateAssetMenu(menuName = "GoodCopBadCop/Enemy/Mutant Enemy Data", fileName = "NewMutantEnemyData")]
public class MutantEnemyData : ScriptableObject
{
    [Header("Movement")]
    [Tooltip("NavMesh movement speed in units per second.")]
    public float moveSpeed = 3.5f;

    [Tooltip("Angular speed for NavMeshAgent rotation.")]
    public float angularSpeed = 120f;

    [Tooltip("NavMeshAgent acceleration.")]
    public float acceleration = 8f;

    [Tooltip("Distance at which the agent stops and begins attacking.")]
    public float stoppingDistance = 1.8f;

    [Header("Attack")]
    [Tooltip("Damage dealt to the target's PlayerHealth per hit.")]
    public float damagePerHit = 10f;

    [Tooltip("Seconds between consecutive attacks.")]
    public float attackCooldown = 1.5f;

    [Tooltip("Maximum range (world units) within which the enemy can attack a player.")]
    public float attackRange = 2f;

    [Header("Detection")]
    [Tooltip("Radius used to locate the nearest player when picking a chase target.")]
    public float detectionRadius = 30f;

    [Header("Health")]
    [Tooltip("Starting health points for this enemy.")]
    public float maxHealth = 60f;
}
