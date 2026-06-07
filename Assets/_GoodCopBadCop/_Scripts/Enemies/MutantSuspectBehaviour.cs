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
    private const float RetreatDuration = 6f;
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

        // Wait one frame for the NavMeshAgent to link onto the baked mesh surface.
        yield return null;

        if (!_agent.isOnNavMesh)
        {
            Debug.LogWarning("[MutantSuspectBehaviour] Not on NavMesh at lineup start — aborting.", this);
            yield break;
        }

        // Walk to the booth stand position.
        _agent.SetDestination(_standPos.position);
        SetWalkingClientRpc(true);

        while (_agent.pathPending || _agent.remainingDistance > _agent.stoppingDistance + ArrivalTolerance)
            yield return new WaitForSeconds(ArrivalPollInterval);

        _agent.ResetPath();

        // Rotate to face the booth window.
        SetWalkingClientRpc(false);

        bool rotationDone = false;
        _activeTween = transform
            .DORotateQuaternion(_standPos.rotation, 0.5f)
            .OnComplete(() => rotationDone = true);

        yield return new WaitUntil(() => rotationDone);

        // Brief pause before acting.
        yield return new WaitForSeconds(_data.preAttackPauseSeconds);

        if (_isDone) yield break;

        if (_shutterController != null && _shutterController.IsOpen)
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

        SetWalkingClientRpc(true);
        EnableMutantEnemyClientRpc();

        // Initialise chase loop on the server manually, since OnNetworkSpawn skipped it.
        if (_mutantEnemy != null)
        {
            _mutantEnemy.enabled = true;
            _mutantEnemy.InitialiseServer();
        }

        _isDone = true;
        _controller?.OnMutantIntruderComplete(this, brokeThrough: true);
    }

    /// <summary>Bangs on the closed shutter, then retreats if the shutter stays closed.</summary>
    private IEnumerator ShutterBangSequence()
    {
        if (!IsServer || _isDone) yield break;

        for (int i = 0; i < _data.shutterBangCount; i++)
        {
            if (_isDone) yield break;

            // Shutter opened mid-bang — break through immediately.
            if (_shutterController != null && _shutterController.IsOpen)
            {
                yield return StartCoroutine(ClimbThroughSequence());
                yield break;
            }

            TriggerAnimClientRpc(_data.bangAnimationTrigger);
            yield return new WaitForSeconds(_data.bangIntervalSeconds);
        }

        if (_isDone) yield break;

        // Give up: pause, then walk back to the despawn point.
        yield return new WaitForSeconds(GiveUpPauseDuration);

        if (_isDone) yield break;

        SetWalkingClientRpc(true);

        if (_agent.isOnNavMesh)
        {
            _agent.SetDestination(_despawnPos.position);

            while (_agent.pathPending || _agent.remainingDistance > _agent.stoppingDistance + ArrivalTolerance)
                yield return new WaitForSeconds(ArrivalPollInterval);

            _agent.ResetPath();
        }

        SetWalkingClientRpc(false);

        _isDone = true;
        _controller?.OnMutantIntruderComplete(this, brokeThrough: false);
    }

    // ── ClientRpcs ─────────────────────────────────────────────────────────────

    /// <summary>Sets the Walking animator bool on all clients.</summary>
    [ClientRpc]
    private void SetWalkingClientRpc(bool walking)
    {
        if (_animator != null)
            _animator.SetBool("Walking", walking);
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
}
