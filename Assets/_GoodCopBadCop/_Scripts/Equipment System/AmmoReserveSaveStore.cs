using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Server-only bookkeeping that keeps each player's <see cref="PlayerAmmoReserve"/> across
/// disconnects and day-start checkpoints.
///
///  - <b>Live</b>: reserves of connected players, keyed by the identity each owner reports on spawn
///    (<see cref="PlayerAmmoReserve"/> sends "steam:&lt;id&gt;", or a host/client fallback outside Steam).
///  - <b>Stash</b>: counts waiting for a player who isn't connected — restored from the checkpoint
///    (<see cref="RestoreAll"/>) or parked when a client disconnects mid-session. A matching
///    player claims them when they register.
///
/// <see cref="ShiftManager.CaptureWorkdaySaveState"/> writes <see cref="CaptureAll"/> into
/// <see cref="WorkdaySaveState.AmmoReserves"/>; <see cref="ShiftManager.RestoreWorkdaySaveState"/>
/// feeds it back through <see cref="RestoreAll"/>.
///
/// Static state outlives scene reloads, so the stash is cleared on every single-mode scene load
/// (return to menu, death screen, connection loss). Live entries unregister themselves on despawn.
/// </summary>
public static class AmmoReserveSaveStore
{
    private static readonly Dictionary<string, PlayerAmmoReserve> Live = new();
    private static readonly Dictionary<string, AmmoReserveSaveData> Stash = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Live.Clear();
        Stash.Clear();
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
            Stash.Clear();
    }

    /// <summary>
    /// Registers a connected player's reserve and applies any stashed counts for that identity.
    /// Returns the key actually used (suffixed with the client id if another live reserve already
    /// owns <paramref name="requestedKey"/>, e.g. two ParrelSync clones on one Steam account).
    /// </summary>
    public static string RegisterLive(PlayerAmmoReserve reserve, string requestedKey)
    {
        if (reserve == null) return null;

        PruneDestroyed();

        string key = string.IsNullOrEmpty(requestedKey) ? $"client:{reserve.OwnerClientId}" : requestedKey;
        if (Live.TryGetValue(key, out PlayerAmmoReserve existing) && existing != reserve)
            key = $"{key}#{reserve.OwnerClientId}";

        Live[key] = reserve;

        if (Stash.Remove(key, out AmmoReserveSaveData saved))
            reserve.ServerAddFromSave(saved);

        return key;
    }

    /// <summary>
    /// Removes a despawning reserve. With <paramref name="stashAmounts"/> (a client left a session
    /// that keeps running), its counts are parked so they reach the next checkpoint and come back if
    /// the player rejoins.
    /// </summary>
    public static void UnregisterLive(PlayerAmmoReserve reserve, string key, bool stashAmounts)
    {
        if (reserve == null || string.IsNullOrEmpty(key)) return;

        if (Live.TryGetValue(key, out PlayerAmmoReserve existing) && existing == reserve)
            Live.Remove(key);

        if (!stashAmounts) return;

        AmmoReserveSaveData data = reserve.CaptureSaveData(key);
        if (!data.IsEmpty)
            Stash[key] = data;
    }

    /// <summary>Snapshot of every connected and stashed reserve with ammo in it.</summary>
    public static AmmoReserveSaveData[] CaptureAll()
    {
        PruneDestroyed();

        var result = new List<AmmoReserveSaveData>();
        foreach (KeyValuePair<string, PlayerAmmoReserve> pair in Live)
        {
            AmmoReserveSaveData data = pair.Value.CaptureSaveData(pair.Key);
            if (!data.IsEmpty) result.Add(data);
        }

        foreach (KeyValuePair<string, AmmoReserveSaveData> pair in Stash)
        {
            if (Live.ContainsKey(pair.Key) || pair.Value == null || pair.Value.IsEmpty) continue;
            result.Add(pair.Value.Copy(pair.Key));
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return result.ToArray();
    }

    /// <summary>
    /// Replaces all reserves with a checkpoint snapshot. Connected players are set to their saved
    /// counts (or zero when they weren't in the save, so a retried day can't keep ammo gained during
    /// the failed attempt); entries for players not connected yet wait in the stash.
    /// </summary>
    public static void RestoreAll(AmmoReserveSaveData[] entries)
    {
        PruneDestroyed();
        Stash.Clear();

        if (entries != null)
        {
            foreach (AmmoReserveSaveData entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Key) || entry.IsEmpty) continue;
                Stash[entry.Key] = entry.Copy();
            }
        }

        foreach (KeyValuePair<string, PlayerAmmoReserve> pair in Live)
        {
            Stash.Remove(pair.Key, out AmmoReserveSaveData saved);
            pair.Value.ServerSetFromSave(saved);
        }
    }

    private static readonly List<string> PruneBuffer = new();

    private static void PruneDestroyed()
    {
        PruneBuffer.Clear();
        foreach (KeyValuePair<string, PlayerAmmoReserve> pair in Live)
            if (pair.Value == null) PruneBuffer.Add(pair.Key);

        foreach (string key in PruneBuffer)
            Live.Remove(key);
    }
}
