using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Server-authoritative combat reaction for guard soldiers. While idle at their post, scans for
/// nearby hostile <see cref="MutantEnemy"/> instances; when one comes within <see cref="_detectionRadius"/>,
/// navigates to within <see cref="_engageDistance"/> of it using the existing <see cref="NavMeshAgent"/>,
/// stops, aims the rifle, and fires — damaging the mutant exactly like a player's shot
/// (<see cref="MutantEnemy.TakeDamage"/>). Returns to its original post position/rotation and resumes
/// idling once no hostile mutant remains nearby.
///
/// Requires: NavMeshAgent, SuspectCharacter (for the shared Animator and death state) on the same
/// GameObject. Reuses the "Walking", "Aiming Rifle", and "FiringRifle" Animator parameters that
/// already drive the soldier's rifle animations.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(SuspectCharacter))]
public class SoldierMutantResponder : NetworkBehaviour
{
    private enum State { Idle, Pursuing, Engaging, Returning }

    [Header("Detection")]
    [Tooltip("Hostile mutants within this range of the soldier are noticed.")]
    [SerializeField] private float _detectionRadius = 30f;

    [Tooltip("How often (seconds) to rescan for the nearest hostile mutant.")]
    [SerializeField] private float _scanInterval = 0.5f;

    [Header("Engagement")]
    [Tooltip("Distance the soldier stops at from the mutant before firing.")]
    [SerializeField] private float _engageDistance = 20f;

    [Tooltip("Extra distance beyond _engageDistance the mutant must move before the soldier " +
             "resumes chasing instead of holding position and firing.")]
    [SerializeField] private float _reengageBuffer = 3f;

    [Tooltip("Delay after stopping before the rifle is raised and the shot fires.")]
    [SerializeField] private float _aimDelay = 0.5f;

    [Tooltip("How long the 'FiringRifle' animation plays for a single shot before the rifle is lowered again.")]
    [SerializeField] private float _fireAnimDuration = 0.25f;

    [Tooltip("Seconds to wait, rifle lowered, between one shot and the next aim-and-fire cycle.")]
    [SerializeField] private float _pauseBetweenShots = 2.5f;

    [Tooltip("Damage dealt per shot, applied via MutantEnemy.TakeDamage just like a player's shot.")]
    [SerializeField] private float _damagePerShot = 10f;

    [Header("Movement")]
    [SerializeField] private float _moveSpeed = 3f;
    [SerializeField] private float _angularSpeed = 360f;
    [Tooltip("Degrees/second the soldier turns to face its target or its post.")]
    [SerializeField] private float _turnSpeed = 480f;

    [Header("Muzzle VFX / SFX")]
    [Tooltip("Particle system at the rifle's muzzle, played on every shot — same VFX used by the Pistol prefab.")]
    [SerializeField] private ParticleSystem _shootVFX;

    [Tooltip("Gunshot sound played on every shot — same clip used by the Pistol prefab.")]
    [SerializeField] private AudioClip _shootSound;

    [Tooltip("Volume the gunshot sound plays at.")]
    [SerializeField] private float _shootSoundVolume = 1f;

    [Tooltip("Dedicated AudioSource (child of this soldier) the gunshot sound is played through via PlayOneShot.")]
    [SerializeField] private AudioSource _shootAudioSource;

    [Header("Health")]
    [Tooltip("Health this soldier has against mutant attacks (separate from the interrogation-flow health on SuspectCharacter, which is gated to the booth).")]
    [SerializeField] private float _maxHealth = 100f;

    [Tooltip("Seconds to wait after death (letting the blood/death animation play) before notifying " +
             "the Guard Purchase Point so the guard can be re-purchased. No-op if this soldier isn't " +
             "a child of a GuardPurchasePoint.")]
    [SerializeField] private float _deathNotifyDelay = 3f;

    private float _health;

    private NavMeshAgent _agent;
    private SuspectCharacter _suspect;
    private MutantEnemy _selfMutantEnemy;

    private Vector3 _postPosition;
    private Quaternion _postRotation;

    private State _state = State.Idle;
    private MutantEnemy _currentTarget;
    private Coroutine _combatRoutine;
    private float _scanTimer;
    private bool _isWalkingAnim;

    private void Awake()
    {
        // The source VFX prefab (shared with the Pistol) has stopAction = Destroy, which would
        // destroy the muzzle flash GameObject after its first shot. Override it to None so the
        // same particle system can be replayed on every shot, exactly like Pistol.Awake does.
        if (_shootVFX != null)
        {
            ParticleSystem.MainModule main = _shootVFX.main;
            main.stopAction = ParticleSystemStopAction.None;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer)
        {
            enabled = false;
            return;
        }

        _agent = GetComponent<NavMeshAgent>();
        _suspect = GetComponent<SuspectCharacter>();
        _selfMutantEnemy = GetComponent<MutantEnemy>();

        _health = _maxHealth;

        _postPosition = transform.position;
        _postRotation = transform.rotation;

        _agent.speed = _moveSpeed;
        _agent.angularSpeed = _angularSpeed;
        _agent.stoppingDistance = _engageDistance;
        _agent.updateRotation = false;
        _agent.enabled = true;
    }

    public override void OnNetworkDespawn()
    {
        StopCombatRoutine();
        base.OnNetworkDespawn();
    }

    /// <summary>True while this soldier can still be targeted/damaged by mutants.</summary>
    public bool IsAlive => _suspect != null && !_suspect.IsDead;

    /// <summary>
    /// Damages this soldier exactly like a mutant's attack on the player. Server-only.
    /// Kills the soldier (reusing <see cref="SuspectCharacter.KillAsGuard"/> for death visuals)
    /// once health reaches zero, then notifies its Guard Purchase Point (if any) so the post
    /// can be re-purchased.
    /// </summary>
    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (!IsServer || !IsAlive)
            return;

        _health -= amount;

        if (_health <= 0f)
            Die();
    }

    private void Die()
    {
        StopCombatRoutine();

        if (_agent != null)
        {
            _agent.isStopped = true;
            _agent.enabled = false;
        }

        _suspect.KillAsGuard();

        StartCoroutine(NotifyPurchasePointAfterDelay());
    }

    private IEnumerator NotifyPurchasePointAfterDelay()
    {
        yield return new WaitForSeconds(_deathNotifyDelay);

        GuardPurchasePoint purchasePoint = GetComponentInParent<GuardPurchasePoint>();
        purchasePoint?.NotifyGuardDied();
    }

    private void Update()
    {
        if (!IsServer || _agent == null || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh)
            return;

        if (_suspect != null && _suspect.IsDead)
        {
            if (_state != State.Idle)
            {
                StopCombatRoutine();
                _agent.isStopped = true;
                _state = State.Idle;
            }
            return;
        }

        _scanTimer -= Time.deltaTime;
        if (_scanTimer <= 0f)
        {
            _scanTimer = _scanInterval;
            ScanForTarget();
        }

        switch (_state)
        {
            case State.Pursuing:
                HandlePursuing();
                break;
            case State.Engaging:
                HandleEngaging();
                break;
            case State.Returning:
                HandleReturning();
                break;
        }

        UpdateWalkingAnim();
    }

    /// <summary>Finds the nearest active, living hostile mutant within range and drives state transitions.</summary>
    private void ScanForTarget()
    {
        MutantEnemy best = null;
        float bestDist = _detectionRadius;

        MutantEnemy[] mutants = FindObjectsByType<MutantEnemy>(FindObjectsSortMode.None);
        foreach (MutantEnemy m in mutants)
        {
            if (m == null || m == _selfMutantEnemy || m.IsDead || !m.IsActive)
                continue;

            // Mutants currently standing at the booth window are mid-interrogation (a scripted,
            // contained encounter) — ignore them so soldiers don't rush the booth and interrupt it.
            SuspectCharacter mutantSuspect = m.GetComponent<SuspectCharacter>();
            if (mutantSuspect != null && mutantSuspect.IsAtBooth)
                continue;

            float dist = Vector3.Distance(transform.position, m.transform.position);
            if (dist <= bestDist)
            {
                bestDist = dist;
                best = m;
            }
        }

        _currentTarget = best;

        if (_currentTarget == null)
        {
            if (_state == State.Pursuing || _state == State.Engaging)
            {
                StopCombatRoutine();
                BeginReturning();
            }
        }
        else if (_state == State.Idle || _state == State.Returning)
        {
            _agent.stoppingDistance = _engageDistance;
            _agent.isStopped = false;
            _state = State.Pursuing;
        }
    }

    private void HandlePursuing()
    {
        if (_currentTarget == null)
        {
            BeginReturning();
            return;
        }

        _agent.SetDestination(_currentTarget.transform.position);
        FaceDirection(_currentTarget.transform.position);

        float dist = Vector3.Distance(transform.position, _currentTarget.transform.position);
        if (!_agent.pathPending && dist <= _engageDistance + 1f)
        {
            _agent.isStopped = true;
            _state = State.Engaging;
            if (_combatRoutine == null)
                _combatRoutine = StartCoroutine(EngageRoutine());
        }
    }

    private void HandleEngaging()
    {
        if (_currentTarget == null || _currentTarget.IsDead)
        {
            StopCombatRoutine();
            BeginReturning();
            return;
        }

        FaceDirection(_currentTarget.transform.position);

        float dist = Vector3.Distance(transform.position, _currentTarget.transform.position);
        if (dist > _engageDistance + _reengageBuffer)
        {
            StopCombatRoutine();
            _agent.isStopped = false;
            _state = State.Pursuing;
        }
    }

    /// <summary>Sends the agent back toward its original post position/rotation and enters State.Returning.</summary>
    private void BeginReturning()
    {
        _currentTarget = null;
        _agent.stoppingDistance = 0.15f;
        _agent.isStopped = false;
        _agent.SetDestination(_postPosition);
        _state = State.Returning;
    }

    private void HandleReturning()
    {
        if (_currentTarget != null)
        {
            _agent.stoppingDistance = _engageDistance;
            _agent.isStopped = false;
            _state = State.Pursuing;
            return;
        }

        if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.2f &&
            _agent.velocity.sqrMagnitude < 0.02f)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, _postRotation, _turnSpeed * Time.deltaTime);
            if (Quaternion.Angle(transform.rotation, _postRotation) < 1f)
            {
                transform.rotation = _postRotation;
                _state = State.Idle;
            }
        }
    }

    /// <summary>Rotates smoothly toward <paramref name="worldPoint"/> on the horizontal plane.</summary>
    private void FaceDirection(Vector3 worldPoint)
    {
        Vector3 dir = worldPoint - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            return;

        Quaternion look = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _turnSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Raises the rifle, fires a single shot, lowers the rifle, then pauses for
    /// <see cref="_pauseBetweenShots"/> before repeating — until the target dies, flees out of
    /// range, or disappears.
    /// </summary>
    private IEnumerator EngageRoutine()
    {
        Animator animator = _suspect.animator;

        while (_state == State.Engaging && _currentTarget != null && !_currentTarget.IsDead)
        {
            if (animator != null)
                animator.SetBool("Aiming Rifle", true);

            yield return new WaitForSeconds(_aimDelay);

            if (_state != State.Engaging || _currentTarget == null || _currentTarget.IsDead)
                break;

            if (animator != null)
                animator.SetBool("FiringRifle", true);

            FireAtCurrentTarget();

            yield return new WaitForSeconds(_fireAnimDuration);

            if (animator != null)
            {
                animator.SetBool("FiringRifle", false);
                animator.SetBool("Aiming Rifle", false);
            }

            yield return new WaitForSeconds(_pauseBetweenShots);
        }

        if (animator != null)
        {
            animator.SetBool("FiringRifle", false);
            animator.SetBool("Aiming Rifle", false);
        }

        _combatRoutine = null;
    }

    /// <summary>Hitscan shot toward the current target, damaging it exactly like a player's pistol shot, plus muzzle VFX/SFX.</summary>
    private void FireAtCurrentTarget()
    {
        if (_currentTarget == null)
            return;

        Vector3 origin = transform.position + Vector3.up * 1.5f;
        Vector3 aimPoint = _currentTarget.transform.position + Vector3.up * 1f;
        Vector3 direction = (aimPoint - origin).normalized;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, _detectionRadius + 5f))
        {
            MutantEnemy hitEnemy = hit.collider.GetComponentInParent<MutantEnemy>();
            if (hitEnemy == _currentTarget)
            {
                hitEnemy.TakeDamage(_damagePerShot, hit.point, knockbackDirection: direction);
            }
        }

        PlayShootFx();
        PlayShootFxClientRpc();
    }

    /// <summary>Plays the muzzle particle and gunshot sound locally.</summary>
    private void PlayShootFx()
    {
        if (_shootVFX != null)
        {
            _shootVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _shootVFX.Play();
        }

        if (_shootSound != null && _shootAudioSource != null)
        {
            _shootAudioSource.PlayOneShot(_shootSound, _shootSoundVolume);
        }
    }

    /// <summary>Replicates the muzzle VFX/SFX to every client (the server already played it locally above).</summary>
    [ClientRpc]
    private void PlayShootFxClientRpc()
    {
        if (IsServer) return;
        PlayShootFx();
    }

    private void StopCombatRoutine()
    {
        if (_combatRoutine != null)
        {
            StopCoroutine(_combatRoutine);
            _combatRoutine = null;
        }

        if (_suspect != null && _suspect.animator != null)
        {
            _suspect.animator.SetBool("FiringRifle", false);
            _suspect.animator.SetBool("Aiming Rifle", false);
        }
    }

    private void UpdateWalkingAnim()
    {
        bool moving = !_agent.isStopped && _agent.velocity.sqrMagnitude > 0.05f;
        if (moving == _isWalkingAnim)
            return;

        _isWalkingAnim = moving;
        if (_suspect != null && _suspect.animator != null)
            _suspect.animator.SetBool("Walking", moving);
    }
}
