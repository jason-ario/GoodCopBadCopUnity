using System;
using System.Collections;
using System.Collections.Generic;
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
    /// <summary>
    /// Fired on the server just before this mutant is removed from play — either by fleeing
    /// and despawning, or by dying. Subscribe to this to defer any post-breakthrough logic
    /// (e.g. queuing the next suspect) until the mutant is truly out of the scene.
    /// </summary>
    public event Action OnRemovedFromPlay;

    // ── Static events ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fired on the server when any <see cref="MutantEnemy"/> instance dies.
    /// <see cref="KillMutantTask"/> and other systems subscribe to this to detect scripted
    /// mutant kills without needing a direct reference to the instance.
    /// </summary>
    public static event Action OnAnyMutantKilled;

    /// <summary>
    /// Fired on the server the first time any <see cref="MutantEnemy"/> instance acquires a
    /// player target (i.e. transitions from idle/patrol to actively chasing).
    /// <see cref="FollowTrailThreat"/> subscribes while the follow-trail task is active so that
    /// encountering a pack mutant early skips straight to the kill-mutants task.
    /// </summary>
    public static event Action OnAnyMutantSpottedPlayer;

    // ── Configuration ─────────────────────────────────────────────────────────

    [SerializeField] private MutantEnemyData data;

    [Header("Animation (optional)")]
    [SerializeField] private Animator animator;
    [SerializeField] private string speedParameterName = "Speed";
    [SerializeField] private string groundedParameterName = "Grounded";
    [SerializeField] private string attackBoolName = "Attack";
    [SerializeField] private string deathBoolName = "Death";
    [Tooltip("Bool parameter set to true whenever the agent is moving. Derived from the synced Speed value — no extra NetworkVariable required.")]
    [SerializeField] private string runningBoolName = "Running";

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

    [Header("Gore")]
    [Tooltip("Collider used to compute random surface spawn points for gore pieces. Defaults to a CapsuleCollider or, failing that, any Collider found on this GameObject.")]
    [SerializeField] private Collider goreCollider;

    [Tooltip("Random gore chunk/giblet prefabs that can pop out of this mutant on hit and burst out on death. Leave empty to disable gore entirely.")]
    [SerializeField] private GameObject[] goreDropPrefabs;

    [Tooltip("Chance (0-1) that a gore piece pops out each time this mutant takes damage and survives the hit.")]
    [Range(0f, 1f)]
    [SerializeField] private float goreDropChanceOnHit = 0.3f;

    [Tooltip("Minimum and maximum number of gore pieces spawned when a hit gore chance roll succeeds.")]
    [SerializeField] private Vector2Int goreHitCountRange = new Vector2Int(1, 1);

    [Tooltip("Minimum and maximum random pop speed (units/sec) applied to a gore piece spawned from a survived hit.")]
    [SerializeField] private Vector2 goreHitSpeedRange = new Vector2(1.5f, 4f);

    [Tooltip("Minimum and maximum number of gore pieces spawned in the burst when this mutant dies.")]
    [SerializeField] private Vector2Int deathGoreBurstCountRange = new Vector2Int(4, 8);

    [Tooltip("Minimum and maximum random pop speed (units/sec) applied to each gore piece spawned in the death burst.")]
    [SerializeField] private Vector2 deathGoreBurstSpeedRange = new Vector2(3f, 7f);

    [Tooltip("Seconds before a spawned gore piece is automatically destroyed.")]
    [Min(0f)]
    [SerializeField] private float goreLifetime = 10f;

    [Header("Aggro Target")]
    [Tooltip("If false, this mutant will never head for the booth on its own, regardless of the aggro chance roll.")]
    [SerializeField] private bool canAggro = true;

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

    [Header("Sounds")]
    [Tooltip("Clips played spatially at random when this mutant takes a hit and survives.")]
    [SerializeField] private AudioClip[] _hurtSounds;

    [Tooltip("Clips played spatially at random while this mutant is actively chasing a player.")]
    [SerializeField] private AudioClip[] _chaseSounds;

    [Tooltip("Minimum seconds between random chase screams.")]
    [Min(0.5f)]
    [SerializeField] private float _chaseScreamIntervalMin = 5f;

    [Tooltip("Maximum seconds between random chase screams.")]
    [Min(0.5f)]
    [SerializeField] private float _chaseScreamIntervalMax = 15f;

    [Tooltip("Seconds to fade out chase music when this mutant dies or flees.")]
    [Min(0f)]
    [SerializeField] private float _chaseMusicFadeOutSeconds = 2f;

    [Header("Flee Behaviour")]
    [Tooltip("When enabled, reaching zero health triggers a rapid flee-and-despawn instead of a normal death. " +
             "Intended for fully-mutated civilian variants that cannot be permanently killed in the world.")]
    [SerializeField] private bool fleeInsteadOfDie = false;

    [Tooltip("NavMesh movement speed during the flee phase. Should be noticeably faster than normal move speed.")]
    [Min(1f)]
    [SerializeField] private float fleeSpeed = 12f;

    [Tooltip("Seconds after beginning the flee before the mutant force-despawns regardless of distance.")]
    [Min(1f)]
    [SerializeField] private float fleeDespawnTimeout = 8f;

    [Header("Deferred Initialisation")]
    [Tooltip("When false, InitialiseServer() is NOT called automatically on OnNetworkSpawn. " +
             "Use this when the enemy lives on a SuspectCharacter prefab and should only activate " +
             "after its booth cutscene completes. Call InitialiseServer() manually via SuspectCharacter.BeginMutantBehavior().")]
    [SerializeField] private bool _autoInitialiseOnSpawn = true;

    // ── State ──────────────────────────────────────────────────────────────────

    private NavMeshAgent _agent;
    private Transform _currentTarget;
    private float _health;
    private float _attackCooldownTimer;
    private float _doorOpenCooldownTimer;
    private float _chaseScreamTimer;
    private bool _isDead;

    // Patrol & aggro state (server only)
    private Vector3 _spawnPosition;
    private bool _isAggroed;
    private bool _forceAggro;
    private bool _patrolWaiting;
    private float _patrolWaitTimer;
    private PerimiterFence _fenceTarget;
    private DoorController _doorTarget;

    // Destination deduplication — prevents redundant SetDestination calls for stationary
    // targets, which cause a 1-frame path-recalculation stutter every retarget tick.
    private Vector3 _lastSetDestination = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    private object _lastDestinationTarget;
    private const float DestinationChangeSqrThreshold = 0.25f; // skip re-set if target moved < 0.5 m

    /// <summary>
    /// True once this enemy has died, regardless of whether it has been despawned yet.
    /// </summary>
    public bool IsDead => _isDead;

    /// <summary>
    /// Valid only once <see cref="OnRemovedFromPlay"/> has fired. True when this enemy's removal
    /// was a genuine permanent death (normal kill, or a fire kill on a flee-instead-of-die unit).
    /// False when it was a flee-and-despawn (fleeInsteadOfDie unit killed by non-fire damage).
    /// <see cref="SuspectCharacter"/> reads this inside its OnRemovedFromPlay handler to decide
    /// whether to register or clear the legacy-mutant record for this suspect.
    /// </summary>
    public bool DiedPermanently { get; private set; }

    // Synced animator speed so non-owners see movement blend correctly
    private readonly NetworkVariable<float> _networkSpeed = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Synced animator grounded state so non-owners see the correct grounded blend
    private readonly NetworkVariable<bool> _networkGrounded = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();

        if (goreCollider == null)
            goreCollider = GetComponent<CapsuleCollider>() as Collider ?? GetComponent<Collider>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && _autoInitialiseOnSpawn)
        {
            InitialiseServer();
        }

        // All clients track the synced speed and grounded state for animation
        _networkSpeed.OnValueChanged += OnNetworkSpeedChanged;
        ApplyAnimatorSpeed(_networkSpeed.Value);

        _networkGrounded.OnValueChanged += OnNetworkGroundedChanged;
        ApplyAnimatorGrounded(_networkGrounded.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _networkSpeed.OnValueChanged -= OnNetworkSpeedChanged;
        _networkGrounded.OnValueChanged -= OnNetworkGroundedChanged;
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

        // Ensure the component is active so Update() and ChaseLoop() run correctly
        // even if the prefab has the script disabled by default.
        enabled = true;

        _agent.speed = data.moveSpeed;
        _agent.angularSpeed = data.angularSpeed;
        _agent.acceleration = data.acceleration;
        _agent.stoppingDistance = data.stoppingDistance;
        _agent.updateRotation = true;
        _agent.isStopped = false;

        _spawnPosition = transform.position;
        _isAggroed = canAggro && aggroTarget != null && (_forceAggro || UnityEngine.Random.value < data.aggroChance);

        _chaseScreamTimer = UnityEngine.Random.Range(_chaseScreamIntervalMin, _chaseScreamIntervalMax);

        StartCoroutine(ChaseLoop());
    }

    /// <summary>
    /// Assigns the Animator reference used for speed and grounded blending.
    /// Call this before <see cref="InitialiseServer"/> when the enemy's Animator lives on a
    /// child that is only activated at runtime (e.g. the Mutated Version mesh on a SuspectCharacter prefab).
    /// </summary>
    public void SetAnimator(Animator a) => animator = a;

    /// <summary>
    /// Prevents <see cref="InitialiseServer"/> from firing automatically during
    /// <see cref="OnNetworkSpawn"/>. Must be called before <see cref="NetworkObject.Spawn"/>
    /// on the server when this enemy lives on a <see cref="SuspectCharacter"/> prefab and must
    /// stay dormant until the booth cutscene ends and
    /// <see cref="SuspectCharacter.BeginMutantBehavior"/> fires.
    /// </summary>
    public void DisableAutoInit() => _autoInitialiseOnSpawn = false;

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

            if (!wasChasing && _currentTarget != null)
                OnAnyMutantSpottedPlayer?.Invoke();

            if (_currentTarget != null)
            {
                // ── Chase ──────────────────────────────────────────────────────
                SetAgentDestination(_currentTarget.position, _currentTarget);

                float distanceToTarget = Vector3.Distance(transform.position, _currentTarget.position);

                if (distanceToTarget <= data.attackRange)
                {
                    TryAttack();
                }
                else if (!_agent.pathPending
                         && _agent.pathStatus != NavMeshPathStatus.PathComplete)
                {
                    TryBangBlockingDoorTowardTarget(_currentTarget);
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

                    // Drop stale door target.
                    if (_doorTarget != null && (!_doorTarget.IsSpawned || !_doorTarget.IsDoorClosed))
                    {
                        _doorTarget = null;
                        InvalidateDestination();
                    }

                    if (_fenceTarget != null)
                    {
                        // Navigate toward the fence — the NavMeshObstacle carved boundary
                        // is the natural stopping point; no manual stop position needed.
                        _agent.isStopped = false;
                        _agent.stoppingDistance = data.stoppingDistance;
                        SetAgentDestination(_fenceTarget.transform.position, _fenceTarget);

                        if (IsFenceTargetInRange())
                            TryAttackFence();
                    }
                    else if (_doorTarget != null)
                    {
                        _agent.isStopped = false;
                        _agent.stoppingDistance = data.stoppingDistance;
                        SetAgentDestination(_doorTarget.transform.position, _doorTarget);

                        float distToDoor = Vector3.Distance(transform.position, _doorTarget.transform.position);
                        if (distToDoor <= data.attackRange)
                            TryBangDoor(_doorTarget);
                    }
                    else
                    {
                        _agent.isStopped = false;
                        _agent.stoppingDistance = data.stoppingDistance;
                        SetAgentDestination(aggroTarget.position, aggroTarget);

                        // Once the agent reaches the booth wall (partial path), find the door.
                        bool agentSettled = _agent.hasPath
                                         && !_agent.pathPending
                                         && _agent.pathStatus != NavMeshPathStatus.PathComplete
                                         && _agent.remainingDistance <= _agent.stoppingDistance + 0.5f;

                        if (agentSettled)
                            _doorTarget = FindNearestBlockingDoor();
                    }
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

    // ── Fence Assault ──────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the NavMesh agent's destination, skipping the call entirely when the target
    /// identity and position have not meaningfully changed since the last call.
    /// Switching to a different target always forces a new <see cref="NavMeshAgent.SetDestination"/> call.
    /// </summary>
    private void SetAgentDestination(Vector3 destination, object targetIdentity = null)
    {
        bool sameTarget = targetIdentity != null && targetIdentity == _lastDestinationTarget;
        if (sameTarget && (destination - _lastSetDestination).sqrMagnitude <= DestinationChangeSqrThreshold)
            return;

        _lastDestinationTarget = targetIdentity;
        _lastSetDestination = destination;
        _agent.SetDestination(destination);
    }

    /// <summary>
    /// Resets destination-deduplication state so the very next <see cref="SetAgentDestination"/>
    /// call always triggers a real <see cref="NavMeshAgent.SetDestination"/>, forcing the agent
    /// to replan its path. Call this whenever a blocking obstacle is removed.
    /// </summary>
    private void InvalidateDestination()
    {
        _lastDestinationTarget = null;
        _lastSetDestination    = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    }

    /// <summary>
    /// Returns true when any collider belonging to <see cref="_fenceTarget"/> overlaps the
    /// attack-range sphere around the mutant. Uses collider overlap so it works correctly
    /// for long fence segments whose transform center may be several metres away.
    /// </summary>
    private bool IsFenceTargetInRange()
    {
        if (_fenceTarget == null) return false;

        Collider[] cols = Physics.OverlapSphere(transform.position, data.attackRange);
        foreach (Collider col in cols)
        {
            if (col.GetComponentInParent<PerimiterFence>() == _fenceTarget)
                return true;
        }
        return false;
    }

    /// <summary>
    /// SphereCasts directly in front of the mutant's movement direction and returns
    /// the first non-passable <see cref="PerimiterFence"/> found within a short distance.
    /// This ensures the mutant only attacks fences that are physically blocking its path.
    /// </summary>
    private PerimiterFence FindBlockingFenceInFront()
    {
        // Cast from chest height forward.
        Vector3 origin = transform.position + Vector3.up * 1f;
        Vector3 direction = transform.forward;
        float distance = 1.5f; // Short range check
        float radius = 0.75f;

        if (Physics.SphereCast(origin, radius, direction, out RaycastHit hit, distance))
        {
            PerimiterFence fence = hit.collider.GetComponentInParent<PerimiterFence>();
            if (fence != null && fence.IsSpawned && !fence.IsPassableByMutant)
                return fence;
        }

        return null;
    }

    /// <summary>
    /// Triggers an attack against the cached fence target if the cooldown has elapsed.
    /// Plays the standard attack animation and schedules the impact hit via <see cref="DelayedFenceHit"/>.
    /// </summary>
    private void TryAttackFence()
    {
        if (Time.time < _attackCooldownTimer || _fenceTarget == null)
            return;

        _attackCooldownTimer = Time.time + data.attackCooldown;

        // Capture the target NOW so DelayedFenceHit doesn't accidentally hit whichever
        // fence happens to be cached when the coroutine executes.
        PerimiterFence target = _fenceTarget;

        TriggerAttackAnimationClientRpc();
        StartCoroutine(DelayedFenceHit(target));
    }

    /// <summary>
    /// Waits for the melee impact frame then fires the hitbox's fence scan — the same OverlapSphere
    /// that the player attack uses, so the hit only registers when the animation reaches the mutant's
    /// arm. Falls back to a centre-distance check when no hitbox is assigned.
    /// </summary>
    private IEnumerator DelayedFenceHit(PerimiterFence fence)
    {
        yield return new WaitForSeconds(attackHitDelay);

        if (_isDead || fence == null || !fence.IsSpawned)
            yield break;

        if (attackHitbox != null)
        {
            attackHitbox.PerformFenceHitScan(data.fenceDamagePerHit, fence);
        }
        else
        {
            // Fallback: centre-distance check when no hitbox component is configured.
            if (Vector3.Distance(transform.position, fence.transform.position) <= data.attackRange)
            {
                // Approximate hit position as the point on the fence closest to this mutant.
                Collider fenceCollider = fence.GetComponentInChildren<Collider>();
                Vector3 hitPosition = fenceCollider != null
                    ? fenceCollider.ClosestPoint(transform.position)
                    : fence.transform.position;
                fence.TakeMutantHitServer(data.fenceDamagePerHit, hitPosition);
            }
        }
    }

    private void Update()
    {
        if (!IsServer || !_agent.isActiveAndEnabled) return;

        // Always sync locomotion state so clients see movement during flee even though _isDead is true.
        _networkSpeed.Value = _agent.velocity.magnitude;
        _networkGrounded.Value = _agent.isOnNavMesh;

        if (_isDead) return;

        // ── Chase Scream ───────────────────────────────────────────────────────
        if (_currentTarget != null)
        {
            _chaseScreamTimer -= Time.deltaTime;
            if (_chaseScreamTimer <= 0f && _chaseSounds != null && _chaseSounds.Length > 0)
            {
                int idx = UnityEngine.Random.Range(0, _chaseSounds.Length);
                PlayChaseSoundClientRpc(idx);
                _chaseScreamTimer = UnityEngine.Random.Range(_chaseScreamIntervalMin, _chaseScreamIntervalMax);
            }
        }
        else
        {
            // Reset to a fresh interval so the first scream fires naturally after acquiring a target.
            _chaseScreamTimer = UnityEngine.Random.Range(_chaseScreamIntervalMin, _chaseScreamIntervalMax);
        }

        // ── Rotation Tracking ──────────────────────────────────────────────────
        // If we are in range to attack something, ensure we rotate to face it 
        // regardless of whether the NavMeshAgent is currently "moving".
        Transform faceTarget = null;
        if (_currentTarget != null && Vector3.Distance(transform.position, _currentTarget.position) <= data.attackRange)
        {
            faceTarget = _currentTarget;
        }
        else if (_fenceTarget != null && IsFenceTargetInRange())
        {
            faceTarget = _fenceTarget.transform;
        }
        else if (_doorTarget != null && Vector3.Distance(transform.position, _doorTarget.transform.position) <= data.attackRange * 1.5f)
        {
            faceTarget = _doorTarget.transform;
        }

        if (faceTarget != null)
        {
            Vector3 toTarget = faceTarget.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(toTarget);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot, data.angularSpeed * Time.deltaTime);
            }
        }

        // ── Aggro / Fence Logic ────────────────────────────────────────────────
        if (_isAggroed && aggroTarget != null)
        {
            if (_fenceTarget == null)
            {
                // Only look for a fence if we are currently moving or trying to move.
                if (_agent.velocity.sqrMagnitude > 0.01f || _agent.hasPath)
                    _fenceTarget = FindBlockingFenceInFront();
            }
            else if (!_fenceTarget.IsSpawned || _fenceTarget.IsPassableByMutant)
            {
                // Fence broken — resume navigation toward the aggro target immediately.
                _fenceTarget = null;
                _agent.isStopped = false;
                _agent.stoppingDistance = data.stoppingDistance;
                InvalidateDestination();
                _agent.SetDestination(aggroTarget.position);
            }
            else if (IsFenceTargetInRange())
            {
                // Stop the agent so it doesn't try to push through the obstacle while attacking.
                _agent.isStopped = true;
                TryAttackFence();
            }
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
    /// Forces this mutant into aggro mode on spawn, bypassing the <see cref="MutantEnemyData.aggroChance"/> roll.
    /// Must be called before <see cref="NetworkObject.Spawn"/> so <see cref="InitialiseServer"/> reads it.
    /// Requires a valid aggro target — pair with <see cref="SetAggroTarget"/>.
    /// </summary>
    public void SetForceAggro(bool forceAggro)
    {
        _forceAggro = forceAggro;
    }

    /// <summary>
    /// Disables this component and stops all running coroutines so that
    /// <see cref="MutantSuspectBehaviour"/> can take exclusive control during a lineup sequence.
    /// Called by <see cref="MutantSuspectBehaviour.BeginLineup"/> before the lineup coroutine starts.
    /// </summary>
    public void SuspendForLineup()
    {
        StopAllCoroutines();
        enabled = false;
    }

    /// <summary>
    /// Finds the nearest player that is alive (PlayerHealth not dead) within detection radius.
    /// Iterates all connected NetworkClients so it works in multiplayer.
    /// Players who are inside a scripted dialogue cutscene are excluded — they cannot be
    /// aggroed while the cutscene holds their controls.
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

            // Do not target players who are locked inside a scripted dialogue cutscene.
            // IsInCutscene is set by the owning client via DialogueChoiceSystem and replicated
            // to the server through a NetworkVariable, so this check is server-authoritative.
            PlayerInstance pi = client.PlayerObject.GetComponent<PlayerInstance>();
            if (pi != null && pi.IsInCutscene)
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
    /// Returns true when an unlocked door was found and opened; false otherwise.
    /// Applies a cooldown to prevent spam.
    /// </summary>
    private bool TryOpenBlockingDoor()
    {
        if (_currentTarget == null) return false;

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
            return true;
        }

        return false;
    }

    /// <summary>
    /// Searches within <see cref="doorDetectionRadius"/> for any physically-closed
    /// <see cref="DoorController"/> (locked or not) in the direction of <paramref name="target"/>,
    /// and bangs on it at the standard attack rate. Used when the chase path is blocked by a
    /// door that cannot be forced open.
    /// </summary>
    private void TryBangBlockingDoorTowardTarget(Transform target)
    {
        if (target == null) return;

        Vector3 toTarget = (target.position - transform.position).normalized;
        Collider[] nearby = Physics.OverlapSphere(transform.position, doorDetectionRadius);

        foreach (Collider col in nearby)
        {
            DoorController door = col.GetComponentInParent<DoorController>();
            if (door == null || !door.IsSpawned || !door.IsDoorClosed)
                continue;

            Vector3 toObstacle = (col.transform.position - transform.position).normalized;
            if (Vector3.Dot(toTarget, toObstacle) <= 0f)
                continue;

            TryBangDoor(door);
            return;
        }
    }

    /// <summary>
    /// Returns the nearest spawned <see cref="DoorController"/> that is physically closed,
    /// regardless of lock state. Used as a fallback when the aggro path is blocked and no
    /// non-passable perimeter fence remains.
    /// </summary>
    private DoorController FindNearestBlockingDoor()
    {
        DoorController[] allDoors = UnityEngine.Object.FindObjectsByType<DoorController>(FindObjectsSortMode.None);

        DoorController nearest = null;
        float nearestSqrDist = float.MaxValue;

        foreach (DoorController door in allDoors)
        {
            if (door == null || !door.IsSpawned || !door.IsDoorClosed)
                continue;

            float sqrDist = (door.transform.position - transform.position).sqrMagnitude;
            if (sqrDist < nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                nearest = door;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Triggers an attack animation against <paramref name="door"/> and schedules the bang
    /// sound at the melee impact frame. Uses the same attack-cooldown timer as player attacks
    /// so the mutant cannot simultaneously pummel both.
    /// </summary>
    private void TryBangDoor(DoorController door)
    {
        if (Time.time < _attackCooldownTimer || door == null)
            return;

        _attackCooldownTimer = Time.time + data.attackCooldown;

        TriggerAttackAnimationClientRpc();
        StartCoroutine(DelayedDoorBang(door));
    }

    /// <summary>
    /// Waits for the melee impact frame, then plays the door-bang sound on all clients.
    /// Validates proximity at impact time so a stale coroutine does not trigger remotely.
    /// </summary>
    private IEnumerator DelayedDoorBang(DoorController door)
    {
        yield return new WaitForSeconds(attackHitDelay);

        if (_isDead || door == null || !door.IsSpawned)
            yield break;

        float distToDoor = Vector3.Distance(transform.position, door.transform.position);
        if (distToDoor <= data.attackRange * 1.5f)
            door.PlayMutantBangClientRpc();
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

        // Do not attack a player who has entered a cutscene since the last ChaseLoop tick
        // (guards the window between FindNearestLivingPlayer clearing the target and the
        // next retarget interval, since _currentTarget can briefly outlive the exclusion).
        PlayerInstance targetPlayer = _currentTarget.GetComponent<PlayerInstance>();
        if (targetPlayer != null && targetPlayer.IsInCutscene)
            return;

        TriggerAttackAnimationClientRpc();

        // Schedule the sphere-cast to fire at the melee impact frame on the server.
        StartCoroutine(DelayedHitScan(data.damagePerHit));
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
        if (animator != null)
        {
            // Use CrossFade to authoritatively force the animator into the attack state.
            // This is more robust for networked one-shots than boolean parameters.
            animator.CrossFade("Mutant Attack", 0.2f, 0, 0f);
        }
    }

    // ── Immobilization ─────────────────────────────────────────────────────────

    /// <summary>
    /// Stops this enemy's NavMeshAgent for <paramref name="duration"/> seconds, then resumes
    /// movement. Server-only; has no effect if the enemy is already dead.
    /// </summary>
    /// <param name="duration">Seconds to keep the agent stopped.</param>
    public void Immobilize(float duration)
    {
        if (!IsServer || _isDead) return;
        StartCoroutine(ImmobilizeCoroutine(duration));
    }

    private IEnumerator ImmobilizeCoroutine(float duration)
    {
        _agent.isStopped = true;
        yield return new WaitForSeconds(duration);
        if (!_isDead)
            _agent.isStopped = false;
    }

    // ── Damage / Death ─────────────────────────────────────────────────────────

    /// <summary>
    /// Apply damage to this enemy. Call from the server (e.g. from a weapon script).
    /// </summary>
    /// <param name="amount">Damage to apply.</param>
    /// <param name="hitPoint">World-space point of impact used to position the hit particle.</param>
    /// <param name="isFireDamage">
    /// True when this damage tick came from <see cref="SetOnFire"/>. Fire damage always kills
    /// permanently, even on units with <see cref="fleeInsteadOfDie"/> enabled — it's the only
    /// way to finish off a fully-mutated resident for good.
    /// </param>
    public void TakeDamage(float amount, Vector3 hitPoint, bool isFireDamage = false)
    {
        if (!IsServer || _isDead)
            return;

        _health -= amount;

        SpawnHitParticleClientRpc(hitPoint);

        if (_health <= 0f)
        {
            Die(isFireDamage);
            return;
        }

        TrySpawnHitGore();

        if (_hurtSounds != null && _hurtSounds.Length > 0)
        {
            int idx = UnityEngine.Random.Range(0, _hurtSounds.Length);
            PlayHurtSoundClientRpc(idx);
        }
    }

    /// <summary>
    /// Rolls <see cref="goreDropChanceOnHit"/> and, on success, pops <see cref="goreHitCountRange"/>
    /// random gore pieces out of random points on <see cref="goreCollider"/>'s surface.
    /// </summary>
    private void TrySpawnHitGore()
    {
        if (goreDropPrefabs == null || goreDropPrefabs.Length == 0)
            return;

        if (UnityEngine.Random.value > goreDropChanceOnHit)
            return;

        SpawnGoreBurst(goreHitCountRange, goreHitSpeedRange);
    }

    /// <summary>
    /// Spawns a burst of <see cref="deathGoreBurstCountRange"/> gore pieces popping outward from
    /// random points on <see cref="goreCollider"/>'s surface. Called once from the server on a
    /// permanent death.
    /// </summary>
    private void SpawnDeathGoreBurst()
    {
        if (goreDropPrefabs == null || goreDropPrefabs.Length == 0)
            return;

        SpawnGoreBurst(deathGoreBurstCountRange, deathGoreBurstSpeedRange);
    }

    /// <summary>
    /// Rolls a random piece count within <paramref name="countRange"/> and builds randomized
    /// spawn data for each piece. Pieces that land inside the Trash Task's yard area are
    /// spawned server-side as real <see cref="JunkItem"/> NetworkObjects that count toward the
    /// task; all other pieces are purely cosmetic and broadcast in a single RPC so every client
    /// spawns the same non-networked result.
    /// </summary>
    private void SpawnGoreBurst(Vector2Int countRange, Vector2 speedRange)
    {
        int count = UnityEngine.Random.Range(countRange.x, countRange.y + 1);
        if (count <= 0)
            return;

        List<Vector3> cosmeticPositions = new List<Vector3>();
        List<int> cosmeticPrefabIndices = new List<int>();
        List<Vector3> cosmeticVelocities = new List<Vector3>();

        for (int i = 0; i < count; i++)
        {
            Vector3 position = GetRandomGoreSpawnPosition();
            int prefabIndex = UnityEngine.Random.Range(0, goreDropPrefabs.Length);
            float speed = UnityEngine.Random.Range(speedRange.x, speedRange.y);
            Vector3 velocity = GetRandomPopVelocity(position, speed);

            if (TakeOutTrashTask.Instance != null && TakeOutTrashTask.Instance.IsPositionInYard(position)
                && SpawnGoreJunkItem(position, prefabIndex, velocity))
            {
                continue;
            }

            cosmeticPositions.Add(position);
            cosmeticPrefabIndices.Add(prefabIndex);
            cosmeticVelocities.Add(velocity);
        }

        if (cosmeticPositions.Count > 0)
            SpawnGoreBurstClientRpc(cosmeticPositions.ToArray(), cosmeticPrefabIndices.ToArray(), cosmeticVelocities.ToArray());
    }

    /// <summary>
    /// Server-side spawn for a gore piece that landed inside the Trash Task's yard area.
    /// Instantiates the prefab, spawns it as a real NetworkObject (so it replicates to every
    /// client like any other <see cref="JunkItem"/>), enables its (pre-attached, disabled)
    /// <see cref="JunkItem"/> component, and registers it with <see cref="TakeOutTrashTask"/>.
    ///
    /// Requires the gore prefab to already have a NetworkObject (registered as a Network Prefab
    /// in the NetworkManager) and a disabled <see cref="JunkItem"/> component, matching the same
    /// pre-attached-but-disabled pattern documented on <see cref="JunkItem"/> for SuspectCharacter
    /// bodies. Returns false (and destroys the instantiated piece) if either is missing, so the
    /// caller can fall back to spawning it as ordinary cosmetic debris instead.
    /// </summary>
    private bool SpawnGoreJunkItem(Vector3 position, int prefabIndex, Vector3 velocity)
    {
        if (!IsServer)
            return false;

        if (goreDropPrefabs == null || prefabIndex < 0 || prefabIndex >= goreDropPrefabs.Length)
            return false;

        GameObject prefab = goreDropPrefabs[prefabIndex];
        if (prefab == null)
            return false;

        GameObject piece = Instantiate(prefab, position, UnityEngine.Random.rotation);
        NetworkObject netObj = piece.GetComponent<NetworkObject>();
        JunkItem junk = piece.GetComponent<JunkItem>();

        if (netObj == null || junk == null)
        {
            Debug.LogWarning("[MutantEnemy] Gore prefab landed in the yard but is missing a NetworkObject " +
                              "and/or a disabled JunkItem component — it must have both to count toward the " +
                              "Trash Task. Falling back to cosmetic debris instead.");
            Destroy(piece);
            return false;
        }

        Rigidbody rb = piece.GetComponent<Rigidbody>();
        if (rb == null)
            rb = piece.AddComponent<Rigidbody>();

        rb.linearVelocity = velocity;
        rb.angularVelocity = UnityEngine.Random.insideUnitSphere * UnityEngine.Random.Range(2f, 6f);

        junk.enabled = true;
        netObj.Spawn(destroyWithScene: true);

        TakeOutTrashTask.Instance?.RegisterExternalJunkItem(netObj);

        return true;
    }

    /// <summary>
    /// Picks a random point on the surface of <see cref="goreCollider"/>. Falls back to this
    /// mutant's position when no collider is assigned.
    /// </summary>
    private Vector3 GetRandomGoreSpawnPosition()
    {
        if (goreCollider == null)
            return transform.position;

        Bounds bounds = goreCollider.bounds;
        Vector3 outsidePoint = bounds.center + Vector3.Scale(UnityEngine.Random.onUnitSphere, bounds.extents) * 2f;
        return goreCollider.ClosestPoint(outsidePoint);
    }

    /// <summary>
    /// Builds a velocity vector pointing outward from this mutant's body (with a bit of upward
    /// bias and random jitter), scaled to the given speed — used to make gore pieces look like
    /// they're popping out of the mutant rather than just falling in place.
    /// </summary>
    private Vector3 GetRandomPopVelocity(Vector3 spawnPosition, float speed)
    {
        Vector3 direction = spawnPosition - transform.position;
        direction.y = Mathf.Max(direction.y, 0.3f);

        if (direction.sqrMagnitude < 0.0001f)
            direction = UnityEngine.Random.onUnitSphere;

        direction = (direction.normalized + UnityEngine.Random.insideUnitSphere * 0.5f).normalized;
        return direction * speed;
    }

    /// <summary>
    /// Disables every Collider on this mutant (and its children) so the corpse stops blocking
    /// navigation, physics, and weapon hit detection after death.
    /// </summary>
    private void DisableColliders()
    {
        foreach (Collider col in GetComponentsInChildren<Collider>(true))
        {
            col.enabled = false;
        }
    }

    [ClientRpc]
    private void DisableCollidersClientRpc()
    {
        DisableColliders();
    }

    [ClientRpc]
    private void SpawnGoreBurstClientRpc(Vector3[] positions, int[] prefabIndices, Vector3[] velocities)
    {
        for (int i = 0; i < positions.Length; i++)
        {
            SpawnGorePiece(positions[i], prefabIndices[i], velocities[i]);
        }
    }

    private void SpawnGorePiece(Vector3 position, int prefabIndex, Vector3 velocity)
    {
        if (goreDropPrefabs == null || prefabIndex < 0 || prefabIndex >= goreDropPrefabs.Length)
            return;

        GameObject prefab = goreDropPrefabs[prefabIndex];
        if (prefab == null)
            return;

        GameObject piece = Instantiate(prefab, position, UnityEngine.Random.rotation);

        Rigidbody rb = piece.GetComponent<Rigidbody>();
        if (rb == null)
            rb = piece.AddComponent<Rigidbody>();

        rb.linearVelocity = velocity;
        rb.angularVelocity = UnityEngine.Random.insideUnitSphere * UnityEngine.Random.Range(2f, 6f);

        Destroy(piece, goreLifetime);
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

    /// <summary>
    /// Resolves the killing blow. Fire damage (<paramref name="killedByFire"/>) always results in a
    /// permanent death, even when <see cref="fleeInsteadOfDie"/> is set — otherwise a
    /// fleeInsteadOfDie unit flees and survives to be re-encountered later.
    /// </summary>
    private void Die(bool killedByFire)
    {
        _isDead = true;
        _agent.ResetPath();
        _agent.enabled = false;
        _networkSpeed.Value = 0f;

        bool permanentDeath = !fleeInsteadOfDie || killedByFire;
        DiedPermanently = permanentDeath;

        if (!permanentDeath)
        {
            // Restore health so IsDead stays true (flee path) but the unit remains functional
            // long enough to run the flee coroutine. _isDead prevents re-entry from TakeDamage.
            _agent.enabled = true;
            StartCoroutine(FleeAndDespawn());
            return;
        }

        // Stop chase music on all clients before the death sequence plays.
        StopChaseMusicClientRpc();

        // Notify any scripted task systems (e.g. KillMutantTask) that this enemy died.
        OnAnyMutantKilled?.Invoke();
        OnRemovedFromPlay?.Invoke();

        // Attempt to drop a MutantBit if the night phase is active.
        MutantThreat.Instance?.TryDropBitAt(transform.position);

        // Pop a burst of gore pieces out of the mutant's body on a permanent kill.
        SpawnDeathGoreBurst();

        // Disable this mutant's colliders on death so the corpse no longer blocks movement,
        // navigation, or weapon hits. Applied locally (server) and broadcast to all clients.
        DisableColliders();
        DisableCollidersClientRpc();

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

    /// <summary>
    /// Flee-and-despawn sequence for fully-mutated civilian mutants that cannot be permanently
    /// killed. The mutant breaks off from its current target, sprints away from the nearest
    /// player at <see cref="fleeSpeed"/>, and despawns after <see cref="fleeDespawnTimeout"/>
    /// seconds regardless of distance. No kill event is fired and no MutantBit is dropped.
    /// </summary>
    private IEnumerator FleeAndDespawn()
    {
        if (!IsServer) yield break;

        DiedPermanently = false;

        // Boost speed, stop any attack animation, and stop chase music on all clients.
        _agent.speed = fleeSpeed;
        SetAttackAnimClientRpc(false);
        SetFleeingClientRpc(true);
        StopChaseMusicClientRpc();

        float elapsed = 0f;

        while (elapsed < fleeDespawnTimeout)
        {
            // Continuously update destination away from the nearest player.
            Transform player = FindNearestLivingPlayer();
            if (player != null)
            {
                Vector3 awayDir = (transform.position - player.position).normalized;
                Vector3 fleeTarget = transform.position + awayDir * 20f;

                // Clamp to NavMesh surface.
                if (UnityEngine.AI.NavMesh.SamplePosition(fleeTarget, out UnityEngine.AI.NavMeshHit hit, 15f, UnityEngine.AI.NavMesh.AllAreas))
                    _agent.SetDestination(hit.position);
            }

            elapsed += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }

        SetFleeingClientRpc(false);
        OnRemovedFromPlay?.Invoke();

        if (IsSpawned)
            NetworkObject.Despawn();
    }

    [ClientRpc]
    private void SetAttackAnimClientRpc(bool attacking)
    {
        if (animator != null && !string.IsNullOrEmpty(attackBoolName))
            animator.SetBool(attackBoolName, attacking);
    }

    [ClientRpc]
    private void SetFleeingClientRpc(bool fleeing)
    {
        // Reuse the Speed parameter — the animator reads it for locomotion blend.
        // Optionally, set a dedicated "Fleeing" bool if the animator has one.
        if (animator != null)
            animator.SetBool("Fleeing", fleeing);
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

    [ClientRpc]
    private void PlayHurtSoundClientRpc(int index)
    {
        if (_hurtSounds == null || index < 0 || index >= _hurtSounds.Length) return;
        AudioClip clip = _hurtSounds[index];
        if (clip != null)
            SFXController.Instance?.PlayAtPosition(clip, transform.position);
    }

    [ClientRpc]
    private void PlayChaseSoundClientRpc(int index)
    {
        if (_chaseSounds == null || index < 0 || index >= _chaseSounds.Length) return;
        AudioClip clip = _chaseSounds[index];
        if (clip != null)
            SFXController.Instance?.PlayAtPosition(clip, transform.position);
    }

    /// <summary>Fades out and stops the looping chase music on all clients.</summary>
    [ClientRpc]
    private void StopChaseMusicClientRpc()
    {
        MusicManager.Instance?.FadeOut(_chaseMusicFadeOutSeconds);
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
        if (animator == null) return;

        if (!string.IsNullOrEmpty(speedParameterName))
            animator.SetFloat(speedParameterName, speed);

        if (!string.IsNullOrEmpty(runningBoolName))
            animator.SetBool(runningBoolName, speed > 0.1f);
    }

    private void OnNetworkGroundedChanged(bool oldValue, bool newValue)
    {
        ApplyAnimatorGrounded(newValue);
    }

    private void ApplyAnimatorGrounded(bool grounded)
    {
        if (animator != null && !string.IsNullOrEmpty(groundedParameterName))
            animator.SetBool(groundedParameterName, grounded);
    }
}
