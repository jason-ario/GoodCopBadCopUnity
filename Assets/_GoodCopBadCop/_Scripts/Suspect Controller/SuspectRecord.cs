using System;
using UnityEngine;

[Serializable]
public class SuspectRecord
{
    public SuspectData SuspectData;
    public int daysShown = 0;
    public int lastDayShown = 0;
    public int infectionScore = 0;

    /// <summary>
    /// True when the suspect's infection score has reached or exceeded the fully-mutated
    /// threshold. Used by AdvanceDayInfection to decide whether quarantine has any effect.
    /// For the observable in-game signal (all categories active) use
    /// <see cref="AnomalyController.IsFullyMutated"/> on the spawned character instead.
    /// </summary>
    public bool IsFullyMutated => infectionScore >= AnomalyController.FULLY_MUTATED_THRESHOLD;

    /// <summary>
    /// When true, the next AdvanceDayInfection resets infectionScore to the suspect's base level
    /// (applied after a Quarantine verdict). Has no effect if IsFullyMutated is true.
    /// </summary>
    public bool pendingVaccineReset;

    /// <summary>When true the suspect was killed and will no longer appear in future shifts.</summary>
    public bool isKilled;

    public SuspectRecord(SuspectData suspectData)
    {
        SuspectData = suspectData;
        infectionScore = (int)UnityEngine.Random.Range(
            SuspectRunRecords.Instance.startingInfectionScore.x,
            SuspectRunRecords.Instance.startingInfectionScore.y);
    }
}
