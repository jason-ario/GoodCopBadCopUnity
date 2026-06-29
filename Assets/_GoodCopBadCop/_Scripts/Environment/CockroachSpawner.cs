using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-level controller that spawns a swarm of cockroaches inside the booth.
/// Referenced by <see cref="InsectSwarmAnomaly"/> to start and stop the infestation.
/// Cockroach behaviour will be implemented separately — this class only handles
/// instantiation and cleanup.
/// </summary>
public class CockroachSpawner : MonoBehaviour
{
    [Header("Cockroach Prefab")]
    [Tooltip("Cockroach prefab to instantiate. Behaviour will be implemented later.")]
    [SerializeField] private GameObject _cockroachPrefab;

    [Header("Spawn Area")]
    [Tooltip("Center point of the spawn area inside the booth. Use a child Transform to position it precisely.")]
    [SerializeField] private Transform _spawnAreaCenter;

    [Tooltip("Half-extents of the rectangular spawn area on the XZ plane (width / depth).")]
    [SerializeField] private Vector2 _spawnAreaHalfExtents = new Vector2(1.5f, 1f);

    [Header("Swarm Size")]
    [Tooltip("Minimum number of cockroaches spawned per swarm.")]
    [SerializeField] private int _minCount = 8;

    [Tooltip("Maximum number of cockroaches spawned per swarm.")]
    [SerializeField] private int _maxCount = 20;

    private readonly List<GameObject> _spawnedCockroaches = new List<GameObject>();

    /// <summary>
    /// Spawns a randomised swarm of cockroaches within the booth spawn area.
    /// Safe to call while a swarm is already active — the new batch is tracked alongside
    /// the existing one and cleaned up together by <see cref="StopSwarm"/>.
    /// </summary>
    public void StartSwarm()
    {
        if (_cockroachPrefab == null)
        {
            Debug.LogWarning("[CockroachSpawner] Cockroach prefab is not assigned.", this);
            return;
        }

        if (_spawnAreaCenter == null)
        {
            Debug.LogWarning("[CockroachSpawner] Spawn area center Transform is not assigned.", this);
            return;
        }

        int count = Random.Range(_minCount, _maxCount + 1);

        for (int i = 0; i < count; i++)
        {
            Vector3 spawnPos = SampleSpawnPosition();
            Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            GameObject cockroach = Instantiate(_cockroachPrefab, spawnPos, spawnRot);
            _spawnedCockroaches.Add(cockroach);
        }

        Debug.Log($"[CockroachSpawner] Spawned {count} cockroaches.");
    }

    /// <summary>
    /// Destroys all cockroaches currently tracked by this spawner and clears the list.
    /// </summary>
    public void StopSwarm()
    {
        foreach (GameObject cockroach in _spawnedCockroaches)
        {
            if (cockroach != null)
                Destroy(cockroach);
        }

        _spawnedCockroaches.Clear();
        Debug.Log("[CockroachSpawner] Swarm cleared.");
    }

    /// <summary>Returns a random world-space position within the flat XZ spawn rectangle.</summary>
    private Vector3 SampleSpawnPosition()
    {
        float x = Random.Range(-_spawnAreaHalfExtents.x, _spawnAreaHalfExtents.x);
        float z = Random.Range(-_spawnAreaHalfExtents.y, _spawnAreaHalfExtents.y);
        return _spawnAreaCenter.position + new Vector3(x, 0f, z);
    }

    private void OnDrawGizmosSelected()
    {
        if (_spawnAreaCenter == null) return;

        Vector3 size = new Vector3(_spawnAreaHalfExtents.x * 2f, 0.05f, _spawnAreaHalfExtents.y * 2f);

        Gizmos.color = new Color(0.4f, 0.8f, 0.2f, 0.35f);
        Gizmos.DrawCube(_spawnAreaCenter.position, size);

        Gizmos.color = new Color(0.4f, 0.8f, 0.2f, 1f);
        Gizmos.DrawWireCube(_spawnAreaCenter.position, size);
    }
}
