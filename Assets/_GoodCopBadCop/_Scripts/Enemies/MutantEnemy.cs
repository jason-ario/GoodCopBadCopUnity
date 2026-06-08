using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public enum DeathBehaviour { Destroy, PlayAnimation }

/// <summary>
/// Server-authoritative mutant NPC.
/// Chases the nearest living player and attacks them when within range.
/// Requires: NetworkObject, NavMeshAgent, Animator (optional).
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MutantEnemy : NetworkBehaviour
{
    // ── Configuration ─────────────────────────────────────────────────────────

    [SerializeField] private MutantEnemyData data;

    [Header("Animation (optional)")]
    [SerializeField] private Animator animator;
    [SerializeField] private string speedParameterName = "Speed";
    [SerializeField] private string attackBoolName = "Attack";
    [SerializeField] private string deathBoolName = "Death";

    [Header("Attack Hitbox")]
    [Tooltip("Hitbox component used to sphere-cast at the melee hit frame.")]
    [SerializeField] private MutantAttackHitbox attackHitbox;

    [Tooltip("Delay in seconds from the start of the attack animation to the melee impact frame.")]
    [SerializeField] private float attackHitDelay = 0.4f;

    [Header("Door Interaction")]
    [Tooltip("Radius within which the mutant detects and forces open doors or gates blocking its NavMesh path.")]
    [SerializeField] private float doorDetectionRadius = 3f;

    [Tooltip("Minimum time in seconds between consecutive door-open attempts.")]
    [SerializeField] private float doorOpenCooldownDuration = 3f;

    [Header("Hit Feedback")]
    [Tooltip("Particle prefab instantiated on all clients at the point of impact when this enemy is hit.")]
    [SerializeField] private GameObject hitParticlePrefab;

    [Header("Aggro Target")]
    [Tooltip("Transform the mutant will move toward when aggroed (e.g. the booth). Can also be assigned at runtime via SetAggroTarget().")]
    [SerializeField] private Transform aggroTarget;

    [Header("Death")]
    [Tooltip("Destroy: despawns immediately on death. PlayAnimation: triggers the death animation then despawns after a delay.")]
    [SerializeField] private DeathBehaviour deathBehaviour = DeathBehaviour.Destroy;

    [Tooltip("Seconds to wait after triggering the death animation before despawning. Only used when Death Behaviour is PlayAnimation.")]
    [Min(0f)]
    [SerializeField] private float deathAnimationDuration = 2f;

    [Tooltip("Sound played on all clients when this enemy dies.")]
    [SerializeField] private AudioClip deathSound;

    // ── State ──────────────────────────────────────────────────────────────────

    private NavMeshAgent _agent;
    private Transform _currentTarget;
    private float _health;
    private float _attackCooldownTimer;
    private float _doorOpenCooldownTimer;
    private bool _isDead;

    // Patrol & aggro state (server only)
    private Vector3 _spawnPosition;
    private bool _isAggroed;
    private bool _patrolWaiting;
    private float _patrolWaitTimer;

    /// <summary>
    /// True once this enemy has died, regardless of whether it has been despawned yet.
    /// </summary>
    public bool IsDead => _isDead;

    // Synced animator speed so non-owners see movement blend correctly
    private readonly NetworkVariable<float> _networkSpeed = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // Skip auto-initialization when MutantSuspectBehaviour is present.
            // It will call InitialiseServer() manually after the lineup sequence completes.
            if (GetComponent<MutantSuspectBehaviour>() == null)
                InitialiseServer();
        }

        // All clients track the synced speed for animation
        _networkSpeed.OnValueChanged += OnNetworkSpeedChanged;
        ApplyAnimatorSpeed(_networkSpeed.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _networkSpeed.OnValueChanged -= OnNetworkSpeedChanged;
    }

    /// <summary>
    /// Sets up health, NavMeshAgent settings, and starts the chase loop.
    /// Called automatically from OnNetworkSpawn on the server, unless a MutantSuspectBehaviour
    /// component is present — in that case it is called manually after lineup breakthrough.
    /// </summary>
    public void InitialiseServer()
    {
        if (data == null)
        {
            Debug.LogError($"[MutantEnemy] No MutantEnemyData assigned on {gameObject.name}. Despawning.", this);
            NetworkObject.Despawn();
            return;
        }

        _health = data.maxHealth;

        _agent.speed = data.moveSpeed;
        _agent.angularSpeed = data.angularSpeed;
        _agent.acceleration = data.acceleration;
        _agent.stoppingDistance = data.stoppingDistance;

        _spawnPosition = transform.position;
        _isAggroed = aggroTarget != null && UnityEngine.Random.value < data.aggroChance;

        StartCoroutine(ChaseLoop());
    }

    // ── Server Loops ───────────────────────────────────────────────────────────

    /// <summary>
    /// Periodically re-evaluates the nearest player target and issues NavMesh destinations.
    /// Priority order per tick: Chase > Aggro toward target > Patrol > Idle.
    /// Runs only on the server.
    /// </summary>
    private IEnumerator ChaseLoop()
    {
        const float retargetInterval = 0.5f;

        // Wait one frame for the NavMeshAgent to place itself on the NavMesh surface
        // after being instantiated at runtime. Without this, SetDestination silently
        // fails on freshly spawned agents that haven't yet been linked to the mesh.
        yield return null;

        while (!_isDead)
        {
            if (!_agent.isOnNavMesh)
            {
                yield return new WaitForSeconds(retargetInterval);
                continue;
            }

            bool wasChasing = _currentTarget != null;
            _currentTarget = FindNearestLivingPlayer();

            if (_currentTarget != null)
            {
                // ── Chase ──────────────────────────────────────────────────────
                _agent.SetDestination(_currentTarget.position);

                float distanceToTarget = Vector3.Distance(transform.position, _currentTarget.position);

                if (distanceToTarget <= data.attackRange)
                {
                    TryAttack();
                }
                else if (!_agent.pathPending
                         && _agent.pathStatus != NavMeshPathStatus.PathComplete
                         && Time.time >= _doorOpenCooldownTimer)
                {
                    TryOpenBlockingDoor();
                }
            }
            else
            {
                // Lost or never had a player target; reset patrol state so we
                // pick a new waypoint immediately rather than waiting out a stale timer.
                if (wasChasing)
                    _patrolWaiting = false;

                if (_isAggroed && aggroTarget != null)
                {
                    // ── Aggro ──────────────────────────────────────────────────
                    _agent.SetDestination(aggroTarget.position);
                }
                else if (data.enablePatrol)
                {
                    // ── Patrol ─────────────────────────────────────────────────
                    UpdatePatrol();
                }
                else
                {
                    // ── Idle ───────────────────────────────────────────────────
                    _agent.ResetPath();
                }
            }

            yield return new WaitForSeconds(retargetInterval);
        }
    }

    // ── Patrol ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Manages waypoint selection for the patrol state.
    /// Called every <see cref="ChaseLoop"/> tick when no player is in range and the enemy is not aggroed.
    /// </summary>
    private void UpdatePatrol()
    {
        // Keep moving toward the current waypoint if we haven't arrived yet.
        if (_agent.hasPath && _agent.remainingDistance > _agent.stoppingDistance)
            return;

        // Arrived (or no path). Start or continue the idle wait before the next waypoint.
        if (!_patrolWaiting)
        {
            _patrolWaiting = true;
            _patrolWaitTimer = Time.time + UnityEngine.Random.Range(data.patrolWaitMin, data.patrolWaitMax);
            _agent.ResetPath();
        }

        if (Time.time >= _patrolWaitTimer)
        {
            _patrolWaiting = false;
            TrySetPatrolDestination();
        }
    }

    /// <summary>
    /// Samples a random reachable NavMesh point within <see cref="MutantEnemyData.patrolRadius"/>
    /// of the spawn position and sets it as the agent destination.
    /// </summary>
    private void TrySetPatrolDestination()
    {
        Vector3 randomOffset = UnityEngine.Random.insideUnitSphere * data.patrolRadius;
        randomOffset.y = 0f;
        Vector3 candidate = _spawnPosition + randomOffset;

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, data.patrolRadius, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);
    }

    private void Update()
    {
        // Smooth speed sync from server every frame on the server
        if (IsServer && !_isDead && _agent.isActiveAndEnabled)
        {
            _networkSpeed.Value = _agent.velocity.magnitude;
        }
    }

    // ── Targeting ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns the aggro target at runtime (e.g. called by the spawner before <see cref="NetworkObject.Spawn"/>).
    /// If <paramref name="target"/> is non-null and the aggro chance roll succeeds in
    /// <see cref="InitialiseServer"/>, this mutant will head toward the target instead of patrolling.
    /// </summary>
    public void SetAggroTarget(Transform target)
    {
        aggroTarget = target;
    }

    /// <summary>
    /// Finds the nearest player that is alive (PlayerHealth not dead) within detection radius.
    /// Iterates all connected NetworkClients so it works in multiplayer.
    /// </summary>
    private Transform FindNearestLivingPlayer()
    {
        Transform nearest = null;
        float nearestSqrDist = data.detectionRadius * data.detectionRadius;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
                continue;

            PlayerHealth health = client.PlayerObject.GetComponent<PlayerHealth>();
            if (health == null || health.IsDead)
                continue;

            float sqrDist = (client.PlayerObject.transform.position - transform.position).sqrMagnitude;
            if (sqrDist < nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                nearest = client.PlayerObject.transform;
            }
        }

        return nearest;
    }

    // ── Door Interaction ───────────────────────────────────────────────────────

    /// <summary>
    /// Searches for an <see cref="IMutantPassable"/> obstacle within <see cref="doorDetectionRadius"/>
    /// that lies roughly between the mutant and its current target, and forces it open.
    /// Only opens one obstacle per call; applies a cooldown to prevent spam.
    /// </summary>
    private void TryOpenBlockingDoor()
    {
        if (_currentTarget == null) return;

        Vector3 toTarget = (_currentTarget.position - transform.position).normalized;
        Collider[] nearby = Physics.OverlapSphere(transform.position, doorDetectionRadius);

        foreach (Collider col in nearby)
        {
            // Walk up the hierarchy in case the collider is on a child of the door/gate.
            IMutantPassable passable = col.GetComponentInParent<IMutantPassable>();
            if (passable == null || !passable.IsBlockingMutant)
                continue;

            // Only open obstacles that are in the general direction of the target.
            Vector3 toObstacle = (col.transform.position - transform.position).normalized;
            if (Vector3.Dot(toTarget, toObstacle) <= 0f)
                continue;

            passable.OpenForMutant();
            _doorOpenCooldownTimer = Time.time + doorOpenCooldownDuration;
            return;
        }
    }

    // ── Attack ─────────────────────────────────────────────────────────────────

    private void TryAttack()
    {
        if (Time.time < _attackCooldownTimer)
            return;

        _attackCooldownTimer = Time.time + data.attackCooldown;

        if (_currentTarget == null)
            return;

        PlayerHealth targetHealth = _currentTarget.GetComponent<PlayerHealth>();
        if (targetHealth == null || targetHealth.IsDead)
            return;

        TriggerAttackAnimationClientRpc();

        // Schedule the sphere-cast to fire at the melee impact frame on the server.
        StartCoroutine(DelayedHitScan(data.damagePerHit));

        // Reset the Attack bool after the attack cooldown so the animator returns to locomotion.
        StartCoroutine(ResetAttackBoolAfterDelay(data.attackCooldown));
    }

    /// <summary>
    /// Waits for the attack animation to finish (approximated by the cooldown duration),
    /// then clears the Attack bool on all clients so the animator transitions back to locomotion.
    /// </summary>
    private IEnumerator ResetAttackBoolAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        ResetAttackAnimationClientRpc();
    }

    [ClientRpc]
    private void ResetAttackAnimationClientRpc()
    {
        if (animator != null && !string.IsNullOrEmpty(attackBoolName))
            animator.SetBool(attackBoolName, false);
    }

    /// <summary>
    /// Waits for the animation's melee point, then runs the hitbox sphere-cast on the server.
    /// </summary>
    private IEnumerator DelayedHitScan(float damage)
    {
        yield return new WaitForSeconds(attackHitDelay);

        if (_isDead || attackHitbox == null)
            yield break;

        attackHitbox.PerformHitScan(damage);
    }

    [ClientRpc]
    private void TriggerAttackAnimationClientRpc()
    {
        if (animator != null && !string.IsNullOrEmpty(attackBoolName))
            animator.SetBool(attackBoolName, true);
    }

    // ── Damage / Death ─────────────────────────────────────────────────────────

    /// <summary>
    /// Apply damage to this enemy. Call from the server (e.g. from a weapon script).
    /// </summary>
    /// <param name="amount">Damage to apply.</param>
    /// <param name="hitPoint">World-space point of impact used to position the hit particle.</param>
    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (!IsServer || _isDead)
            return;

        _health -= amount;

        SpawnHitParticleClientRpc(hitPoint);

        if (_health <= 0f)
            Die();
    }

    [ClientRpc]
    private void SpawnHitParticleClientRpc(Vector3 position)
    {
        if (hitParticlePrefab == null)
            return;

        GameObject fx = Instantiate(hitParticlePrefab, position, Quaternion.identity);

        if (fx.GetComponentInChildren<AutoDestroy>() == null)
            fx.AddComponent<AutoDestroy>();
    }

    private void Die()
    {
        _isDead = true;
        _agent.ResetPath();
        _agent.enabled = false;
        _networkSpeed.Value = 0f;

        if (deathBehaviour == DeathBehaviour.PlayAnimation)
        {
            TriggerDeathAnimationClientRpc();
            //StartCoroutine(DespawnAfterDelay(deathAnimationDuration));
        }
        else
        {
            PlayDeathSoundClientRpc();

            if (IsSpawned)
                NetworkObject.Despawn();
        }
    }

    [ClientRpc]
    private void TriggerDeathAnimationClientRpc()
    {
        if (animator != null && !string.IsNullOrEmpty(deathBoolName))
            animator.SetBool(deathBoolName, true);

        if (deathSound != null)
            SFXController.Instance.Play(deathSound);
    }

    [ClientRpc]
    private void PlayDeathSoundClientRpc()
    {
        if (deathSound != null)
            SFXController.Instance.Play(deathSound);
    }

    private IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (IsSpawned)
            NetworkObject.Despawn();
    }

    // ── Animation Sync ─────────────────────────────────────────────────────────

    private void OnNetworkSpeedChanged(float oldValue, float newValue)
    {
        ApplyAnimatorSpeed(newValue);
    }

    private void ApplyAnimatorSpeed(float speed)
    {
        if (animator != null && !string.IsNullOrEmpty(speedParameterName))
            animator.SetFloat(speedParameterName, speed);
    }
}
