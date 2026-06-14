using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative singleton that manages dispatch of CollectorNPC instances
/// to filled CollectableContainers.
///
/// When a container calls HQ, it enqueues a pickup request here. The dispatcher
/// maintains one active collector at a time and processes the queue as collectors finish.
///
/// Scene setup:
///   - Place this on its own GameObject with a NetworkObject component.
///   - Assign _collectorNpcPrefab (must be registered in NetworkManager's prefab list).
///   - Assign _collectorSpawnPoint to a Transform off-screen or at the map edge.
///   - Optionally assign _dispatchAudioClip for on-dispatch feedback.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class HQPickupDispatcher : NetworkBehaviour
{
    public static HQPickupDispatcher Instance { get; private set; }

    [Header("Collector NPC")]
    [Tooltip("NetworkObject prefab containing a CollectorNPC component.")]
    [SerializeField] private GameObject _collectorNpcPrefab;

    [Tooltip("World-space spawn/return point for the collector NPC (typically off-screen).")]
    [SerializeField] private Transform _collectorSpawnPoint;

    [Header("Audio")]
    [Tooltip("Played on all clients when a collector is dispatched.")]
    [SerializeField] private AudioClip _dispatchAudioClip;

    private readonly Queue<CollectableContainer> _pendingRequests = new();
    private bool _collectorActive;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[HQPickupDispatcher] Duplicate instance detected — destroying self.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues a pickup request for the given container and starts the dispatch process
    /// if no collector is currently active. SERVER ONLY.
    /// </summary>
    public void DispatchCollector(CollectableContainer target)
    {
        if (!IsServer) return;

        if (target == null)
        {
            Debug.LogWarning("[HQPickupDispatcher] DispatchCollector called with null target.");
            return;
        }

        _pendingRequests.Enqueue(target);
        TryDispatchNext();
    }

    /// <summary>
    /// Called by a CollectorNPC when it has finished its job and returned to the spawn point.
    /// Clears the active flag and processes the next queued request. SERVER ONLY.
    /// </summary>
    public void OnCollectorFinished()
    {
        if (!IsServer) return;

        _collectorActive = false;
        TryDispatchNext();
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private void TryDispatchNext()
    {
        if (_collectorActive || _pendingRequests.Count == 0) return;

        CollectableContainer target = _pendingRequests.Dequeue();

        // Target may have been destroyed while waiting.
        if (target == null || !target.IsSpawned)
        {
            TryDispatchNext();
            return;
        }

        if (_collectorNpcPrefab == null)
        {
            Debug.LogError("[HQPickupDispatcher] _collectorNpcPrefab is not assigned.");
            return;
        }

        if (_collectorSpawnPoint == null)
        {
            Debug.LogError("[HQPickupDispatcher] _collectorSpawnPoint is not assigned.");
            return;
        }

        GameObject npcGo = Instantiate(
            _collectorNpcPrefab,
            _collectorSpawnPoint.position,
            _collectorSpawnPoint.rotation);

        NetworkObject netObj = npcGo.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[HQPickupDispatcher] Collector NPC prefab has no NetworkObject component.");
            Destroy(npcGo);
            return;
        }

        netObj.Spawn(destroyWithScene: true);

        CollectorNPC collector = npcGo.GetComponent<CollectorNPC>();

        if (collector == null)
        {
            Debug.LogError("[HQPickupDispatcher] Collector NPC prefab has no CollectorNPC component.");
            netObj.Despawn(destroy: true);
            return;
        }

        collector.SetTarget(target, _collectorSpawnPoint.position);
        _collectorActive = true;

        PlayDispatchSoundClientRpc();
    }

    [ClientRpc]
    private void PlayDispatchSoundClientRpc()
    {
        if (_dispatchAudioClip != null && SFXController.Instance != null)
            SFXController.Instance.Play(_dispatchAudioClip);
    }
}
