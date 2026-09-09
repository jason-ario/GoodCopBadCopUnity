using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A swappable pool of mutant prefabs eligible to appear in the suspect lineup.
/// Mirrors the SuspectSet pattern used by DailySuspectManager.
/// Assign this to DailySuspectManager.lineupMutants; swap per-day via SetMutantLineupSet().
/// </summary>
[CreateAssetMenu(menuName = "Scriptable Objects/Mutant Lineup Set")]
public class MutantLineupSet : ScriptableObject
{
    [Tooltip("All mutant prefabs that can be randomly selected for a lineup slot.")]
    public List<MutantSuspectBehaviour> mutants = new List<MutantSuspectBehaviour>();

    /// <summary>
    /// Returns a random valid entry from the pool, avoiding <paramref name="excludedPrefab"/>
    /// whenever another valid mutant is available. If the excluded prefab is the only valid
    /// entry, it remains eligible so a single-mutant pool can still spawn.
    /// </summary>
    public MutantSuspectBehaviour GetRandomExcluding(MutantSuspectBehaviour excludedPrefab)
    {
        if (mutants == null || mutants.Count == 0)
        {
            Debug.LogError($"[MutantLineupSet] '{name}' has no mutant prefabs assigned.");
            return null;
        }

        int validCount = 0;
        int nonExcludedCount = 0;
        foreach (MutantSuspectBehaviour mutant in mutants)
        {
            if (mutant == null) continue;

            validCount++;
            if (mutant != excludedPrefab)
                nonExcludedCount++;
        }

        if (validCount == 0)
        {
            Debug.LogError($"[MutantLineupSet] '{name}' has no valid mutant prefabs assigned.");
            return null;
        }

        bool avoidExcludedPrefab = excludedPrefab != null && nonExcludedCount > 0;
        int selectionIndex = Random.Range(0, avoidExcludedPrefab ? nonExcludedCount : validCount);

        foreach (MutantSuspectBehaviour mutant in mutants)
        {
            if (mutant == null || (avoidExcludedPrefab && mutant == excludedPrefab))
                continue;

            if (selectionIndex-- == 0)
                return mutant;
        }

        return null;
    }
}
