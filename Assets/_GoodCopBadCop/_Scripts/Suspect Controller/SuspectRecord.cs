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
    /// Population deaths use the design-facing mutant threshold, not the full
    /// transformation threshold. A suspect becomes eligible if they are passed
    /// while their persisted mutation score is above 80 or they visibly present
    /// more than 10 active anomalies.
    /// </summary>
    public bool IsPopulationMutantByScore => infectionScore > 80;

    /// <summary>
    /// When true, the next AdvanceDayInfection resets infectionScore to the suspect's base level
    /// (applied after a Quarantine verdict). Has no effect if IsFullyMutated is true.
    /// </summary>
    public bool pendingVaccineReset;

    /// <summary>When true the suspect was killed and will no longer appear in future shifts.</summary>
    public bool isKilled;

    /// <summary>
    /// True when this suspect has escaped a full-mutant booth encounter alive — beaten (health
    /// depleted by non-fire damage while <see cref="MutantEnemy"/>'s fleeInsteadOfDie is active)
    /// and fled into the woods instead of dying. While true this suspect is a candidate for
    /// <see cref="MutantSpawner"/>'s legacy-mutant pool, allowing them to re-appear as a roaming
    /// full mutant. Cleared (and <see cref="isKilled"/> set) if they are ever permanently killed
    /// with fire. Set via <see cref="SuspectRunRecords.MarkAsLegacyMutant"/> /
    /// <see cref="SuspectRunRecords.ClearLegacyMutant"/>.
    /// </summary>
    public bool isLegacyMutant;

    /// <summary>
    /// True once this suspect has been passed through the gate into the city.
    /// This is persisted as historical state; population deaths are driven by
    /// <see cref="populationKillPending"/> so a passed mutant only gets one night.
    /// </summary>
    public bool hasEnteredCity;

    /// <summary>
    /// True when this suspect was passed into the city while qualifying as a
    /// population-killing mutant. The population simulation consumes this once
    /// on the next night and then clears it.
    /// </summary>
    public bool populationKillPending;

    /// <summary>
    /// True once this suspect's death has already been counted by the population system.
    /// Prevents repeated contactable-population death counts if save/load or duplicate
    /// verdict paths touch the same record again.
    /// </summary>
    public bool populationDeathRecorded;

    /// <summary>
    /// The 1-based campaign day on which this suspect was killed. -1 means never killed.
    /// Used to calculate when the replacement version of this suspect should activate.
    /// </summary>
    public int killedOnDay = -1;

    /// <summary>
    /// When true, this suspect has been "replaced" — they were killed, enough time has passed,
    /// and an uncanny replacement version of them should now re-enter the shift pool.
    /// The replacement is spawned via DoppelgangerData (InitializeAsDoppelganger) using the
    /// replacementConfig defined on the suspect's SuspectData asset.
    /// </summary>
    public bool isReplacement;

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
        if (SuspectRunRecords.Instance != null)
        {
            infectionScore = (int)UnityEngine.Random.Range(
                SuspectRunRecords.Instance.startingInfectionScore.x,
                SuspectRunRecords.Instance.startingInfectionScore.y);
        }
        else
        {
            infectionScore = suspectData != null ? suspectData.startingInfectionScore : 0;
        }
    }
}
