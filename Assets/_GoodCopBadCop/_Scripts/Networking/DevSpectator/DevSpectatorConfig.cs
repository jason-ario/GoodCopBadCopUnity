using System;
using System.Collections.Generic;
using System.Text;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

/// <summary>
/// Static configuration for the hidden developer spectator mode.
///
/// A whitelisted developer (see <see cref="DevSteamIds"/>) can open the Dev Session Browser
/// (Ctrl+Shift+O), list every public Steam lobby for this app, and join one as an invisible
/// spectator. Every lobby is created with <see cref="ReservedDevSlots"/> extra Steam slot(s)
/// that regular players can never occupy (enforced client-side on Steam join and server-side
/// in NGO connection approval), so a full lobby can still be spectated.
/// </summary>
public static class DevSpectatorConfig
{
    /// <summary>
    /// SteamID64s allowed to use developer spectator mode. Add your own ID here — the Dev
    /// Session Browser shows your current SteamID when opened in the Editor.
    /// </summary>
    public static readonly HashSet<ulong> DevSteamIds = new HashSet<ulong>
    {
        // 7656119XXXXXXXXXX,
    };

    /// <summary>Maximum number of real (visible) players per session.</summary>
    public const int MaxPlayers = 3;

    /// <summary>Extra Steam lobby slots reserved for developer spectators.</summary>
    public const int ReservedDevSlots = 1;

    /// <summary>Steam lobby member-data key every member publishes on entering a lobby.</summary>
    public const string MemberRoleKey = "gcbc_role";
    public const string RolePlayer = "player";
    public const string RoleSpectator = "spec";

    /// <summary>Host-maintained lobby data: count of visible players (excludes spectators).</summary>
    public const string LobbyDataPlayerCount = "player_count";
    public const string LobbyDataMaxPlayers = "max_players";

    private const string SpectatorPayloadPrefix = "GCBC_DEVSPEC:";

    public static bool IsDevSteamId(ulong steamId) => steamId != 0 && DevSteamIds.Contains(steamId);

    /// <summary>True when the local Steam user is whitelisted as a developer.</summary>
    public static bool IsLocalDev => SteamClient.IsValid && IsDevSteamId(SteamClient.SteamId.Value);

    /// <summary>
    /// Builds the NGO connection payload. Regular players send their stable
    /// <see cref="PlayerSessionIdentity"/> so the host can recognise them when they rejoin.
    /// </summary>
    public static byte[] BuildConnectionPayload(bool asSpectator)
    {
        if (!asSpectator)
            return PlayerSessionIdentity.BuildPayload();

        if (!SteamClient.IsValid)
            return Array.Empty<byte>();

        return Encoding.UTF8.GetBytes(SpectatorPayloadPrefix + SteamClient.SteamId.Value);
    }

    /// <summary>Returns true if the payload is a spectator request from a whitelisted developer.</summary>
    public static bool TryParseSpectatorPayload(byte[] payload, out ulong steamId)
    {
        steamId = 0;
        if (payload == null || payload.Length == 0)
            return false;

        string text;
        try { text = Encoding.UTF8.GetString(payload); }
        catch (Exception) { return false; }

        if (!text.StartsWith(SpectatorPayloadPrefix, StringComparison.Ordinal))
            return false;

        return ulong.TryParse(text.Substring(SpectatorPayloadPrefix.Length), out steamId) && IsDevSteamId(steamId);
    }

    /// <summary>
    /// True if this lobby member is a hidden spectator. Members that haven't published a role
    /// yet are treated as spectators only if whitelisted, so a spectating developer is never
    /// briefly visible between the Steam join and their member data arriving.
    /// </summary>
    public static bool IsSpectatorMember(Lobby lobby, Friend member)
    {
        string role = lobby.GetMemberData(member, MemberRoleKey);
        if (role == RoleSpectator)
            return true;
        if (string.IsNullOrEmpty(role))
            return IsDevSteamId(member.Id.Value);
        return false;
    }

    /// <summary>Number of visible (non-spectator) players in a lobby the local user is in.</summary>
    public static int CountVisibleMembers(Lobby lobby)
    {
        int count = 0;
        foreach (var member in lobby.Members)
        {
            if (!IsSpectatorMember(lobby, member))
                count++;
        }
        return count;
    }

    /// <summary>Player count/max to display for a lobby seen from outside (browser rows).</summary>
    public static string FormatBrowserPlayerCount(Lobby lobby)
    {
        string countData = lobby.GetData(LobbyDataPlayerCount);
        string maxData = lobby.GetData(LobbyDataMaxPlayers);

        int count = int.TryParse(countData, out int c) ? c : Mathf.Min(lobby.MemberCount, MaxPlayers);
        int max = int.TryParse(maxData, out int m) ? m : Mathf.Max(1, lobby.MaxMembers - ReservedDevSlots);
        return $"{count}/{max}";
    }
}
