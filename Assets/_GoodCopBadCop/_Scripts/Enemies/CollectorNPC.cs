using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A networked NPC that walks to a CollectableContainer, triggers the collection,
/// then walks back to the spawn point and despawns.
///
/// Movement and collection sequence run server-side only. The collect animation
/// is broadcast to all clients via a ClientRpc.
///
/// Prefab setup:
///   - NetworkObject
///   - NavMeshAgent
///   - Optional: Animator with a trigger named by <see cref="_collectAnimTrigger"/>
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(NetworkObject))]
public class CollectorNPC : NetworkBehaviour
{
    private const float DefaultArrivalThreshold = 1.5f;

    [Header("Collection Settings")]
    [Tooltip("Seconds the collector pauses at the container before signalling completion.")]
    [SerializeField] private float _collectDuration = 1.5f;

    [Tooltip("Distance threshold (metres) considered 'arrived' at the destination.")]
    [SerializeField] private float _arrivalThreshold = DefaultArrivalThreshold;

    [Header("Animation")]
    [SerializeField] private Animator _animator;
    [SerializeField] private string _collectAnimTrigger = "Collect";

    private NavMeshAgent _agent;
    private CollectableContainer _target;
    private Vector3 _returnPoint;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Non-server clients don't run the NavMesh simulation.
        if (!IsServer)
            _agent.enabled = false;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns a target container and return point, then starts the collection sequence.
    /// SERVER ONLY. Call this immediately after spawning.
    /// </summary>
    public void SetTarget(CollectableContainer container, Vector3 returnPoint)
    {
        if (!IsServer) return;

        _target      = container;
        _returnPoint = returnPoint;
        StartCoroutine(CollectSequence());
    }

    // ── Collection sequence ───────────────────────────────────────────────────

    private IEnumerator CollectSequence()
    {
        if (_target == null)
        {
            Debug.LogWarning("[CollectorNPC] CollectSequence started with null target — aborting.");
            DespawnSelf();
            yield break;
        }

        // ── Walk to container ──────────────────────────────────────────────
        _agent.SetDestination(_target.transform.position);
        yield return new WaitUntil(() => HasArrived(_target.transform.position));

        // ── Collect ────────────────────────────────────────────────────────
        TriggerCollectAnimClientRpc();
        yield return new WaitForSeconds(_collectDuration);

        _target.OnCollectorArrived();

        // ── Walk back ──────────────────────────────────────────────────────
        _agent.SetDestination(_returnPoint);
        yield return new WaitUntil(() => HasArrived(_returnPoint));

        // ── Finish ─────────────────────────────────────────────────────────
        HQPickupDispatcher.Instance?.OnCollectorFinished();
        DespawnSelf();
    }

    private bool HasArrived(Vector3 destination)
    {
        if (!_agent.isOnNavMesh) return false;
        if (_agent.pathPending)  return false;

        return _agent.remainingDistance <= _arrivalThreshold;
    }

    private void DespawnSelf()
    {
        if (IsSpawned)
            NetworkObject.Despawn(destroy: true);
    }

    // ── Client RPCs ───────────────────────────────────────────────────────────

    [ClientRpc]
    private void TriggerCollectAnimClientRpc()
    {
        if (_animator != null && !string.IsNullOrEmpty(_collectAnimTrigger))
            _animator.SetTrigger(_collectAnimTrigger);
    }
}
