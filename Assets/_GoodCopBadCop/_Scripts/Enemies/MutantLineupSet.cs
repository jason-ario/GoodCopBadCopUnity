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

    /// <summary>Returns a random entry from the pool. Logs an error and returns null if the list is empty.</summary>
    public MutantSuspectBehaviour GetRandom()
    {
        if (mutants == null || mutants.Count == 0)
        {
            Debug.LogError($"[MutantLineupSet] '{name}' has no mutant prefabs assigned.");
            return null;
        }

        return mutants[Random.Range(0, mutants.Count)];
    }
}
