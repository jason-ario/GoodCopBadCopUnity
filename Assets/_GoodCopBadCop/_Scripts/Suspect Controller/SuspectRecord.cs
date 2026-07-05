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

    /// <summary>
    /// The 1-based campaign day on which this suspect was most recently quarantined.
    /// -1 means never quarantined this session.
    /// DailySuspectManager uses this to skip the suspect on the shift immediately following
    /// their quarantine: a suspect quarantined on day N is excluded from day N+1 only,
    /// then re-enters the rotation normally from day N+2 onward.
    /// </summary>
    public int quarantinedOnDay = -1;

    /// <summary>
    /// Returns true if this suspect is currently serving a one-day quarantine cooldown
    /// and should be excluded from the given day's shift pool.
    /// </summary>
    /// <param name="currentDay">The campaign day being populated (1-based).</param>
    public bool IsOnQuarantineCooldown(int currentDay)
        => quarantinedOnDay >= 0 && quarantinedOnDay == currentDay - 1;

    public SuspectRecord(SuspectData suspectData)
    {
        SuspectData = suspectData;
        infectionScore = (int)UnityEngine.Random.Range(
            SuspectRunRecords.Instance.startingInfectionScore.x,
            SuspectRunRecords.Instance.startingInfectionScore.y);
    }
}
