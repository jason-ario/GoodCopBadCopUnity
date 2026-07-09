using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Server-authoritative controller for the Ocho NPC.
///
/// Ocho spawns at a fixed distant position and watches the player by rotating slowly
/// to face them. If any player enters <see cref="_fleeRadius"/> he transitions to a
/// flee state: the NavMeshAgent runs at <see cref="_fleeSpeed"/> toward
/// <see cref="_fleeDestination"/>, then the NetworkObject is despawned on arrival.
///
/// Prefab requirements:
///   - NetworkObject
///   - NavMeshAgent  (speed managed by this script)
///   - NetworkTransform (syncs rotation and flee position to all clients)
///   - Animator somewhere in the hierarchy (found automatically via GetComponentInChildren)
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(NetworkObject))]
public class OchoWatcherBehaviour : NetworkBehaviour
{
    // ── States ─────────────────────────────────────────────────────────────────

    private enum OchoState { Watching, Fleeing }

    // ── Inspector — Proximity ──────────────────────────────────────────────────

    [Header("Proximity")]
    [Tooltip("Distance at which any player triggers Ocho to flee.")]
    [SerializeField] private float _fleeRadius = 40f;

    [Tooltip("How frequently (seconds) the server checks player distances and updates watch rotation.")]
    [SerializeField] private float _tickInterval = 0.25f;

    // ── Inspector — Watching ───────────────────────────────────────────────────

    [Header("Watching")]
    [Tooltip("Degrees-per-second at which Ocho rotates to face the nearest player while idle.")]
    [SerializeField] private float _watchRotateSpeed = 60f;

    // ── Inspector — Flee ───────────────────────────────────────────────────────

    [Header("Flee")]
    [Tooltip("Transform Ocho runs toward when fleeing. Place this off-screen behind the tree line " +
             "at a point that sits on the NavMesh.")]
    [SerializeField] private Transform _fleeDestination;

    [Tooltip("NavMeshAgent speed while fleeing.")]
    [SerializeField] private float _fleeSpeed = 20f;

    [Tooltip("NavMeshAgent angular speed while fleeing (high so he turns instantly).")]
    [SerializeField] private float _fleeAngularSpeed = 720f;

    [Tooltip("NavMeshAgent acceleration while fleeing.")]
    [SerializeField] private float _fleeAcceleration = 60f;

    [Tooltip("Remaining-distance threshold at which Ocho is considered arrived and despawns.")]
    [SerializeField] private float _arrivalThreshold = 2f;

    // ── Inspector — Animation ──────────────────────────────────────────────────

    [Header("Animation (optional)")]
    [Tooltip("Trigger parameter fired on all clients when Ocho begins fleeing.")]
    [SerializeField] private string _fleeTriggerName = "Flee";
    [Tooltip("Bool parameter set to true while Ocho is fleeing.")]
    [SerializeField] private string _walkingBoolName = "Walking";
    [Tooltip("Float parameter driven by movement speed (blend tree).")]
    [SerializeField] private string _speedParamName  = "Speed";
    [Tooltip("Idle animation state name played on spawn.")]
    [SerializeField] private string _idleStateName   = "Idle";

    // ── Inspector — Audio ──────────────────────────────────────────────────────

    [Header("Audio (optional)")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip   _fleeSound;

    // ── Network variables ──────────────────────────────────────────────────────

    private readonly NetworkVariable<bool> _networkFleeing = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ── Runtime state ──────────────────────────────────────────────────────────

    private NavMeshAgent     _agent;
    private Animator         _animator;
    private OchoState        _state = OchoState.Watching;
    private SuspectCharacter _suspectCharacter;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _agent            = GetComponent<NavMeshAgent>();
        _animator         = GetComponentInChildren<Animator>();
        _suspectCharacter = GetComponent<SuspectCharacter>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _networkFleeing.OnValueChanged += OnFleeingChanged;

        // Play idle on spawn for all clients.
        if (_animator != null && !string.IsNullOrEmpty(_idleStateName))
            _animator.Play(_idleStateName);

        if (IsServer)
        {
            // SuspectCharacter.Awake() disables the NavMeshAgent by default.
            // Re-enable it here now that the network object is live on the server.
            _agent.enabled   = true;
            _agent.speed     = 0f;
            _agent.isStopped = true;

            // Flee immediately if Ocho takes any damage.
            if (_suspectCharacter != null)
                _suspectCharacter.OnHit += BeginFlee;

            StartCoroutine(WatchLoop());
        }
        else
        {
            // Non-server clients do not drive the NavMesh simulation.
            _agent.enabled = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _networkFleeing.OnValueChanged -= OnFleeingChanged;

        if (_suspectCharacter != null)
            _suspectCharacter.OnHit -= BeginFlee;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Configures runtime values before <see cref="OnNetworkSpawn"/> fires.
    /// Call this on the server immediately after Instantiate and before netObj.Spawn().
    /// Mirrors the SetAggroTarget / SetForceAggro pattern used in MutantSpawner.
    /// </summary>
    public void Initialise(
        Transform   fleeDestination,
        float       fleeRadius       = 8f,
        float       fleeSpeed        = 20f,
        float       watchRotateSpeed = 60f,
        AudioSource audioSource      = null,
        AudioClip   fleeSound        = null)
    {
        _fleeDestination  = fleeDestination;
        _fleeRadius       = fleeRadius;
        _fleeSpeed        = fleeSpeed;
        _watchRotateSpeed = watchRotateSpeed;

        if (audioSource != null) _audioSource = audioSource;
        if (fleeSound   != null) _fleeSound   = fleeSound;
    }

    // ── Server loop ────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs on the server every <see cref="_tickInterval"/> seconds.
    /// While watching, rotates toward the nearest player and checks proximity.
    /// While fleeing, polls for arrival then despawns.
    /// </summary>
    private IEnumerator WatchLoop()
    {
        // One-frame delay so NavMeshAgent has time to place itself on the mesh
        // after being spawned — matches the pattern in MutantEnemy and CollectorNPC.
        yield return null;

        while (true)
        {
            switch (_state)
            {
                case OchoState.Watching:
                    TickWatching();
                    break;

                case OchoState.Fleeing:
                    if (HasArrivedAtFleeDest())
                    {
                        NetworkObject.Despawn(destroy: true);
                        yield break;
                    }
                    break;
            }

            yield return new WaitForSeconds(_tickInterval);
        }
    }

    private void TickWatching()
    {
        Transform nearest = FindNearestPlayer();
        if (nearest == null) return;

        // Rotate to face the player on the Y axis only.
        Vector3 dir = nearest.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
        {
            Quaternion target = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                target,
                _watchRotateSpeed * _tickInterval
            );
        }

        // Trigger flee when a player is within range.
        if (dir.sqrMagnitude <= _fleeRadius * _fleeRadius)
            BeginFlee();
    }

    private void BeginFlee()
    {
        _state                = OchoState.Fleeing;
        _networkFleeing.Value = true;

        _agent.isStopped    = false;
        _agent.speed        = _fleeSpeed;
        _agent.angularSpeed = _fleeAngularSpeed;
        _agent.acceleration = _fleeAcceleration;

        if (_fleeDestination != null)
        {
            _agent.SetDestination(_fleeDestination.position);
        }
        else
        {
            // Fallback: run directly away from the nearest player.
            Transform nearest = FindNearestPlayer();
            if (nearest != null)
            {
                Vector3 awayDir = (transform.position - nearest.position).normalized;
                _agent.SetDestination(transform.position + awayDir * 50f);
            }
        }

        PlayFleeAudioClientRpc();

        Debug.Log("[OchoWatcher] Flee triggered.", this);
    }

    private bool HasArrivedAtFleeDest()
    {
        if (!_agent.isOnNavMesh) return false;
        if (_agent.pathPending)  return false;
        return _agent.remainingDistance <= _arrivalThreshold;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Transform FindNearestPlayer()
    {
        Transform nearest        = null;
        float     nearestSqrDist = float.MaxValue;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            float sqrDist = (client.PlayerObject.transform.position - transform.position).sqrMagnitude;
            if (sqrDist < nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                nearest        = client.PlayerObject.transform;
            }
        }

        return nearest;
    }

    // ── NetworkVariable callbacks ──────────────────────────────────────────────

    private void OnFleeingChanged(bool previous, bool current)
    {
        if (_animator == null) return;

        if (current)
        {
            if (!string.IsNullOrEmpty(_fleeTriggerName))
                _animator.SetTrigger(_fleeTriggerName);
            if (!string.IsNullOrEmpty(_walkingBoolName))
                _animator.SetBool(_walkingBoolName, true);
        }
        else
        {
            if (!string.IsNullOrEmpty(_walkingBoolName))
                _animator.SetBool(_walkingBoolName, false);
            if (!string.IsNullOrEmpty(_idleStateName))
                _animator.Play(_idleStateName);
        }
    }

    // Drives the Speed blend-tree parameter on all clients.
    private void Update()
    {
        if (_animator == null || string.IsNullOrEmpty(_speedParamName)) return;

        float speed = (IsServer && _agent != null && _agent.enabled)
            ? _agent.velocity.magnitude
            : (_networkFleeing.Value ? _fleeSpeed : 0f);

        _animator.SetFloat(_speedParamName, speed);
    }

    // ── Client RPCs ────────────────────────────────────────────────────────────

    [ClientRpc]
    private void PlayFleeAudioClientRpc()
    {
        if (_audioSource != null && _fleeSound != null)
            _audioSource.PlayOneShot(_fleeSound);
    }
}
