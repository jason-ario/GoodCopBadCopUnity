using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative spawner that periodically creates bursts of MutantEnemy instances
/// at random positions within a configurable box volume in the woods.
/// The box is centred on this GameObject's position.
/// Spawn intervals are randomised between <see cref="spawnIntervalMin"/> and <see cref="spawnIntervalMax"/>.
/// Each interval triggers a burst: a rapid sequence of spawns with a short delay between each one.
/// </summary>
public class MutantSpawner : NetworkBehaviour
{
    // ── Configuration ──────────────────────────────────────────────────────────

    [Header("Enemy Setup")]
    [Tooltip("Networked prefabs to choose from at random. Each must contain a MutantEnemy component and be registered in NetworkManager's prefab list.")]
    [SerializeField] private GameObject[] mutantPrefabs;

    [Header("Spawn Area")]
    [Tooltip("Half-extents of the axis-aligned box (in local space) within which enemies can spawn. The box is centred on this GameObject's position.")]
    [SerializeField] private Vector3 spawnBoxHalfExtents = new Vector3(20f, 0f, 20f);

    [Header("Timing")]
    [Tooltip("Seconds before the first burst after the game starts.")]
    [SerializeField] private float initialDelay = 10f;

    [Tooltip("Minimum seconds between consecutive bursts.")]
    [SerializeField] private float spawnIntervalMin = 30f;

    [Tooltip("Maximum seconds between consecutive bursts.")]
    [SerializeField] private float spawnIntervalMax = 60f;

    [Header("Burst")]
    [Tooltip("Minimum number of enemies spawned per burst.")]
    [SerializeField] private int burstCountMin = 2;

    [Tooltip("Maximum number of enemies spawned per burst.")]
    [SerializeField] private int burstCountMax = 5;

    [Tooltip("Seconds between each individual spawn within a burst.")]
    [SerializeField] private float burstSpawnDelay = 0.5f;

    [Header("Cap")]
    [Tooltip("Maximum number of active enemies this spawner will maintain. Individual burst spawns are skipped once at or above this cap.")]
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

        if (mutantPrefabs == null || mutantPrefabs.Length == 0)
        {
            Debug.LogError("[MutantSpawner] mutantPrefabs is empty. Spawner will not run.", this);
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
            yield return StartCoroutine(SpawnBurst());

            float interval = Random.Range(spawnIntervalMin, spawnIntervalMax);
            yield return new WaitForSeconds(interval);
        }
    }

    // ── Spawning ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a rapid sequence of enemies, one at a time, with <see cref="burstSpawnDelay"/> between each.
    /// The burst count is randomised between <see cref="burstCountMin"/> and <see cref="burstCountMax"/>.
    /// Individual spawns are skipped (but the delay still elapses) when the active cap is reached.
    /// </summary>
    private IEnumerator SpawnBurst()
    {
        int count = Random.Range(burstCountMin, burstCountMax + 1);

        for (int i = 0; i < count; i++)
        {
            if (!_isRunning)
                yield break;

            PruneDeadEnemies();

            if (_activeEnemies.Count < maxActiveEnemies)
                SpawnSingleEnemy();

            if (i < count - 1)
                yield return new WaitForSeconds(burstSpawnDelay);
        }
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

        GameObject prefab = mutantPrefabs[Random.Range(0, mutantPrefabs.Length)];
        GameObject instance = Instantiate(prefab, spawnPosition, spawnRotation);
        NetworkObject netObj = instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[MutantSpawner] A prefab in mutantPrefabs is missing a NetworkObject component.", this);
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
    /// Manually triggers an immediate burst. SERVER ONLY.
    /// </summary>
    public void ForceSpawn()
    {
        if (!IsServer)
            return;

        StartCoroutine(SpawnBurst());
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
