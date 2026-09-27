using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-only accumulator of flame exposure on burnable remains (dead player corpses, dead
/// mutant bodies, dead suspect bodies, gore <see cref="JunkItem"/>s). <see cref="Flamethrower"/>
/// feeds it once per validated hit report; when an object's exposure reaches the required burn
/// time it is "completely burned" and the caller removes it from play.
///
/// Exposure resets if the object hasn't been hit for <c>resetAfterSeconds</c>, so a body that
/// was only briefly licked by the flame doesn't stay half-burned forever. Keyed by
/// NetworkObjectId; stale entries (despawned objects / expired exposure) are pruned lazily.
/// </summary>
public static class FlameBurnTracker
{
    private struct Entry
    {
        public float Exposure;
        public float LastHitTime;
    }

    private static readonly Dictionary<ulong, Entry> Entries = new();
    private static readonly List<ulong> PruneBuffer = new();

    /// <summary>
    /// Adds <paramref name="seconds"/> of flame exposure to <paramref name="obj"/>.
    /// Returns true exactly once, when the accumulated exposure reaches
    /// <paramref name="requiredSeconds"/> (the entry is then cleared).
    /// <paramref name="isFirstHit"/> is true when this call started a fresh burn.
    /// </summary>
    public static bool AddExposure(NetworkObject obj, float seconds, float requiredSeconds,
        float resetAfterSeconds, out bool isFirstHit)
    {
        isFirstHit = false;
        if (obj == null || !obj.IsSpawned) return false;

        float now = Time.time;
        ulong id = obj.NetworkObjectId;

        if (!Entries.TryGetValue(id, out Entry entry) || now - entry.LastHitTime > resetAfterSeconds)
        {
            entry = default;
            isFirstHit = true;
        }

        entry.Exposure += Mathf.Max(0f, seconds);
        entry.LastHitTime = now;

        if (entry.Exposure >= requiredSeconds)
        {
            Entries.Remove(id);
            return true;
        }

        Entries[id] = entry;

        if (Entries.Count > 64)
            Prune(now, resetAfterSeconds);

        return false;
    }

    /// <summary>Forgets any partial burn on <paramref name="obj"/>.</summary>
    public static void Clear(NetworkObject obj)
    {
        if (obj != null)
            Entries.Remove(obj.NetworkObjectId);
    }

    private static void Prune(float now, float resetAfterSeconds)
    {
        PruneBuffer.Clear();
        NetworkManager nm = NetworkManager.Singleton;

        foreach (KeyValuePair<ulong, Entry> kv in Entries)
        {
            bool expired = now - kv.Value.LastHitTime > resetAfterSeconds;
            bool despawned = nm == null || nm.SpawnManager == null ||
                             !nm.SpawnManager.SpawnedObjects.ContainsKey(kv.Key);
            if (expired || despawned)
                PruneBuffer.Add(kv.Key);
        }

        foreach (ulong id in PruneBuffer)
            Entries.Remove(id);
    }
}
