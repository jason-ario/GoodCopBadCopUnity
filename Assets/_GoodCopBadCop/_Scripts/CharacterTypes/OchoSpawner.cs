using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Scene component that spawns the Ocho NetworkObject prefab at a configured world position.
///
/// Place this MonoBehaviour anywhere in the scene (e.g. on a dedicated "OchoSpawner"
/// GameObject). Assign the Ocho prefab (which must be registered in NetworkManager's
/// prefab list) and a <see cref="_spawnPoint"/> Transform positioned where Ocho should
/// appear — somewhere in the tree line, far enough from the player that he reads as a
/// distant watcher.
///
/// Call <see cref="SpawnOcho"/> from Day logic (e.g. Day_02.NightPhaseStarted) or
/// from any other server-side context.
///
/// Only one Ocho instance is allowed at a time; repeated calls while an instance is
/// already live are safely ignored.
/// </summary>
public class OchoSpawner : MonoBehaviour
{
    [Header("Ocho")]
    [Tooltip("The Ocho prefab. Must contain a NetworkObject and OchoWatcherBehaviour. " +
             "Must be registered in NetworkManager's Network Prefabs list.")]
    [SerializeField] private GameObject _ochoPrefab;

    [Tooltip("World position and rotation at which Ocho spawns. " +
             "Position him deep in the tree line so he appears as a distant silhouette.")]
    [SerializeField] private Transform _spawnPoint;

    // ── Runtime state ──────────────────────────────────────────────────────────

    private NetworkObject _activeInstance;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Instantiates and network-spawns the Ocho prefab at <see cref="_spawnPoint"/>.
    /// SERVER ONLY. Safe to call multiple times — ignored while an instance is alive.
    /// </summary>
    public void SpawnOcho()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[OchoSpawner] SpawnOcho called on a non-server client — ignoring.", this);
            return;
        }

        // Guard: only one Ocho at a time.
        if (_activeInstance != null && _activeInstance.IsSpawned)
        {
            Debug.LogWarning("[OchoSpawner] Ocho is already in the scene — ignoring duplicate spawn.", this);
            return;
        }

        if (_ochoPrefab == null)
        {
            Debug.LogError("[OchoSpawner] _ochoPrefab is not assigned.", this);
            return;
        }

        Vector3    spawnPos = _spawnPoint != null ? _spawnPoint.position : transform.position;
        Quaternion spawnRot = _spawnPoint != null ? _spawnPoint.rotation : Quaternion.identity;

        GameObject instance = Instantiate(_ochoPrefab, spawnPos, spawnRot);
        NetworkObject netObj = instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[OchoSpawner] Ocho prefab is missing a NetworkObject component. Destroying.", this);
            Destroy(instance);
            return;
        }

        netObj.Spawn(destroyWithScene: true);
        _activeInstance = netObj;

        Debug.Log($"[OchoSpawner] Ocho spawned at {spawnPos}.", this);
    }

    /// <summary>
    /// Despawns the current Ocho instance if one exists.
    /// SERVER ONLY. Safe to call when no instance is alive.
    /// </summary>
    public void DespawnOcho()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (_activeInstance != null && _activeInstance.IsSpawned)
        {
            _activeInstance.Despawn(destroy: true);
            Debug.Log("[OchoSpawner] Ocho despawned by spawner.", this);
        }

        _activeInstance = null;
    }

    /// <summary>
    /// True if an Ocho instance is currently live in the scene.
    /// </summary>
    public bool IsOchoPresent => _activeInstance != null && _activeInstance.IsSpawned;
}
