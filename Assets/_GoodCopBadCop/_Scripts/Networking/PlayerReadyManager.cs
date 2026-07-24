using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Steamworks;

/// <summary>
/// Tracks the ready state of each connected client in the pre-game lobby.
/// Lives as a NetworkBehaviour so state changes are authoritative on the server
/// and broadcast to all clients via ClientRpc.
///
/// States are keyed by Steam ID (self-reported by the ready-ing client) rather than NGO
/// clientId, because <see cref="StartCampaignScreen"/> displays and matches players using the
/// Steam lobby member list (Friend.Id), not NGO client IDs.
/// </summary>
public class PlayerReadyManager : NetworkBehaviour
{
    public static PlayerReadyManager Instance { get; private set; }

    /// <summary>Fired on all clients whenever any player's ready state changes. Keyed by Steam ID.</summary>
    public event Action<Dictionary<ulong, bool>> OnReadyStatesChanged;

    private readonly Dictionary<ulong, bool> _readyStates = new Dictionary<ulong, bool>();

    // ---------------------------------------------------------------------------
    // Unity Lifecycle
    // ---------------------------------------------------------------------------

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
        _readyStates.Clear();
    }

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Called by the local player to signal their ready state to the server.
    /// </summary>
    public void SetReady(bool isReady)
    {
        ulong steamId = SteamClient.IsValid ? SteamClient.SteamId.Value : 0;
        SetReadyServerRpc(isReady, steamId);
    }

    /// <summary>Resets all ready states. Call before a new session begins.</summary>
    public void ResetAll()
    {
        if (!IsServer) return;
        _readyStates.Clear();
        BroadcastReadyStates();
    }

    // ---------------------------------------------------------------------------
    // Server
    // ---------------------------------------------------------------------------

    [ServerRpc(RequireOwnership = false)]
    private void SetReadyServerRpc(bool isReady, ulong steamId, ServerRpcParams rpcParams = default)
    {
        _readyStates[steamId] = isReady;

        Debug.Log($"[PlayerReadyManager] SteamId {steamId} ready={isReady}");
        BroadcastReadyStates();
    }

    private void BroadcastReadyStates()
    {
        // Serialise the dictionary as parallel arrays for the ClientRpc.
        ulong[] ids = new ulong[_readyStates.Count];
        bool[] states = new bool[_readyStates.Count];
        int i = 0;
        foreach (var kvp in _readyStates)
        {
            ids[i] = kvp.Key;
            states[i] = kvp.Value;
            i++;
        }

        UpdateReadyStatesClientRpc(ids, states);
    }

    // ---------------------------------------------------------------------------
    // Clients
    // ---------------------------------------------------------------------------

    [ClientRpc]
    private void UpdateReadyStatesClientRpc(ulong[] ids, bool[] states)
    {
        _readyStates.Clear();
        for (int i = 0; i < ids.Length; i++)
            _readyStates[ids[i]] = states[i];

        OnReadyStatesChanged?.Invoke(new Dictionary<ulong, bool>(_readyStates));
    }
}
