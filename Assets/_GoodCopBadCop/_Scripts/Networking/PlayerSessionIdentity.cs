using System;
using System.Collections.Generic;
using System.Text;
using Steamworks;
using UnityEngine;

/// <summary>
/// Stable per-player identity that survives leaving and rejoining a session.
/// NGO hands a rejoining player a brand-new client ID, so the server can't tell them apart
/// from a new player by ID alone. Regular players send this identity in their connection
/// payload (Steam ID, or a per-process GUID on non-Steam transports), and the host maps it
/// to the current client ID during connection approval.
/// </summary>
public static class PlayerSessionIdentity
{
    private const string PlayerPayloadPrefix = "GCBC_PLAYER:";

    private static readonly Dictionary<ulong, string> ServerIdentities = new Dictionary<ulong, string>();
    private static string s_processFallbackId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        ServerIdentities.Clear();
        s_processFallbackId = null;
    }

    /// <summary>The local player's identity: Steam ID when available, otherwise a GUID for this process.</summary>
    public static string LocalIdentity
    {
        get
        {
            if (SteamClient.IsValid)
                return "steam:" + SteamClient.SteamId.Value;

            return s_processFallbackId ??= "local:" + Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>Connection payload a regular (non-spectator) player sends to the host.</summary>
    public static byte[] BuildPayload() => Encoding.UTF8.GetBytes(PlayerPayloadPrefix + LocalIdentity);

    /// <summary>Extracts the identity from a regular player's connection payload.</summary>
    public static bool TryParsePayload(byte[] payload, out string identity)
    {
        identity = null;
        if (payload == null || payload.Length == 0)
            return false;

        string text;
        try { text = Encoding.UTF8.GetString(payload); }
        catch (Exception) { return false; }

        if (!text.StartsWith(PlayerPayloadPrefix, StringComparison.Ordinal))
            return false;

        identity = text.Substring(PlayerPayloadPrefix.Length);
        return !string.IsNullOrEmpty(identity);
    }

    /// <summary>SERVER: records the identity of an approved client from its connection payload.</summary>
    public static void RegisterClient(ulong clientId, byte[] payload)
    {
        if (TryParsePayload(payload, out string identity))
            ServerIdentities[clientId] = identity;
        else
            ServerIdentities.Remove(clientId);
    }

    public static void UnregisterClient(ulong clientId) => ServerIdentities.Remove(clientId);

    public static void ClearServerState() => ServerIdentities.Clear();

    /// <summary>SERVER: the stable identity of a connected client, if it sent one.</summary>
    public static bool TryGetIdentity(ulong clientId, out string identity) =>
        ServerIdentities.TryGetValue(clientId, out identity);
}
