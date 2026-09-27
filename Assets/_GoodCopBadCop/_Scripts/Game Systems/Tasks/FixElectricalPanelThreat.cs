/// <summary>
/// Guidebook task shown while a booth power outage must be fixed at the in-booth
/// <see cref="ElectricPanelController"/> (flip every breaker on, then turn the knob) — e.g. the
/// Day 2 Ocho encounter (<see cref="OchoBoothEncounter"/>). Deliberately separate from
/// <see cref="RepairPowerThreat"/>, which is the Day 3 power-station fuse-box task.
/// Registered locally on every client; call <see cref="Resolve"/> once power is back on.
/// </summary>
public class FixElectricalPanelThreat : ISystemicThreat
{
    private bool _isResolved;

    public string ThreatName => "Fix the Power";

    public string ThreatDescription => _isResolved
        ? "Power restored."
        : "Fix the power at the electrical panel: flip every breaker on, then turn the knob.";

    /// <summary>1 while unresolved, 0 once the power has been restored.</summary>
    public float ThreatLevel => _isResolved ? 0f : 1f;

    public float ScoreWeight => 1f;

    // Not driven by the normal night-phase lifecycle — activated the moment the outage starts.
    public void BeginNightPhase() { }
    public void EndNightPhase() { }

    /// <summary>Marks the task resolved and refreshes the guidebook row.</summary>
    public void Resolve()
    {
        if (_isResolved) return;
        _isResolved = true;
        TaskRegistry.Instance?.NotifyTaskStateChanged();
    }
}
