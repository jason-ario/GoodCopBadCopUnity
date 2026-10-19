using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-only pool of recurring world NPCs who have no <see cref="SuspectData"/> (so they're
/// outside <see cref="SuspectRunRecords"/>' legacy-mutant pool) but turn into full mutants during
/// the story, e.g. Givi the postman on Day 4. After an NPC has turned, <see cref="MutantSpawner"/>
/// can spawn them in their full-mutant form, the same way it spawns escaped residents.
///
/// State:
///   - Turned: true once <see cref="MarkTurned"/> runs this session, or (after a reload) the
///     current day is later than the entry's turn day.
///   - Killed: persistent world-object flag "NpcMutantKilled_{id}", set when an instance dies to
///     fire (<see cref="MutantEnemy.DiedPermanently"/>). A killed NPC never spawns again.
///   - Active: at most one live instance per NPC (the original or a spawner copy).
///
/// Entries are registered by the NPC's own scene controller (e.g.
/// <c>GoodCopBadCop.Postman.PostmanVisitController</c>). They are static so the entry survives
/// that controller being destroyed when its mutant despawns.
/// </summary>
public static class NpcMutantPool
{
    private class Entry
    {
        public string Id;
        public GameObject Prefab;
        public int TurnDay;
        public bool TurnedThisSession;
        public NetworkObject ActiveInstance;
    }

    private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Entries.Clear();

    private static string KilledKey(string id) => $"NpcMutantKilled_{id}";

    private static int CurrentDay => ShiftManager.Instance != null ? ShiftManager.Instance.CurrentDay : 1;

    /// <summary>
    /// Server-only. Adds or refreshes an NPC. <paramref name="prefab"/> is the networked character
    /// prefab spawned at mutant spawners (must contain SuspectCharacter + MutantEnemy and be in the
    /// NetworkPrefabs list). <paramref name="turnDay"/> is the day the NPC turns. Re-registering
    /// after a scene reload resets the session's turned/active state.
    /// </summary>
    public static void Register(string id, GameObject prefab, int turnDay)
    {
        if (string.IsNullOrEmpty(id)) return;
        Entries[id] = new Entry { Id = id, Prefab = prefab, TurnDay = turnDay };
    }

    /// <summary>True when the NPC has turned (this session, or on an earlier day).</summary>
    public static bool HasTurned(string id)
    {
        if (!Entries.TryGetValue(id, out Entry entry)) return false;
        return HasTurned(entry);
    }

    private static bool HasTurned(Entry entry) => entry.TurnedThisSession || CurrentDay > entry.TurnDay;

    /// <summary>True when the NPC's mutant has been permanently killed with fire on this save.</summary>
    public static bool IsKilled(string id) =>
        SaveDataManager.Instance != null && SaveDataManager.Instance.IsWorldObjectUnlocked(KilledKey(id));

    /// <summary>Server-only. Records that the NPC has turned into a full mutant.</summary>
    public static void MarkTurned(string id)
    {
        if (Entries.TryGetValue(id, out Entry entry))
            entry.TurnedThisSession = true;
    }

    /// <summary>
    /// Server-only. Tracks a live full-mutant instance of this NPC so no second copy spawns while it
    /// lives, and records a permanent kill when it dies to fire.
    /// </summary>
    public static void TrackInstance(string id, SuspectCharacter character)
    {
        if (character == null || !Entries.TryGetValue(id, out Entry entry)) return;

        entry.ActiveInstance = character.NetworkObject;

        MutantEnemy enemy = character.GetComponent<MutantEnemy>();
        if (enemy == null) return;

        enemy.OnRemovedFromPlay += () =>
        {
            if (entry.ActiveInstance == character.NetworkObject)
                entry.ActiveInstance = null;

            if (enemy.DiedPermanently && SaveDataManager.Instance != null)
            {
                SaveDataManager.Instance.UnlockWorldObject(KilledKey(id));
                Debug.Log($"[NpcMutantPool] '{id}' was destroyed by fire and won't spawn again.");
            }
        };
    }

    private static bool IsEligible(Entry entry)
    {
        if (entry.Prefab == null || !HasTurned(entry) || IsKilled(entry.Id)) return false;

        // A day-transition despawn drops MutantEnemy's listeners without invoking them, so
        // also treat a despawned/destroyed tracked instance as gone.
        if (entry.ActiveInstance != null && entry.ActiveInstance.IsSpawned) return false;
        entry.ActiveInstance = null;
        return true;
    }

    /// <summary>Server-only. Number of NPCs currently eligible for a world spawn.</summary>
    public static int EligibleCount
    {
        get
        {
            int count = 0;
            foreach (Entry entry in Entries.Values)
                if (IsEligible(entry)) count++;
            return count;
        }
    }

    /// <summary>
    /// Server-only. Picks a random eligible NPC. Returns false when none are eligible.
    /// </summary>
    public static bool TryGetRandomEligible(out string id, out GameObject prefab)
    {
        id = null;
        prefab = null;

        var candidates = new List<Entry>();
        foreach (Entry entry in Entries.Values)
            if (IsEligible(entry)) candidates.Add(entry);

        if (candidates.Count == 0) return false;

        Entry chosen = candidates[Random.Range(0, candidates.Count)];
        id = chosen.Id;
        prefab = chosen.Prefab;
        return true;
    }
}
