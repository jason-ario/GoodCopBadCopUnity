using Unity.Netcode;
using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    public static PlayerSpawner Instance;

    [Header("Prefabs")]
    [SerializeField] private GameObject playerPrefab;

    [Header("Spawn Points")]
    [SerializeField] private Transform singlePlayerSpawnPoint;
    [SerializeField] private Transform[] multiplayerSpawnPoints;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    /// <summary>
    /// Spawns a player for the given client.
    /// SERVER ONLY.
    /// </summary>
    public void SpawnPlayer(ulong clientId, bool isSinglePlayer)
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("SpawnPlayer called on client. Ignored.");
            return;
        }

        // Safety: prevent double spawning
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) &&
            client.PlayerObject != null)
        {
            Debug.LogWarning($"Player already exists for client {clientId}");
            return;
        }

        Transform spawnPoint = GetSpawnPoint(clientId, isSinglePlayer);

        GameObject player = Instantiate(
            playerPrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        NetworkObject netObj = player.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("Player prefab is missing NetworkObject!");
            Destroy(player);
            return;
        }

        netObj.SpawnAsPlayerObject(clientId, true);

        Debug.Log($"[PlayerSpawner] Spawned player for client {clientId} at {spawnPoint.name}");
    }

    /// <summary>
    /// Determines the correct spawn point.
    /// </summary>
    private Transform GetSpawnPoint(ulong clientId, bool isSinglePlayer)
    {
        if (isSinglePlayer || multiplayerSpawnPoints.Length == 0)
            return singlePlayerSpawnPoint;

        int index = (int)(clientId % (ulong)multiplayerSpawnPoints.Length);
        return multiplayerSpawnPoints[index];
    }
}
