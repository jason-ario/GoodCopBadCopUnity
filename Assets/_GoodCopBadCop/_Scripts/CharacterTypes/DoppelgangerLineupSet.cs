using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A swappable pool of DoppelgangerData assets eligible to appear in the suspect lineup.
/// Mirrors the MutantLineupSet pattern. Assign to DailySuspectManager and swap per-day
/// via SetDoppelgangerLineupSet(). Set spawnChance to 0 for Days 1–5 to suppress spawning.
/// </summary>
[CreateAssetMenu(menuName = "Scriptable Objects/Doppelganger Lineup Set")]
public class DoppelgangerLineupSet : ScriptableObject
{
    [Tooltip("Probability (0–1) that at least one doppelganger is injected into this day's lineup.")]
    [Range(0f, 1f)] public float spawnChance = 0.1f;

    [Tooltip("All DoppelgangerData assets eligible for random selection this day.")]
    public List<DoppelgangerData> doppelgangers = new List<DoppelgangerData>();

    /// <summary>
    /// Returns a random entry from the pool.
    /// Logs an error and returns null if the list is empty.
    /// </summary>
    public DoppelgangerData GetRandom()
    {
        if (doppelgangers == null || doppelgangers.Count == 0)
        {
            Debug.LogError($"[DoppelgangerLineupSet] '{name}' has no DoppelgangerData assigned.");
            return null;
        }

        return doppelgangers[Random.Range(0, doppelgangers.Count)];
    }
}
