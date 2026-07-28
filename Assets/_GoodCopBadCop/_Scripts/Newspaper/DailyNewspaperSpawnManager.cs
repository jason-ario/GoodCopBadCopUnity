using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative manager that spawns a fresh networked <see cref="Newspaper"/> at
/// <see cref="_spawnPoint"/> every time <see cref="ShiftManager.OnDayStart"/> fires.
/// Any newspaper instance spawned on a previous day is despawned first, so at most one
/// daily newspaper exists in the scene at a time.
///
/// Prefab setup:
///   - This GameObject requires a NetworkObject component.
///   - <see cref="_newspaperPrefab"/> must have a NetworkObject and be registered in the
///     NetworkManager prefab list.
///   - Assign the world spawn location to <see cref="_spawnPoint"/> (e.g. "Newspaper Pickup Pos").
/// </summary>
public class DailyNewspaperSpawnManager : NetworkBehaviour
{
    [Header("Newspaper")]
    [Tooltip("Networked newspaper prefab to spawn at the start of each day.")]
    [SerializeField] private Newspaper _newspaperPrefab;

    [Tooltip("World location where the daily newspaper is spawned.")]
    [SerializeField] private Transform _spawnPoint;

    // The newspaper spawned for the current day, if any, so it can be despawned next day.
    private NetworkObject _activeNewspaper;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    // Tracks whether OnDayStart has been subscribed, so OnNetworkDespawn only unsubscribes
    // once the retry coroutine has actually attached the handler.
    private bool _subscribedToDayStart;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        Debug.Log($"[DailyNewspaperSpawnManager] OnNetworkSpawn. IsServer={IsServer}, IsHost={IsHost}, IsClient={IsClient}.", this);

        if (!IsServer)
            return;

        StartCoroutine(SubscribeToDayStartWhenReady());
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (IsServer && _subscribedToDayStart && ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;

        _subscribedToDayStart = false;
    }

    /// <summary>
    /// Waits for <see cref="ShiftManager.Instance"/> to become available before subscribing.
    /// Scene-placed NetworkObjects spawn in a non-deterministic order, so ShiftManager's
    /// singleton may not be assigned yet at the exact moment this object's OnNetworkSpawn runs.
    /// A one-shot null check here would silently and permanently miss the subscription —
    /// this retries every frame until the instance exists.
    /// </summary>
    private IEnumerator SubscribeToDayStartWhenReady()
    {
        yield return new WaitUntil(() => ShiftManager.Instance != null);

        ShiftManager.Instance.OnDayStart += OnDayStart;
        _subscribedToDayStart = true;
    }

    // ── Day Start ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="ShiftManager.OnDayStart"/> on the server.
    /// Despawns the previous day's newspaper (if still present), then spawns a fresh one.
    /// </summary>
    private void OnDayStart()
    {
        DespawnPreviousNewspaper();
        SpawnDailyNewspaper();
    }

    // ── Despawn ──────────────────────────────────────────────────────────────

    private void DespawnPreviousNewspaper()
    {
        if (_activeNewspaper != null && _activeNewspaper.IsSpawned)
            _activeNewspaper.Despawn();

        _activeNewspaper = null;
    }

    // ── Spawn ────────────────────────────────────────────────────────────────

    private void SpawnDailyNewspaper()
    {
        if (_newspaperPrefab == null)
        {
            Debug.LogWarning("[DailyNewspaperSpawnManager] No newspaper prefab assigned.", this);
            return;
        }

        if (_spawnPoint == null)
        {
            Debug.LogWarning("[DailyNewspaperSpawnManager] No spawn point assigned.", this);
            return;
        }

        GameObject instance = Instantiate(_newspaperPrefab.gameObject, _spawnPoint.position, _spawnPoint.rotation);
        NetworkObject netObj = instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError($"[DailyNewspaperSpawnManager] Prefab '{_newspaperPrefab.name}' is missing a NetworkObject component.", this);
            Destroy(instance);
            return;
        }

        netObj.Spawn(true);
        _activeNewspaper = netObj;

        Debug.Log("[DailyNewspaperSpawnManager] Spawned today's newspaper.");
    }
}
