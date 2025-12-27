using Unity.Netcode;
using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    public static PlayerSpawner Instance;

    public GameObject playerPrefab;
    public Transform singlePlayerSpawnPoint;
    public Transform[] spawnPoints;

    private void Awake()
    {
        Instance = this;
    }

    public void SpawnPlayer(ulong clientId, bool isSinglePlayer)
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        int index = (int)(clientId % (ulong)spawnPoints.Length);
        Transform spawnPoint = spawnPoints[index];

        if (isSinglePlayer)
        {
            spawnPoint = singlePlayerSpawnPoint;
        }
        GameObject player = Instantiate(
            playerPrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        player.GetComponent<NetworkObject>()
            .SpawnAsPlayerObject(clientId, true);

        Debug.Log($"Spawned player for client {clientId} at {spawnPoint.name}");
    }
}