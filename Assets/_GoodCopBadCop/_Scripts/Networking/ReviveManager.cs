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

    private const float RosterRefreshInterval = 0.25f;

    // Server-authoritative roster replicated to every peer. NetworkManager.ConnectedClientsList and
    // NetworkClient.PlayerObject are not reliably populated on non-host clients, so client UI (e.g. the
    // HQ "Call in Backup" order) must read player/dead state from here instead of iterating that list.
    private NetworkList<ulong> _playerClientIds;
    private NetworkList<ulong> _deadPlayerClientIds;
    private float _nextRosterRefreshTime;

    private void Awake()
    {
        _playerClientIds = new NetworkList<ulong>();
        _deadPlayerClientIds = new NetworkList<ulong>();

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

        if (IsServer)
            RefreshRosterServer();
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer || Time.unscaledTime < _nextRosterRefreshTime)
            return;

        _nextRosterRefreshTime = Time.unscaledTime + RosterRefreshInterval;
        RefreshRosterServer();
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

    /// <summary>True when at least one other (non-spectator) player is in the session. Valid on every peer.</summary>
    public bool HasTeammate(ulong localClientId)
    {
        foreach (ulong id in _playerClientIds)
        {
            if (id != localClientId)
                return true;
        }
        return false;
    }

    /// <summary>True when another player in the session is currently dead. Valid on every peer.</summary>
    public bool HasDeadTeammate(ulong localClientId)
    {
        foreach (ulong id in _deadPlayerClientIds)
        {
            if (id != localClientId)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Asks the server to revive the first dead teammate of the calling client (mid-game revive),
    /// charging <paramref name="cost"/> from the shared money pool. The server picks the target from
    /// its authoritative client list and only charges when a revive actually happens.
    /// Can be called from any peer.
    /// </summary>
    public void RequestReviveDeadTeammate(int cost)
    {
        if (IsServer)
            ReviveDeadTeammateServer(NetworkManager.Singleton.LocalClientId, cost);
        else
            RequestReviveDeadTeammateServerRpc(cost);
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

    /// <summary>
    /// SERVER ONLY. Finds a dead teammate of <paramref name="requesterClientId"/>, deducts the cost and
    /// revives them. Refunds the cost if the revive could not be completed.
    /// </summary>
    private void ReviveDeadTeammateServer(ulong requesterClientId, int cost)
    {
        if (!IsServer)
            return;

        if (!TryFindDeadTeammateServer(requesterClientId, out ulong targetClientId))
        {
            Debug.LogWarning($"[ReviveManager] Client {requesterClientId} requested backup, but no dead teammate was found.");
            return;
        }

        GlobalHostVariables bank = GlobalHostVariables.Instance;
        if (cost > 0 && (bank == null || !bank.SubtractMoney(cost)))
        {
            Debug.LogWarning($"[ReviveManager] Client {requesterClientId} requested backup without enough funds ({cost} required).");
            return;
        }

        if (!RevivePlayerServer(targetClientId, isNewDay: false) && cost > 0)
            bank.AddMoney(cost);

        RefreshRosterServer();
    }

    private static bool TryFindDeadTeammateServer(ulong requesterClientId, out ulong targetClientId)
    {
        targetClientId = ulong.MaxValue;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.ClientId == requesterClientId || DevSpectatorRegistry.IsSpectator(client.ClientId))
                continue;
            if (client.PlayerObject == null)
                continue;

            PlayerHealth health = client.PlayerObject.GetComponent<PlayerHealth>();
            if (health != null && health.IsDead)
            {
                targetClientId = client.ClientId;
                return true;
            }
        }

        return false;
    }

    /// <summary>SERVER ONLY. Rebuilds the replicated player / dead-player roster when it changed.</summary>
    private void RefreshRosterServer()
    {
        if (!IsServer || NetworkManager.Singleton == null)
            return;

        var players = new System.Collections.Generic.List<ulong>();
        var dead = new System.Collections.Generic.List<ulong>();

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (DevSpectatorRegistry.IsSpectator(client.ClientId) || client.PlayerObject == null)
                continue;

            players.Add(client.ClientId);

            PlayerHealth health = client.PlayerObject.GetComponent<PlayerHealth>();
            if (health != null && health.IsDead)
                dead.Add(client.ClientId);
        }

        SyncList(_playerClientIds, players);
        SyncList(_deadPlayerClientIds, dead);
    }

    private static void SyncList(NetworkList<ulong> target, System.Collections.Generic.List<ulong> source)
    {
        bool same = target.Count == source.Count;
        for (int i = 0; same && i < source.Count; i++)
            same = target[i] == source[i];
        if (same)
            return;

        target.Clear();
        foreach (ulong id in source)
            target.Add(id);
    }

    private bool RevivePlayerServer(ulong clientId, bool isNewDay)
    {
        if (!IsServer || NetworkManager.Singleton == null)
            return false;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            Debug.LogWarning($"[ReviveManager] Cannot revive client {clientId} — client not connected.");
            return false;
        }

        if (PlayerSpawner.Instance == null)
        {
            Debug.LogError($"[ReviveManager] Cannot revive client {clientId} — PlayerSpawner is unavailable.");
            return false;
        }

        NetworkObject deadPlayerObject = client.PlayerObject;
        if (deadPlayerObject == null)
        {
            Debug.LogWarning($"[ReviveManager] Cannot revive client {clientId} — no player object is assigned.");
            return false;
        }

        PlayerHealth health = deadPlayerObject.GetComponent<PlayerHealth>();
        if (health == null || !health.IsDead)
        {
            Debug.LogWarning($"[ReviveManager] Ignoring revive request for client {clientId} — their player is not dead.");
            return false;
        }

        CorpseResurrectionController corpse = deadPlayerObject.GetComponent<CorpseResurrectionController>();
        bool hasActiveCorpse = corpse != null && corpse.HasActiveCorpse;
        bool isSinglePlayer = DevSpectatorRegistry.PlayerClientCount(NetworkManager.Singleton) <= 1;
        bool spawnAtBooth = !isNewDay && ShouldReviveAtBooth();

        if (hasActiveCorpse)
        {
            // Do not despawn and immediately respawn the same player object. Death disables
            // network behaviours (such as NetworkTransform), and a respawn with a different
            // behaviour layout can corrupt the spawn payload received by other clients.
            // SpawnAsPlayerObject safely replaces the client's PlayerObject and automatically
            // demotes this existing corpse to a normal NetworkObject.
            // Clear client-local state on the old PlayerObject before replacing it.
            corpse.PrepareForPlayerObjectReplacement();

            bool replacementSpawned = isNewDay
                ? PlayerSpawner.Instance.ReplacePlayerAtOutsideBunker(clientId)
                : spawnAtBooth
                    ? PlayerSpawner.Instance.ReplacePlayerAtBooth(clientId)
                    : PlayerSpawner.Instance.ReplacePlayerAtLobby(clientId, isSinglePlayer);
            if (!replacementSpawned)
                return false;

            // The retained corpse must be controlled by the server, not by the revived client.
            deadPlayerObject.ChangeOwnership(NetworkManager.ServerClientId);
            corpse.DetachFromPlayer();
        }
        else
        {
            if (deadPlayerObject != null)
            {
                Debug.Log($"[ReviveManager] Despawning dead player object for client {clientId}.");
                deadPlayerObject.Despawn(true);
            }

            if (isNewDay)
                PlayerSpawner.Instance.SpawnPlayerAtOutsideBunker(clientId);
            else if (spawnAtBooth)
                PlayerSpawner.Instance.SpawnPlayerAtBooth(clientId);
            else
                PlayerSpawner.Instance.SpawnPlayerAtLobby(clientId, isSinglePlayer);
        }

        OnPlayerRevived?.Invoke(clientId);
        Debug.Log($"[ReviveManager] Revived client {clientId} (isNewDay: {isNewDay}, atBooth: {spawnAtBooth}).");
        return true;
    }

    /// <summary>
    /// Day 1 only: a mid-game revive that happens before the shift has ended respawns the
    /// player inside the booth instead of at the lobby, so they rejoin the shift directly.
    /// The shift counts as ended once <see cref="ShiftManager.EndShift"/> has run, i.e. the
    /// day is in <see cref="ShiftManager.DayPhase.PostShift"/> and <c>shiftStarted</c> is cleared.
    /// SERVER ONLY.
    /// </summary>
    private static bool ShouldReviveAtBooth()
    {
        ShiftManager shift = ShiftManager.Instance;
        if (shift == null || shift.CurrentDay != 1)
            return false;

        bool shiftEnded = shift.CurrentPhase == ShiftManager.DayPhase.PostShift && !shift.shiftStarted.Value;
        return !shiftEnded;
    }

    // ── ServerRpc ──────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void RevivePlayerServerRpc(ulong clientId, bool isNewDay)
    {
        RevivePlayerServer(clientId, isNewDay);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestReviveDeadTeammateServerRpc(int cost, ServerRpcParams rpcParams = default)
    {
        ReviveDeadTeammateServer(rpcParams.Receive.SenderClientId, cost);
    }
}
