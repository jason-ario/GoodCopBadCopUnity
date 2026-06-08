using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Server-authoritative state machine that drives a mutant through the suspect lineup.
/// Handles walk-to-booth, rotation, shutter check, climb-through or bang-and-retreat.
/// Keeps MutantEnemy disabled until a successful breakthrough, then hands off to it.
/// Requires: NetworkObject, NavMeshAgent, Animator, MutantEnemy.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MutantSuspectBehaviour : NetworkBehaviour
{
    // ── Config ─────────────────────────────────────────────────────────────────

    [Tooltip("If false, this mutant will never climb through the booth window even when the shutter is open. It will bang on the window frame and retreat instead.")]
    [SerializeField] private bool canClimb = true;

    private MutantIntruderData _data;
    private Transform _standPos;
    private Transform _despawnPos;
    private Transform _climbThroughTargetPos;
    private ShutterController _shutterController;
    private SuspectController _controller;

    // ── Components ─────────────────────────────────────────────────────────────

    private NavMeshAgent _agent;
    private Animator _animator;
    private MutantEnemy _mutantEnemy;

    // ── State ──────────────────────────────────────────────────────────────────

    private Tween _activeTween;
    private bool _isDone;

    private const float ArrivalPollInterval = 0.1f;
    private const float ArrivalTolerance = 0.25f;
    private const float GiveUpPauseDuration = 1f;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();
        _mutantEnemy = GetComponent<MutantEnemy>();

        // Keep MutantEnemy dormant until the lineup phase completes.
        if (_mutantEnemy != null)
            _mutantEnemy.enabled = false;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _activeTween?.Kill();
        StopAllCoroutines();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Begins the full mutant-in-lineup sequence.
    /// Must be called on the server only, immediately after the NetworkObject is spawned.
    /// </summary>
    public void BeginLineup(
        MutantIntruderData data,
        Transform standPos,
        Transform despawnPos,
        Transform climbThroughTargetPos,
        ShutterController shutterController,
        SuspectController controller)
    {
        if (!IsServer) return;

        _data = data;
        _standPos = standPos;
        _despawnPos = despawnPos;
        _climbThroughTargetPos = climbThroughTargetPos;
        _shutterController = shutterController;
        _controller = controller;

        StartCoroutine(LineupSequence());
    }

    // ── Coroutines ─────────────────────────────────────────────────────────────

    private IEnumerator LineupSequence()
    {
        if (!IsServer || _isDone) yield break;

        // Disable the NavMeshAgent during the DOMove walk-in phase so it doesn't
        // steer or override position — mirrors exactly how SuspectController.InitiateSuspect works.
        _agent.enabled = false;

        // Walk to the booth stand position using DOTween (position only, rotation unchanged).
        SetWalkingClientRpc(true);

        bool walkDone = false;
        _activeTween = transform
            .DOMove(_standPos.position, _data.walkInDurationSeconds)
            .OnComplete(() => walkDone = true);

        yield return new WaitUntil(() => walkDone);

        if (_isDone) yield break;

        // Arrived — stop walking, then rotate to face the booth window.
        SetWalkingClientRpc(false);

        bool rotationDone = false;
        _activeTween = transform
            .DORotateQuaternion(_standPos.rotation, 0.5f)
            .OnComplete(() => rotationDone = true);

        yield return new WaitUntil(() => rotationDone);

        // Brief pause before acting.
        yield return new WaitForSeconds(_data.preAttackPauseSeconds);

        if (_isDone) yield break;

        if (canClimb && _shutterController != null && _shutterController.IsOpen)
            yield return StartCoroutine(ClimbThroughSequence());
        else
            yield return StartCoroutine(ShutterBangSequence());
    }

    /// <summary>Climbs through the open booth window into the player area, then enables MutantEnemy.</summary>
    private IEnumerator ClimbThroughSequence()
    {
        if (!IsServer || _isDone) yield break;

        TriggerAnimClientRpc(_data.climbAnimationTrigger);

        // Disable agent so DOTween can move freely across the counter (off-mesh).
        _agent.enabled = false;

        bool moveDone = false;
        _activeTween = transform
            .DOMove(_climbThroughTargetPos.position, _data.climbDurationSeconds)
            .OnComplete(() => moveDone = true);

        yield return new WaitUntil(() => moveDone);

        if (_isDone) yield break;

        // Stop the walking animation — MutantEnemy drives its own Speed parameter from here on.
        SetWalkingClientRpc(false);

        // Re-enable the NavMeshAgent so ChaseLoop can place the agent on the mesh.
        // One frame is required for the agent to link onto the NavMesh surface after
        // being re-enabled at the new position; without it isOnNavMesh returns false.
        _agent.enabled = true;
        yield return null;

        // Enable MutantEnemy on all clients (animation sync) and hand off server control.
        EnableMutantEnemyClientRpc();

        if (_mutantEnemy != null)
        {
            _mutantEnemy.enabled = true;
            _mutantEnemy.InitialiseServer();
        }

        _isDone = true;
        _controller?.OnMutantIntruderComplete(this, brokeThrough: true);
    }

    /// <summary>
    /// Attacks the closed shutter window.
    /// Climbing mutants bang a fixed number of times, climb through opportunistically if the shutter opens, then retreat.
    /// Non-climbing mutants attack for a fixed duration, then lose interest and retreat.
    /// </summary>
    private IEnumerator ShutterBangSequence()
    {
        if (!IsServer || _isDone) yield break;

        if (!canClimb)
        {
            // Release the lineup slot right away — this mutant stays at the window as a persistent threat
            // while the rest of the shift continues normally.
            _controller?.OnMutantIntruderComplete(this, brokeThrough: false, staysAtWindow: true);

            // Attack the window until the loses-interest timer expires.
            float endTime = Time.time + _data.losesInterestAfterSeconds;
            while (!_isDone && Time.time < endTime)
            {
                SetAttackClientRpc(true);
                HitShutterClientRpc();
                yield return new WaitForSeconds(_data.attackAnimDurationSeconds);
                SetAttackClientRpc(false);
                yield return new WaitForSeconds(Mathf.Max(0f, _data.bangIntervalSeconds - _data.attackAnimDurationSeconds));
            }

            if (_isDone) yield break;

            // Lost interest — clear the attack state and retreat.
            SetAttackClientRpc(false);
            yield return StartCoroutine(RetreatingSequence(notifyController: false));
            yield break;
        }

        // Climbing mutant: bang a fixed number of times, checking each cycle for an opened shutter.
        for (int i = 0; i < _data.shutterBangCount; i++)
        {
            if (_isDone) yield break;

            // Shutter opened mid-bang — break through immediately.
            if (_shutterController != null && _shutterController.IsOpen)
            {
                yield return StartCoroutine(ClimbThroughSequence());
                yield break;
            }

            SetAttackClientRpc(true);
            HitShutterClientRpc();
            yield return new WaitForSeconds(_data.attackAnimDurationSeconds);
            SetAttackClientRpc(false);
            yield return new WaitForSeconds(Mathf.Max(0f, _data.bangIntervalSeconds - _data.attackAnimDurationSeconds));
        }

        if (_isDone) yield break;

        // Give up: brief pause, then retreat.
        yield return new WaitForSeconds(GiveUpPauseDuration);

        if (_isDone) yield break;

        yield return StartCoroutine(RetreatingSequence(notifyController: true));
    }

    /// <summary>
    /// Rotates to face the despawn point, walks back to it, then either calls
    /// <see cref="SuspectController.OnMutantIntruderComplete"/> (climbing mutants that gave up)
    /// or despawns directly (non-climbing mutants whose lineup slot was already released).
    /// </summary>
    private IEnumerator RetreatingSequence(bool notifyController)
    {
        if (_isDone) yield break;

        // Rotate to face the despawn direction before walking.
        Vector3 toSpawn = _despawnPos.position - transform.position;
        toSpawn.y = 0f;
        if (toSpawn.sqrMagnitude > 0.001f)
        {
            bool rotDone = false;
            _activeTween = transform
                .DORotateQuaternion(Quaternion.LookRotation(toSpawn.normalized), 0.5f)
                .OnComplete(() => rotDone = true);
            yield return new WaitUntil(() => rotDone);
        }

        if (_isDone) yield break;

        SetWalkingClientRpc(true);

        // Re-enable the NavMeshAgent for retreat pathfinding (was disabled during walk-in).
        _agent.enabled = true;
        yield return null; // One frame for the agent to link onto the NavMesh.

        if (_agent.isOnNavMesh)
        {
            _agent.SetDestination(_despawnPos.position);

            while (_agent.pathPending || _agent.remainingDistance > _agent.stoppingDistance + ArrivalTolerance)
                yield return new WaitForSeconds(ArrivalPollInterval);

            _agent.ResetPath();
        }

        SetWalkingClientRpc(false);
        _isDone = true;

        if (notifyController)
        {
            // Climbing mutant gave up — let the controller clean up and queue the next suspect.
            _controller?.OnMutantIntruderComplete(this, brokeThrough: false);
        }
        else
        {
            // Non-climbing mutant — controller was already notified; just despawn.
            if (NetworkObject.IsSpawned)
                NetworkObject.Despawn();
        }
    }

    // ── ClientRpcs ─────────────────────────────────────────────────────────────

    /// <summary>Sets the Walking animator bool on all clients.</summary>
    [ClientRpc]
    private void SetWalkingClientRpc(bool walking)
    {
        if (_animator != null)
            _animator.SetBool("Walking", walking);
    }

    /// <summary>Sets the Attack animator bool on all clients. Used to drive the shutter-attack animation.</summary>
    [ClientRpc]
    private void SetAttackClientRpc(bool attacking)
    {
        if (_animator != null)
            _animator.SetBool("Attack", attacking);
    }

    /// <summary>Fires an animator trigger on all clients.</summary>
    [ClientRpc]
    private void TriggerAnimClientRpc(string trigger)
    {
        if (_animator != null && !string.IsNullOrEmpty(trigger))
            _animator.SetTrigger(trigger);
    }

    /// <summary>Enables MutantEnemy on all clients so chase animations and logic activate.</summary>
    [ClientRpc]
    private void EnableMutantEnemyClientRpc()
    {
        if (_mutantEnemy != null)
            _mutantEnemy.enabled = true;
    }

    /// <summary>Triggers hit feedback (sound + shake) on the shutter for all clients.</summary>
    [ClientRpc]
    private void HitShutterClientRpc()
    {
        ShutterController.Instance?.OnHitByMutant();
    }
}
