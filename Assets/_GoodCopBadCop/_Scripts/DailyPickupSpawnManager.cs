using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative manager for the daily runtime pickup set. The host records which
/// spawn-point/prefab combinations exist and their transforms, recreates that set on resume,
/// and lets NGO distribute the reconstructed objects to connected and late-joining clients.
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

    public static DailyPickupSpawnManager Instance { get; private set; }
    public bool HasInitializedForDay => ShiftManager.Instance != null && _initializedDay == ShiftManager.Instance.CurrentDay;

    private sealed class ActiveSpawn
    {
        public NetworkObject NetworkObject;
        public int SpawnPointIndex;
        public int PrefabIndex;
        public string SaveId;
    }

    private readonly List<ActiveSpawn> _activeSpawns = new();
    private int _initializedDay = -1;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Instance = this;

        if (!IsServer)
            return;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDayStart;
        else
            Debug.LogError("[DailyPickupSpawnManager] ShiftManager.Instance is null on spawn.", this);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;

        if (Instance == this)
            Instance = null;
        base.OnNetworkDespawn();
    }

    /// <summary>Captures the live runtime pickup set. Despawned entries are intentionally omitted.</summary>
    public DailyPickupSaveData[] CaptureSaveData()
    {
        var results = new List<DailyPickupSaveData>(_activeSpawns.Count);
        foreach (ActiveSpawn spawn in _activeSpawns)
        {
            if (spawn?.NetworkObject == null || !spawn.NetworkObject.IsSpawned)
                continue;

            results.Add(new DailyPickupSaveData
            {
                SpawnPointIndex = spawn.SpawnPointIndex,
                PrefabIndex = spawn.PrefabIndex,
                SaveId = spawn.SaveId,
                Position = spawn.NetworkObject.transform.position,
                RotationEuler = spawn.NetworkObject.transform.eulerAngles
            });
        }

        return results.ToArray();
    }

    private void OnDayStart()
    {
        if (!IsServer)
            return;

        int day = ShiftManager.Instance != null ? ShiftManager.Instance.CurrentDay : 0;
        if (_initializedDay == day)
            return;

        WorkdaySaveState savedState = SaveDataManager.Instance?.GetWorkdayState(day);

        if (savedState != null && savedState.DailyPickupsInitialized)
            RestoreSavedPickups(savedState.DailyPickups);
        else
            SpawnFreshPickups();

        _initializedDay = day;
        DailyPickupSaveData[] snapshot = CaptureSaveData();
        SaveDataManager.Instance?.SaveDayStartDailyPickupState(day, snapshot);
        SaveDataManager.Instance?.SaveCurrentWorkdayState();
        Debug.Log($"[DailyPickupSpawnManager] Initialized {snapshot.Length} persistent daily pickup(s) for Day {day}.");
    }

    private void RestoreSavedPickups(DailyPickupSaveData[] savedPickups)
    {
        DespawnLeftovers();
        if (savedPickups == null)
            return;

        foreach (DailyPickupSaveData saved in savedPickups)
        {
            if (saved == null || saved.SpawnPointIndex < 0 || saved.SpawnPointIndex >= (_spawnPoints?.Length ?? 0))
                continue;

            PickupSpawnPoint point = _spawnPoints[saved.SpawnPointIndex];
            GameObject prefab = point != null ? point.GetPrefab(saved.PrefabIndex) : null;
            if (prefab == null)
            {
                Debug.LogWarning($"[DailyPickupSpawnManager] Cannot restore daily pickup at point {saved.SpawnPointIndex}: configured prefab {saved.PrefabIndex} is unavailable.", this);
                continue;
            }

            SpawnAt(point, prefab, saved.SpawnPointIndex, saved.PrefabIndex, saved.SaveId, saved.Position, Quaternion.Euler(saved.RotationEuler));
        }
    }

    private void DespawnLeftovers()
    {
        foreach (ActiveSpawn spawn in _activeSpawns)
        {
            if (spawn?.NetworkObject != null && spawn.NetworkObject.IsSpawned)
                spawn.NetworkObject.Despawn();
        }

        _activeSpawns.Clear();
    }

    private void SpawnFreshPickups()
    {
        if (_spawnPoints == null || _spawnPoints.Length == 0)
        {
            Debug.LogWarning("[DailyPickupSpawnManager] No spawn points assigned.", this);
            return;
        }

        int available = _spawnPoints.Length;
        int spawnCount = Mathf.Clamp(UnityEngine.Random.Range(_minSpawnsPerDay, _maxSpawnsPerDay + 1), 0, available);
        List<int> shuffledPointIndices = ShuffledIndices(available);

        for (int i = 0; i < spawnCount; i++)
        {
            int spawnPointIndex = shuffledPointIndices[i];
            PickupSpawnPoint point = _spawnPoints[spawnPointIndex];
            GameObject prefab = point != null ? point.GetRandomPrefab() : null;
            int prefabIndex = point != null ? point.GetPrefabIndex(prefab) : -1;

            if (prefab == null || prefabIndex < 0)
            {
                Debug.LogWarning($"[DailyPickupSpawnManager] Spawn point '{point?.name ?? "<missing>"}' has no valid prefab — skipping.", this);
                continue;
            }

            SpawnAt(point, prefab, spawnPointIndex, prefabIndex, BuildSaveId(spawnPointIndex), point.transform.position, point.transform.rotation);
        }
    }

    private void SpawnAt(PickupSpawnPoint spawnPoint, GameObject prefab, int spawnPointIndex, int prefabIndex, string saveId, Vector3 position, Quaternion rotation)
    {
        GameObject instance = Instantiate(prefab, position, rotation);
        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        PickableObject pickable = instance.GetComponent<PickableObject>();

        if (networkObject == null || pickable == null)
        {
            Debug.LogError($"[DailyPickupSpawnManager] Prefab '{prefab.name}' must contain NetworkObject and PickableObject components.", this);
            Destroy(instance);
            return;
        }

        pickable.SetRuntimeSaveId(string.IsNullOrEmpty(saveId) ? BuildSaveId(spawnPointIndex) : saveId);
        networkObject.Spawn(true);
        _activeSpawns.Add(new ActiveSpawn
        {
            NetworkObject = networkObject,
            SpawnPointIndex = spawnPointIndex,
            PrefabIndex = prefabIndex,
            SaveId = pickable.SaveId
        });
    }

    private static string BuildSaveId(int spawnPointIndex) => $"DailyPickup/{spawnPointIndex}";

    private static List<int> ShuffledIndices(int count)
    {
        var indices = new List<int>(count);
        for (int i = 0; i < count; i++)
            indices.Add(i);

        for (int i = indices.Count - 1; i > 0; i--)
        {
            int swapIndex = UnityEngine.Random.Range(0, i + 1);
            (indices[i], indices[swapIndex]) = (indices[swapIndex], indices[i]);
        }

        return indices;
    }
}
