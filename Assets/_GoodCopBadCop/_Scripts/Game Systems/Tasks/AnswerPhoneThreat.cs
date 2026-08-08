/// <summary>
/// Placeholder systemic threat registered on all clients the moment HQ starts calling the booth
/// phone (e.g. the Day 3 power-outage call). Drives the guidebook/HUD task row prompting the
/// player to pick up the ringing phone. Removed once the call is answered — see the caller
/// (e.g. <see cref="Day_03"/>), which subscribes to <see cref="Telephone.OnScriptedCallAnsweredAllClients"/>
/// and calls <see cref="TaskRegistry.RemoveThreat"/> for the same instance that was added.
/// </summary>
public class AnswerPhoneThreat : ISystemicThreat
{
    private readonly string _threatName;
    private readonly string _threatDescription;

    public AnswerPhoneThreat(
        string threatName = "Answer the Phone",
        string threatDescription = "The booth phone is ringing — HQ is calling. Pick it up.")
    {
        _threatName = threatName;
        _threatDescription = threatDescription;
    }

    public string ThreatName => _threatName;
    public string ThreatDescription => _threatDescription;

    /// <summary>Always at maximum pressure while ringing — removed outright once answered.</summary>
    public float ThreatLevel => 1f;

    public float ScoreWeight => 0f;

    // Not driven by the normal night-phase lifecycle — added/removed directly around the call.
    public void BeginNightPhase() { }
    public void EndNightPhase() { }
}
