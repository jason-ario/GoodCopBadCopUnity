using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative spawner that periodically creates MutantEnemy instances
/// at random positions within a configurable box volume in the woods.
/// The box is centred on this GameObject's position.
/// </summary>
public class MutantSpawner : NetworkBehaviour
{
    // ── Configuration ──────────────────────────────────────────────────────────

    [Header("Enemy Setup")]
    [Tooltip("Networked prefab containing a MutantEnemy component. Must be registered in NetworkManager's prefab list.")]
    [SerializeField] private GameObject mutantPrefab;

    [Header("Spawn Area")]
    [Tooltip("Half-extents of the axis-aligned box (in local space) within which enemies can spawn. The box is centred on this GameObject's position.")]
    [SerializeField] private Vector3 spawnBoxHalfExtents = new Vector3(20f, 0f, 20f);

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
        Vector3 localOffset = new Vector3(
            Random.Range(-spawnBoxHalfExtents.x, spawnBoxHalfExtents.x),
            Random.Range(-spawnBoxHalfExtents.y, spawnBoxHalfExtents.y),
            Random.Range(-spawnBoxHalfExtents.z, spawnBoxHalfExtents.z)
        );

        Vector3 spawnPosition = transform.TransformPoint(localOffset);
        Quaternion spawnRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        GameObject instance = Instantiate(mutantPrefab, spawnPosition, spawnRotation);
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

    // ── Gizmos ─────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, spawnBoxHalfExtents * 2f);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
        Gizmos.DrawWireCube(Vector3.zero, spawnBoxHalfExtents * 2f);
    }
}
