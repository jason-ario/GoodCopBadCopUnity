/// <summary>
/// Systemic threat registered on all clients when the Day 3 power-outage phone call is
/// answered. Drives the guidebook task row that walks the player through the power
/// station's fuse-box puzzle, one step at a time — see <see cref="Step"/>.
///
/// <see cref="Day_03"/> advances the step (via <see cref="SetStep"/>) as the player
/// progresses: opening the fuse box for the first time, filling every fuse slot, then
/// flipping the power switch. To mark this threat resolved, call <see cref="Resolve"/>
/// from the same place — <see cref="Day_03.OnPowerOutageResolved"/> — once power actually
/// comes back on.
/// </summary>
public class RepairPowerThreat : ISystemicThreat
{
    /// <summary>Step of the fuse-box repair sequence currently shown to the player.</summary>
    public enum Step
    {
        /// <summary>Direct the player to find and open the power station's fuse box.</summary>
        InvestigateFuseBox,

        /// <summary>Fuse box is open — direct the player to fill every empty slot.</summary>
        InsertFuses,

        /// <summary>Every slot is filled — direct the player to flip the power switch.</summary>
        PullLever,
    }

    private bool _isResolved;
    private Step _step = Step.InvestigateFuseBox;

    public string ThreatName => "Restore Power";

    public string ThreatDescription
    {
        get
        {
            if (_isResolved) return "Power restored.";

            return _step switch
            {
                Step.InvestigateFuseBox => "Investigate the fuse box in the power station.",
                Step.InsertFuses        => "Input the missing fuses into the fuse box.",
                Step.PullLever          => "Pull the lever to reactivate the power.",
                _                       => "The circuit box at the power station has tripped. Go reset it to restore power.",
            };
        }
    }

    /// <summary>1 while unresolved, 0 once the power has been restored.</summary>
    public float ThreatLevel => _isResolved ? 0f : 1f;

    public float ScoreWeight => 1f;

    // Not driven by the normal night-phase lifecycle — activated immediately on phone answer.
    public void BeginNightPhase() { }
    public void EndNightPhase() { }

    /// <summary>
    /// Advances the guidebook row to a new step and immediately refreshes its displayed text.
    /// No-op once resolved, and no-op if already on the requested step. Called by
    /// <see cref="Day_03"/> as the player progresses through the fuse-box puzzle.
    /// </summary>
    public void SetStep(Step step)
    {
        if (_isResolved) return;
        if (_step == step) return;
        _step = step;
        TaskRegistry.Instance?.NotifyTaskStateChanged();
    }

    /// <summary>
    /// Marks the threat as resolved and notifies the task registry so the guidebook row refreshes.
    /// Call this once the fuse box is fixed and power has actually been restored.
    /// </summary>
    public void Resolve()
    {
        if (_isResolved) return;
        _isResolved = true;
        TaskRegistry.Instance?.NotifyTaskStateChanged();
    }
}
