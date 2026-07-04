using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerSpawner : NetworkBehaviour
{
    public static PlayerSpawner Instance;

    /// <summary>Fired on the server the first time a player is spawned at a lobby spawn point.</summary>
    public static event Action<ulong> OnPlayerSpawnedAtLobby;

    [Header("Player Prefab")]
    [SerializeField] private GameObject playerPrefab;

    [Header("Lobby Spawn Points")]
    [SerializeField] private Transform singlePlayerLobbySpawnPoint;
    [SerializeField] private Transform[] multiplayerLobbySpawnPoints;

    [Header("Gameplay Spawn Points")]
    [SerializeField] private Transform singlePlayerSpawnPoint;
    [SerializeField] private Transform[] multiplayerSpawnPoints;

    [Header("Outside Bunker Spawn Points")]
    [Tooltip("Single-player spawn used when waking up at the start of each new day (inside the bunker area).")]
    [SerializeField] private Transform singlePlayerOutsideBunkerSpawnPoint;
    [Tooltip("Per-client spawns used when waking up at the start of each new day (inside the bunker area).")]
    [SerializeField] private Transform[] multiplayerOutsideBunkerSpawnPoints;

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
    /// Spawns the player prefab for the given client at the lobby spawn point.
    /// SERVER ONLY.
    /// </summary>
    public void SpawnPlayerAtLobby(ulong clientId, bool isSinglePlayer)
    {
        SpawnPlayerAtPoint(clientId, GetLobbySpawnPoint(clientId, isSinglePlayer), isOutside: true);
        OnPlayerSpawnedAtLobby?.Invoke(clientId);
    }

    /// <summary>
    /// Spawns the player prefab for the given client at the gameplay spawn point.
    /// SERVER ONLY.
    /// </summary>
    public void SpawnPlayer(ulong clientId, bool isSinglePlayer)
    {
        SpawnPlayerAtPoint(clientId, GetSpawnPoint(clientId, isSinglePlayer), isOutside: false);
    }

    /// <summary>
    /// Spawns the player prefab for the given client at the booth gameplay spawn point.
    /// SERVER ONLY.
    /// </summary>
    public void SpawnPlayerAtBooth(ulong clientId)
    {
        SpawnPlayerAtPoint(clientId, GetBoothSpawnPoint(clientId), isOutside: false);
    }

    private void SpawnPlayerAtPoint(ulong clientId, Transform spawnPoint, bool isOutside)
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[PlayerSpawner] SpawnPlayerAtPoint called on client. Ignored.");
            return;
        }

        // Guard against race conditions where two code paths both attempt to spawn the same client
        // (e.g. SpawnAllPlayersAtLobby and OnClientConnected overlapping during LobbyTransitionSequence).
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var existingClient) &&
            existingClient.PlayerObject != null)
        {
            Debug.LogWarning($"[PlayerSpawner] Client {clientId} already has a player object — skipping duplicate spawn.");
            return;
        }

        if (playerPrefab == null)
        {
            Debug.LogError("[PlayerSpawner] playerPrefab is not assigned.");
            return;
        }

        var go = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
        var networkObject = go.GetComponent<NetworkObject>();
        networkObject.SpawnAsPlayerObject(clientId);

        var playerInstance = go.GetComponent<PlayerInstance>();
        if (playerInstance != null)
            playerInstance.SetIsOutside(isOutside);

        Debug.Log($"[PlayerSpawner] Spawned player for client {clientId} at {spawnPoint.name} (isOutside: {isOutside})");
    }

    /// <summary>
    /// Determines the correct gameplay spawn point for the given client.
    /// </summary>
    public Transform GetSpawnPoint(ulong clientId, bool isSinglePlayer)
    {
        if (isSinglePlayer || multiplayerSpawnPoints.Length == 0)
            return singlePlayerSpawnPoint;

        int index = (int)(clientId % (ulong)multiplayerSpawnPoints.Length);
        return multiplayerSpawnPoints[index];
    }

    /// <summary>
    /// Determines the correct lobby spawn point for the given client.
    /// </summary>
    public Transform GetLobbySpawnPoint(ulong clientId, bool isSinglePlayer)
    {
        if (isSinglePlayer || multiplayerLobbySpawnPoints.Length == 0)
            return singlePlayerLobbySpawnPoint != null ? singlePlayerLobbySpawnPoint : singlePlayerSpawnPoint;

        int index = (int)(clientId % (ulong)multiplayerLobbySpawnPoints.Length);
        return multiplayerLobbySpawnPoints[index];
    }

    /// <summary>
    /// Returns the booth (gameplay) spawn point used for mid-shift and end-of-shift resets.
    /// </summary>
    public Transform GetBoothSpawnPoint(ulong clientId)
    {
        int index = (int)(clientId % (ulong)multiplayerSpawnPoints.Length);
        return multiplayerSpawnPoints[index];
    }

    /// <summary>
    /// Returns the outside (lobby) spawn point used when transitioning between days.
    /// Players are placed here after the end-of-shift report so they re-enter the booth
    /// themselves to begin the next shift.
    /// </summary>
    public Transform GetOutsideSpawnPoint(ulong clientId)
    {
        bool isSinglePlayer = NetworkManager.Singleton.ConnectedClients.Count <= 1;
        return GetLobbySpawnPoint(clientId, isSinglePlayer);
    }

    /// <summary>
    /// Returns the outside-bunker spawn point used when waking up at the start of each new day.
    /// Falls back to <see cref="singlePlayerSpawnPoint"/> if no bunker points are assigned.
    /// </summary>
    public Transform GetOutsideBunkerSpawnPoint(ulong clientId)
    {
        bool isSinglePlayer = NetworkManager.Singleton.ConnectedClients.Count <= 1;
        if (isSinglePlayer || multiplayerOutsideBunkerSpawnPoints == null || multiplayerOutsideBunkerSpawnPoints.Length == 0)
            return singlePlayerOutsideBunkerSpawnPoint != null ? singlePlayerOutsideBunkerSpawnPoint : singlePlayerSpawnPoint;

        int index = (int)(clientId % (ulong)multiplayerOutsideBunkerSpawnPoints.Length);
        return multiplayerOutsideBunkerSpawnPoints[index];
    }
}
