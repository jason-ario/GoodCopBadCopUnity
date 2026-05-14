using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative spawner that periodically creates MutantEnemy instances
/// at designated spawn points in the woods.
/// Place in the scene alongside a set of child Transform spawn points.
/// </summary>
public class MutantSpawner : NetworkBehaviour
{
    // ── Configuration ──────────────────────────────────────────────────────────

    [Header("Enemy Setup")]
    [Tooltip("Networked prefab containing a MutantEnemy component. Must be registered in NetworkManager's prefab list.")]
    [SerializeField] private GameObject mutantPrefab;

    [Header("Spawn Points")]
    [Tooltip("World positions used as spawn locations. Enemies are placed at a random point from this list each wave.")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Timing")]
    [Tooltip("Seconds before the first spawn wave after the game starts.")]
    [SerializeField] private float initialDelay = 10f;

    [Tooltip("Seconds between consecutive spawn waves.")]
    [SerializeField] private float spawnInterval = 30f;

    [Header("Count")]
    [Tooltip("How many enemies to spawn per wave.")]
    [SerializeField] private int enemiesPerWave = 3;

    [Tooltip("Maximum number of active enemies this spawner will maintain. No new wave starts while at or above this cap.")]
    [SerializeField] private int maxActiveEnemies = 10;

    // ── State ──────────────────────────────────────────────────────────────────

    private readonly List<NetworkObject> _activeEnemies = new List<NetworkObject>();
    private bool _isRunning;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer)
            return;

        if (mutantPrefab == null)
        {
            Debug.LogError("[MutantSpawner] mutantPrefab is not assigned. Spawner will not run.", this);
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("[MutantSpawner] No spawn points assigned. Add child Transforms to the spawnPoints array.", this);
            return;
        }

        _isRunning = true;
        StartCoroutine(SpawnLoop());
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isRunning = false;
    }

    // ── Spawn Loop ─────────────────────────────────────────────────────────────

    private IEnumerator SpawnLoop()
    {
        yield return new WaitForSeconds(initialDelay);

        while (_isRunning)
        {
            PruneDeadEnemies();

            if (_activeEnemies.Count < maxActiveEnemies)
                SpawnWave();

            yield return new WaitForSeconds(spawnInterval);
        }
    }

    // ── Spawning ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a single wave of enemies, up to the per-wave count and the global cap.
    /// </summary>
    private void SpawnWave()
    {
        int toSpawn = Mathf.Min(enemiesPerWave, maxActiveEnemies - _activeEnemies.Count);

        for (int i = 0; i < toSpawn; i++)
            SpawnSingleEnemy();
    }

    private void SpawnSingleEnemy()
    {
        Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)];

        GameObject instance = Instantiate(mutantPrefab, point.position, point.rotation);
        NetworkObject netObj = instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[MutantSpawner] mutantPrefab is missing a NetworkObject component.", this);
            Destroy(instance);
            return;
        }

        netObj.Spawn(true);
        _activeEnemies.Add(netObj);
    }

    /// <summary>
    /// Removes entries from the active list that have already been despawned.
    /// </summary>
    private void PruneDeadEnemies()
    {
        _activeEnemies.RemoveAll(netObj => netObj == null || !netObj.IsSpawned);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Manually triggers an immediate spawn wave. SERVER ONLY.
    /// </summary>
    public void ForceSpawnWave()
    {
        if (!IsServer)
            return;

        PruneDeadEnemies();
        SpawnWave();
    }

    /// <summary>
    /// Stops the spawner loop. Existing enemies remain active. SERVER ONLY.
    /// </summary>
    public void StopSpawning()
    {
        _isRunning = false;
    }

    /// <summary>
    /// Restarts the spawner loop after it has been stopped. SERVER ONLY.
    /// </summary>
    public void ResumeSpawning()
    {
        if (!IsServer || _isRunning)
            return;

        _isRunning = true;
        StartCoroutine(SpawnLoop());
    }

    /// <summary>
    /// Despawns all currently tracked active enemies. SERVER ONLY.
    /// </summary>
    public void DespawnAllEnemies()
    {
        if (!IsServer)
            return;

        foreach (NetworkObject netObj in _activeEnemies)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn();
        }

        _activeEnemies.Clear();
    }
}
