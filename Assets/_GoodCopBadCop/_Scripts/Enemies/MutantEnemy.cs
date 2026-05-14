using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

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
    [SerializeField] private string attackTriggerName = "Attack";
    [SerializeField] private string deathTriggerName = "Death";

    // ── State ──────────────────────────────────────────────────────────────────

    private NavMeshAgent _agent;
    private Transform _currentTarget;
    private float _health;
    private float _attackCooldownTimer;
    private bool _isDead;

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
            InitialiseServer();

        // All clients track the synced speed for animation
        _networkSpeed.OnValueChanged += OnNetworkSpeedChanged;
        ApplyAnimatorSpeed(_networkSpeed.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _networkSpeed.OnValueChanged -= OnNetworkSpeedChanged;
    }

    private void InitialiseServer()
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

        StartCoroutine(ChaseLoop());
    }

    // ── Server Loops ───────────────────────────────────────────────────────────

    /// <summary>
    /// Periodically re-evaluates the nearest player target and issues NavMesh destinations.
    /// Runs only on the server.
    /// </summary>
    private IEnumerator ChaseLoop()
    {
        const float retargetInterval = 0.5f;

        while (!_isDead)
        {
            _currentTarget = FindNearestLivingPlayer();

            if (_currentTarget != null)
            {
                _agent.SetDestination(_currentTarget.position);
                _networkSpeed.Value = _agent.velocity.magnitude;

                float distanceToTarget = Vector3.Distance(transform.position, _currentTarget.position);

                if (distanceToTarget <= data.attackRange)
                    TryAttack();
            }
            else
            {
                _agent.ResetPath();
                _networkSpeed.Value = 0f;
            }

            yield return new WaitForSeconds(retargetInterval);
        }
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

    // ── Attack ─────────────────────────────────────────────────────────────────

    private void TryAttack()
    {
        if (Time.time < _attackCooldownTimer)
            return;

        _attackCooldownTimer = Time.time + data.attackCooldown;

        if (_currentTarget == null)
            return;

        PlayerHealth targetHealth = _currentTarget.GetComponent<PlayerHealth>();
        if (targetHealth != null && !targetHealth.IsDead)
        {
            targetHealth.TakeDamage(data.damagePerHit);
            TriggerAttackAnimationClientRpc();
        }
    }

    [ClientRpc]
    private void TriggerAttackAnimationClientRpc()
    {
        if (animator != null && !string.IsNullOrEmpty(attackTriggerName))
            animator.SetTrigger(attackTriggerName);
    }

    // ── Damage / Death ─────────────────────────────────────────────────────────

    /// <summary>
    /// Apply damage to this enemy. Call from the server (e.g. from a weapon script).
    /// </summary>
    public void TakeDamage(float amount)
    {
        if (!IsServer || _isDead)
            return;

        _health -= amount;

        if (_health <= 0f)
            Die();
    }

    private void Die()
    {
        _isDead = true;
        _agent.ResetPath();
        _agent.enabled = false;
        _networkSpeed.Value = 0f;

        TriggerDeathAnimationClientRpc();
        StartCoroutine(DespawnAfterDelay(2f));
    }

    [ClientRpc]
    private void TriggerDeathAnimationClientRpc()
    {
        if (animator != null && !string.IsNullOrEmpty(deathTriggerName))
            animator.SetTrigger(deathTriggerName);
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
