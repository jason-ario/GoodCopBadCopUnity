/// <summary>
/// Placeholder systemic threat registered on all clients when the Day 3 power-outage
/// phone call is answered. Drives the guidebook task row that directs players to the
/// power station to reset the circuit box.
///
/// The actual circuit-box interaction and resolution logic will be implemented separately.
/// To mark this threat resolved, call <see cref="Resolve"/> from the circuit-box system
/// and pass the same instance that was added to <see cref="TaskRegistry"/>.
/// </summary>
public class RepairPowerThreat : ISystemicThreat
{
    private bool _isResolved;

    public string ThreatName => "Restore Power";

    public string ThreatDescription => _isResolved
        ? "Power restored."
        : "The circuit box at the power station has tripped. Go reset it to restore power.";

    /// <summary>1 while unresolved, 0 once the power has been restored.</summary>
    public float ThreatLevel => _isResolved ? 0f : 1f;

    public float ScoreWeight => 1f;

    // Not driven by the normal night-phase lifecycle — activated immediately on phone answer.
    public void BeginNightPhase() { }
    public void EndNightPhase() { }

    /// <summary>
    /// Marks the threat as resolved and notifies the task registry so the guidebook row refreshes.
    /// Call this from the circuit-box interaction system once the player fixes the power.
    /// </summary>
    public void Resolve()
    {
        if (_isResolved) return;
        _isResolved = true;
        TaskRegistry.Instance?.NotifyTaskStateChanged();
    }
}
