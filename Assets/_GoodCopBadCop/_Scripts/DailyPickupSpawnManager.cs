using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative manager that spawns networked prefabs at the start of each day.
/// A random subset of the registered <see cref="PickupSpawnPoint"/> transforms is chosen
/// each day. Each point defines its own prefab pool and provides the prefab to spawn via
/// <see cref="PickupSpawnPoint.GetRandomPrefab"/>.
///
/// Prefab setup:
///   - This GameObject requires a NetworkObject component.
///   - All prefabs referenced by the spawn points must have a NetworkObject and be registered
///     in the NetworkManager prefab list.
///   - Assign all candidate <see cref="PickupSpawnPoint"/> scene objects to <see cref="_spawnPoints"/>.
/// </summary>
public class DailyPickupSpawnManager : NetworkBehaviour
{
    [Header("Spawn Points")]
    [Tooltip("All candidate spawn point transforms in the scene. A random subset is chosen each day.")]
    [SerializeField] private PickupSpawnPoint[] _spawnPoints;

    [Header("Spawn Count")]
    [Tooltip("Minimum number of items spawned at day start.")]
    [SerializeField] private int _minSpawnsPerDay = 1;
    [Tooltip("Maximum number of items spawned at day start. Clamped to the number of available spawn points.")]
    [SerializeField] private int _maxSpawnsPerDay = 3;

    // Tracks all currently active spawned NetworkObjects so they can be cleaned up the next day.
    private readonly List<NetworkObject> _activeSpawns = new List<NetworkObject>();

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer)
            return;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDayStart;
        else
            Debug.LogError("[DailyPickupSpawnManager] ShiftManager.Instance is null on spawn.", this);
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
    /// Despawns any leftover items from the previous day, then spawns a fresh random selection.
    /// </summary>
    private void OnDayStart()
    {
        DespawnLeftovers();
        SpawnDailyPickups();
    }

    // ── Despawn ──────────────────────────────────────────────────────────────

    private void DespawnLeftovers()
    {
        foreach (NetworkObject netObj in _activeSpawns)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn();
        }

        _activeSpawns.Clear();
    }

    // ── Spawn ────────────────────────────────────────────────────────────────

    private void SpawnDailyPickups()
    {
        if (_spawnPoints == null || _spawnPoints.Length == 0)
        {
            Debug.LogWarning("[DailyPickupSpawnManager] No spawn points assigned.", this);
            return;
        }

        int available  = _spawnPoints.Length;
        int spawnCount = Mathf.Clamp(Random.Range(_minSpawnsPerDay, _maxSpawnsPerDay + 1), 0, available);

        List<PickupSpawnPoint> shuffledPoints = ShuffledCopy(_spawnPoints);

        for (int i = 0; i < spawnCount; i++)
        {
            PickupSpawnPoint point = shuffledPoints[i];
            GameObject prefab = point.GetRandomPrefab();

            if (prefab == null)
            {
                Debug.LogWarning($"[DailyPickupSpawnManager] Spawn point '{point.name}' has no prefabs assigned — skipping.", this);
                continue;
            }

            SpawnAt(point, prefab);
        }

        Debug.Log($"[DailyPickupSpawnManager] Spawned {spawnCount} item(s) for today.");
    }

    private void SpawnAt(PickupSpawnPoint spawnPoint, GameObject prefab)
    {
        if (spawnPoint == null || prefab == null)
            return;

        GameObject instance = Instantiate(prefab, spawnPoint.transform.position, spawnPoint.transform.rotation);
        NetworkObject netObj = instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError($"[DailyPickupSpawnManager] Prefab '{prefab.name}' is missing a NetworkObject component.", this);
            Destroy(instance);
            return;
        }

        netObj.Spawn(true);
        _activeSpawns.Add(netObj);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Returns a new list containing all elements of <paramref name="source"/> in a random order (Fisher-Yates).</summary>
    private static List<T> ShuffledCopy<T>(T[] source)
    {
        List<T> list = new List<T>(source);

        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }
}
