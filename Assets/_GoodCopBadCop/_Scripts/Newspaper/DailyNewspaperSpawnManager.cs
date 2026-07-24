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

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer)
            return;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDayStart;
        else
            Debug.LogError("[DailyNewspaperSpawnManager] ShiftManager.Instance is null on spawn.", this);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (IsServer && ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;
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
