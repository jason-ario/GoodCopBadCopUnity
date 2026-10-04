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

    [Header("Patrol")]
    [Tooltip("When no player is in detection range, the mutant wanders randomly instead of standing still.")]
    public bool enablePatrol = true;

    [Tooltip("Maximum distance from the spawn position the mutant will wander while patrolling.")]
    public float patrolRadius = 12f;

    [Tooltip("Minimum seconds the mutant idles at a patrol waypoint before choosing the next one.")]
    public float patrolWaitMin = 2f;

    [Tooltip("Maximum seconds the mutant idles at a patrol waypoint before choosing the next one.")]
    public float patrolWaitMax = 5f;

    [Header("Aggro")]
    [Tooltip("Probability (0–1) that this mutant spawns in aggro mode and heads straight toward the aggro target, ignoring detection radius.")]
    [Range(0f, 1f)]
    public float aggroChance = 0.25f;

    [Header("Lose Interest")]
    [Tooltip("When true, a mutant that has been stuck at an unreachable target (e.g. banging on the closed booth " +
             "door) for Lose Interest Delay seconds gives up and patrols the yard instead.")]
    public bool enableLoseInterest = true;

    [Tooltip("Seconds of being stuck at an unreachable target (door banging / partial path) before giving up.")]
    [Min(1f)]
    public float loseInterestDelay = 5f;

    [Tooltip("After giving up on a player, the mutant only re-notices them once they are reachable again " +
             "(e.g. left the booth) AND within this distance. Being hurt bypasses the distance check briefly.")]
    [Min(0f)]
    public float renoticeRadius = 10f;

    [Tooltip("Minimum distance from the abandoned target (the booth) that give-up patrol waypoints are picked at.")]
    [Min(0f)]
    public float giveUpPatrolMinDistance = 8f;

    [Tooltip("Maximum distance from the abandoned target (the booth) that give-up patrol waypoints are picked at.")]
    [Min(1f)]
    public float giveUpPatrolMaxDistance = 24f;

    [Header("Fence Combat")]
    [Tooltip("Damage dealt to a PerimiterFence per melee hit. Tune alongside the fence's Max Health.")]
    public float fenceDamagePerHit = 15f;

    [Tooltip("Distance from the fence surface at which the mutant stops moving and begins swinging. " +
             "Should be less than attackRange — the OverlapSphere detects the fence first, then the " +
             "mutant closes in until this surface distance is reached.")]
    public float fenceStopDistance = 1f;

    [Header("Health")]
    [Tooltip("Starting health points for this enemy.")]
    public float maxHealth = 60f;

    [Header("Knockback")]
    [Tooltip("Distance (world units) the mutant is shoved back on a survived melee or gunshot hit.")]
    public float knockbackDistance = 0.6f;

    [Tooltip("Seconds the knockback shove takes to play out before the mutant resumes normal movement.")]
    public float knockbackDuration = 0.25f;
}
