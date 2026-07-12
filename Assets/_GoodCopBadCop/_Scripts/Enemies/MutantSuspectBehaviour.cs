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

    /// <summary>
    /// Optional callback fired on the server when the lineup sequence completes.
    /// Receives true if the mutant broke through, false if it retreated or despawned.
    /// Set this before calling BeginLineup() when bypassing SuspectController (e.g. AlexeiController).
    /// </summary>
    public System.Action<bool> OnSequenceComplete;

    /// <summary>
    /// When true, the mutant despawns immediately after finishing the shutter-bang sequence
    /// instead of walking back to the despawn point. Default false.
    /// Set this before calling BeginAtStandPos() for scripted entrances (e.g. Alexei cutscene).
    /// </summary>
    public bool DespawnInsteadOfRetreat { get; set; }

    private const float ArrivalPollInterval = 0.1f;
    private const float ArrivalTolerance = 0.25f;
    private const float GiveUpPauseDuration = 0.1f;
    private const string ClimbingAnimBool = "climbing";
    private const string BangOnShuttersAnimBool = "BangOnShutters";

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponentInChildren<Animator>();
        _mutantEnemy = GetComponent<MutantEnemy>();
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

        // Suspend the chase loop before it gets a chance to run (it defers one frame),
        // so MutantSuspectBehaviour has exclusive control during the lineup sequence.
        _mutantEnemy?.SuspendForLineup();

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

        SetClimbingClientRpc(true);

        // Disable agent so DOTween can move freely across the counter (off-mesh).
        _agent.enabled = false;

        bool moveDone = false;
        _activeTween = transform
            .DOMove(_climbThroughTargetPos.position, _data.climbDurationSeconds)
            .OnComplete(() => moveDone = true);

        // Keep the climbing bool true for exactly one second as requested.
        yield return new WaitForSeconds(1f);
        SetClimbingClientRpc(false);

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
            // Aggro on the booth itself once inside.
            if (_controller != null)
                _mutantEnemy.SetAggroTarget(_controller.transform);
            
            _mutantEnemy.SetForceAggro(true);
            _mutantEnemy.enabled = true;
            _mutantEnemy.InitialiseServer();
        }

        _isDone = true;
        _controller?.OnMutantIntruderComplete(this, brokeThrough: true);
        OnSequenceComplete?.Invoke(true);
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

            // Lost interest — clear the attack state then either despawn or retreat.
            SetAttackClientRpc(false);

            if (DespawnInsteadOfRetreat)
            {
                _isDone = true;
                OnSequenceComplete?.Invoke(false);
                if (NetworkObject.IsSpawned) NetworkObject.Despawn();
                yield break;
            }

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

        // Give up: brief pause, then either despawn or retreat.
        yield return new WaitForSeconds(GiveUpPauseDuration);

        if (_isDone) yield break;

        if (DespawnInsteadOfRetreat)
        {
            _isDone = true;
            _controller?.OnMutantIntruderComplete(this, brokeThrough: false);
            OnSequenceComplete?.Invoke(false);
            if (NetworkObject.IsSpawned) NetworkObject.Despawn();
            yield break;
        }

        yield return StartCoroutine(RetreatingSequence(notifyController: true));
    }

    /// <summary>
    /// Snaps to face the despawn point, sprints back to it, then either calls
    /// <see cref="SuspectController.OnMutantIntruderComplete"/> (climbing mutants that gave up)
    /// or despawns directly (non-climbing mutants whose lineup slot was already released).
    /// A hard <see cref="MutantIntruderData.retreatDespawnTimeout"/> deadline ensures the
    /// mutant is force-despawned within that window even if it never reaches the despawn point.
    /// </summary>
    private IEnumerator RetreatingSequence(bool notifyController)
    {
        if (_isDone) yield break;

        float retreatDeadline = Time.time + _data.retreatDespawnTimeout;

        // Quick snap-turn toward the despawn direction.
        Vector3 toSpawn = _despawnPos.position - transform.position;
        toSpawn.y = 0f;
        if (toSpawn.sqrMagnitude > 0.001f)
        {
            bool rotDone = false;
            _activeTween = transform
                .DORotateQuaternion(Quaternion.LookRotation(toSpawn.normalized), 0.2f)
                .OnComplete(() => rotDone = true);

            // Honour the hard deadline even during the turn.
            while (!rotDone && Time.time < retreatDeadline)
                yield return null;
        }

        if (_isDone) yield break;

        SetWalkingClientRpc(true);

        // Re-enable the NavMeshAgent for retreat pathfinding (was disabled during walk-in).
        _agent.enabled = true;
        yield return null; // One frame for the agent to link onto the NavMesh.

        if (_agent.isOnNavMesh)
        {
            _agent.speed = _data.retreatSpeed;
            _agent.SetDestination(_despawnPos.position);

            // Sprint until arrival OR the hard deadline fires — whichever comes first.
            while (!_isDone && Time.time < retreatDeadline)
            {
                if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + ArrivalTolerance)
                    break;
                yield return new WaitForSeconds(ArrivalPollInterval);
            }

            _agent.ResetPath();
        }

        SetWalkingClientRpc(false);
        _isDone = true;

        if (notifyController)
        {
            // Climbing mutant gave up — let the controller clean up and queue the next suspect.
            _controller?.OnMutantIntruderComplete(this, brokeThrough: false);
            OnSequenceComplete?.Invoke(false);
        }
        else
        {
            // Non-climbing mutant — controller was already notified; just despawn.
            if (NetworkObject.IsSpawned)
                NetworkObject.Despawn();
            OnSequenceComplete?.Invoke(false);
        }
    }

    // ── Scripted Entrance ──────────────────────────────────────────────────────

    /// <summary>
    /// Begins the window interaction sequence from the stand position, skipping the walk-in phase.
    /// Use this when the mutant has already been placed at the booth window via a scripted entrance
    /// (e.g. falling from above). Must be called on the server only, after the NetworkObject is spawned.
    /// </summary>
    public void BeginAtStandPos(
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

        _mutantEnemy?.SuspendForLineup();

        StartCoroutine(AtStandPosSequence());
    }

    private IEnumerator AtStandPosSequence()
    {
        if (!IsServer || _isDone) yield break;

        _agent.enabled = false;

        // Rotate to face the booth window.
        bool rotationDone = false;
        _activeTween = transform
            .DORotateQuaternion(_standPos.rotation, 0.5f)
            .OnComplete(() => rotationDone = true);

        yield return new WaitUntil(() => rotationDone);

        yield return new WaitForSeconds(_data.preAttackPauseSeconds);

        if (_isDone) yield break;

        if (canClimb && _shutterController != null && _shutterController.IsOpen)
            yield return StartCoroutine(ClimbThroughSequence());
        else
            yield return StartCoroutine(ShutterBangSequence());
    }

    /// <summary>
    /// Assigns the Animator reference used for all animation RPCs.
    /// Call this after swapping the mesh on a full-mutant SuspectCharacter prefab so that
    /// walking, climbing, and attack bools are driven on the correct skeleton.
    /// </summary>
    public void SetAnimator(Animator a) => _animator = a;

    /// <summary>Server-side: sets an Animator bool on all clients.</summary>
    public void SetAnimBool(string paramName, bool value) => SetAnimBoolClientRpc(paramName, value);

    /// <summary>Server-side: fires an Animator trigger on all clients.</summary>
    public void TriggerAnim(string paramName) => TriggerAnimClientRpc(paramName);

    // ── ClientRpcs ─────────────────────────────────────────────────────────────

    /// <summary>Sets the Walking animator bool on all clients.</summary>
    [ClientRpc]
    private void SetWalkingClientRpc(bool walking)
    {
        if (_animator != null)
            _animator.SetBool("Walking", walking);
    }

    /// <summary>Sets the climbing animator bool on all clients for the breakthrough sequence.</summary>
    [ClientRpc]
    private void SetClimbingClientRpc(bool climbing)
    {
        if (_animator != null)
            _animator.SetBool(ClimbingAnimBool, climbing);
    }

    /// <summary>Sets the BangOnShutters animator bool on all clients. Used to drive the shutter-attack animation.</summary>
    [ClientRpc]
    private void SetAttackClientRpc(bool attacking)
    {
        if (_animator != null)
            _animator.SetBool(BangOnShuttersAnimBool, attacking);
    }

    /// <summary>Fires an animator trigger on all clients.</summary>
    [ClientRpc]
    private void TriggerAnimClientRpc(string trigger)
    {
        if (_animator != null && !string.IsNullOrEmpty(trigger))
            _animator.SetTrigger(trigger);
    }

    /// <summary>Sets a named Animator bool on all clients. Used for scripted-entrance animations (e.g. falling, landing).</summary>
    [ClientRpc]
    private void SetAnimBoolClientRpc(string paramName, bool value)
    {
        if (_animator != null && !string.IsNullOrEmpty(paramName))
            _animator.SetBool(paramName, value);
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
