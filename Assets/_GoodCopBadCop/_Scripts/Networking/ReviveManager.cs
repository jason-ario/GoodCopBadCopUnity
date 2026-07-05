using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative manager for reviving dead players.
/// Handles both mid-game revives (lobby spawn points) and new-day revives (outside bunker spawn points).
/// Subscribe to <see cref="OnPlayerRevived"/> to react to revive events across the codebase.
/// </summary>
public class ReviveManager : NetworkBehaviour
{
    public static ReviveManager Instance;

    /// <summary>Fired on the server whenever a player is successfully revived. Argument is the client ID.</summary>
    public static event Action<ulong> OnPlayerRevived;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += ReviveDeadPlayersForNewDay;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= ReviveDeadPlayersForNewDay;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Revives a player by despawning their current dead PlayerObject and spawning a fresh one.
    /// Can be called from any context — routes to the server when not already on the server.
    /// </summary>
    /// <param name="clientId">The client to revive.</param>
    /// <param name="isNewDay">
    /// When <c>true</c>, spawns at outside bunker spawn points (new-day revival).
    /// When <c>false</c>, spawns at lobby spawn points (mid-game revival).
    /// </param>
    public void RevivePlayer(ulong clientId, bool isNewDay)
    {
        if (IsServer)
            RevivePlayerServer(clientId, isNewDay);
        else
            RevivePlayerServerRpc(clientId, isNewDay);
    }

    // ── Server-only logic ──────────────────────────────────────────────────────

    /// <summary>
    /// Revives all dead players at the start of a new day, spawning them at outside bunker spawn points.
    /// SERVER ONLY.
    /// </summary>
    private void ReviveDeadPlayersForNewDay()
    {
        if (!IsServer) return;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            var health = client.PlayerObject.GetComponent<PlayerHealth>();
            if (health != null && health.IsDead)
            {
                Debug.Log($"[ReviveManager] Auto-reviving dead player (client {client.ClientId}) for new day.");
                RevivePlayerServer(client.ClientId, isNewDay: true);
            }
        }
    }

    private void RevivePlayerServer(ulong clientId, bool isNewDay)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            Debug.LogWarning($"[ReviveManager] Cannot revive client {clientId} — client not connected.");
            return;
        }

        // Despawn the existing dead PlayerObject so the guard in PlayerSpawner clears
        // and NetworkManager reassigns the PlayerObject on the fresh spawn.
        if (client.PlayerObject != null)
        {
            Debug.Log($"[ReviveManager] Despawning dead player object for client {clientId}.");
            client.PlayerObject.Despawn(true);
        }

        bool isSinglePlayer = NetworkManager.Singleton.ConnectedClients.Count <= 1;

        if (isNewDay)
            PlayerSpawner.Instance.SpawnPlayerAtOutsideBunker(clientId);
        else
            PlayerSpawner.Instance.SpawnPlayerAtLobby(clientId, isSinglePlayer);

        OnPlayerRevived?.Invoke(clientId);
        Debug.Log($"[ReviveManager] Revived client {clientId} (isNewDay: {isNewDay}).");
    }

    // ── ServerRpc ──────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void RevivePlayerServerRpc(ulong clientId, bool isNewDay)
    {
        RevivePlayerServer(clientId, isNewDay);
    }
}
