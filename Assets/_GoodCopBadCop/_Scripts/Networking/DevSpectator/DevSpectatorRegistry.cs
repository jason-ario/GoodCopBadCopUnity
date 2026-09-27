using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Runtime state for hidden developer spectators.
/// Server side: tracks which NGO client IDs are spectators (populated in LobbyManager's
/// connection approval) so spawning and player-count logic can ignore them.
/// Client side: <see cref="IsLocalSpectating"/> is true while this machine is spectating.
/// </summary>
public static class DevSpectatorRegistry
{
    private static readonly HashSet<ulong> SpectatorClientIds = new HashSet<ulong>();

    /// <summary>True on the developer's machine while connected (or connecting) as a spectator.</summary>
    public static bool IsLocalSpectating { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        SpectatorClientIds.Clear();
        IsLocalSpectating = false;
    }

    public static void RegisterSpectator(ulong clientId) => SpectatorClientIds.Add(clientId);
    public static void UnregisterSpectator(ulong clientId) => SpectatorClientIds.Remove(clientId);
    public static void ClearServerState() => SpectatorClientIds.Clear();

    /// <summary>SERVER: true if the NGO client is a hidden developer spectator.</summary>
    public static bool IsSpectator(ulong clientId) => SpectatorClientIds.Contains(clientId);

    /// <summary>SERVER: number of connected clients that are real players (excludes spectators).</summary>
    public static int PlayerClientCount(NetworkManager networkManager)
    {
        if (networkManager == null)
            return 0;

        int count = 0;
        foreach (ulong id in networkManager.ConnectedClientsIds)
        {
            if (!SpectatorClientIds.Contains(id))
                count++;
        }
        return count;
    }

    /// <summary>SERVER: connected client IDs that are real players (excludes spectators).</summary>
    public static IEnumerable<ulong> PlayerClientIds(NetworkManager networkManager)
    {
        if (networkManager == null)
            yield break;

        foreach (ulong id in networkManager.ConnectedClientsIds)
        {
            if (!SpectatorClientIds.Contains(id))
                yield return id;
        }
    }
}
