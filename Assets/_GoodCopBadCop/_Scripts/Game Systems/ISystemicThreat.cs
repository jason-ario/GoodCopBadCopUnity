/// <summary>
/// Implemented by every NetworkBehaviour that represents an ongoing, systemic night-phase threat.
/// Threats never "complete" — they accumulate pressure that players manage continuously.
/// Register instances on BetweenShiftTaskManager via the Inspector.
/// </summary>
public interface ISystemicThreat
{
    /// <summary>Display name shown in the guidebook threat list.</summary>
    string ThreatName { get; }

    /// <summary>Dynamic description that reflects current pressure (e.g. "Active mutants: 3/10").</summary>
    string ThreatDescription { get; }

    /// <summary>
    /// Normalised threat pressure: 0 = no pressure, 1 = maximum pressure.
    /// Each implementation stores this in a NetworkVariable so all clients see the same value.
    /// </summary>
    float ThreatLevel { get; }

    /// <summary>
    /// Relative weight of this threat when computing the end-of-shift performance score.
    /// Higher weight = larger contribution to the final bonus.
    /// </summary>
    float ScoreWeight { get; }

    /// <summary>
    /// Activates this threat for the current night phase. Server only.
    /// Spawn loops, sync coroutines, and other server-driven behaviour should start here.
    /// </summary>
    void BeginNightPhase();

    /// <summary>
    /// Deactivates spawn loops and stops server-driven behaviour for this threat. Server only.
    /// Existing threat objects (graffiti, trash bags, fence damage) may intentionally persist
    /// as day-shift consequences.
    /// </summary>
    void EndNightPhase();
}
