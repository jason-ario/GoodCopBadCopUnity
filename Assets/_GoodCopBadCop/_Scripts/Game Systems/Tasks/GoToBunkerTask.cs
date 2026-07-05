/// <summary>
/// A one-off end-of-day directive shown on the HUD after the player clocks out.
/// Registered with <see cref="TaskRegistry"/> when <see cref="ShiftManager.OnShiftEnd"/> fires
/// and removed when the player confirms sleeping in <see cref="BunkBedInteractable"/>.
/// Carries no score weight and no threat pressure — it exists only as a navigation cue.
/// </summary>
public class GoToBunkerTask : ISystemicThreat
{
    private const string TaskDisplayName = "End the day";
    private const string TaskDisplayDesc = "Go to the bunker and sleep";

    /// <summary>The currently active instance, or null if none exists.</summary>
    public static GoToBunkerTask Current { get; private set; }

    public string ThreatName        => TaskDisplayName;
    public string ThreatDescription => TaskDisplayDesc;
    public float  ThreatLevel       => 0f;
    public float  ScoreWeight       => 0f;

    /// <summary>
    /// Creates a new instance, registers it with the <see cref="TaskRegistry"/>, and returns it.
    /// If an instance is already active, returns it without creating a duplicate.
    /// </summary>
    public static GoToBunkerTask CreateAndRegister()
    {
        if (Current != null) return Current;

        Current = new GoToBunkerTask();
        TaskRegistry.Instance.AddThreat(Current);
        return Current;
    }

    /// <summary>
    /// Removes the active instance from the <see cref="TaskRegistry"/> and clears <see cref="Current"/>.
    /// Safe to call when no instance is active.
    /// </summary>
    public static void CompleteAndRemove()
    {
        if (Current == null) return;

        TaskRegistry.Instance.RemoveThreat(Current);
        Current = null;
    }

    // ISystemicThreat — no server-driven behaviour needed for this task.
    public void BeginNightPhase() { }
    public void EndNightPhase()   { }
}
