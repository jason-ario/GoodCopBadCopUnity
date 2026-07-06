/// <summary>
/// HUD task shown after the shift ends on days that run the post-shift Vlad Out-Back sequence.
/// Registered by the day script when the shift ends; removed when the player approaches Vlad
/// and the sequence advances past the intro meeting.
/// Mirrors the lightweight <see cref="GoToBunkerTask"/> pattern — no NetworkBehaviour needed.
/// </summary>
public class MeetVladOutBackTask : ISystemicThreat
{
    private const string TaskDisplayName = "Meet Vlad out back";
    private const string TaskDisplayDesc = "He's waiting by the bunker";

    /// <summary>The currently active instance, or null if none exists.</summary>
    public static MeetVladOutBackTask Current { get; private set; }

    public string ThreatName        => TaskDisplayName;
    public string ThreatDescription => TaskDisplayDesc;
    public float  ThreatLevel       => 0f;
    public float  ScoreWeight       => 0f;

    /// <summary>
    /// Creates a new instance, registers it with <see cref="TaskRegistry"/>, and returns it.
    /// If an instance is already active, returns it without creating a duplicate.
    /// </summary>
    public static MeetVladOutBackTask CreateAndRegister()
    {
        if (Current != null) return Current;

        Current = new MeetVladOutBackTask();
        TaskRegistry.Instance.AddThreat(Current);
        return Current;
    }

    /// <summary>
    /// Removes the active instance from <see cref="TaskRegistry"/> and clears <see cref="Current"/>.
    /// Safe to call when no instance is active.
    /// </summary>
    public static void CompleteAndRemove()
    {
        if (Current == null) return;

        TaskRegistry.Instance.RemoveThreat(Current);
        Current = null;
    }

    // ISystemicThreat — no server-driven behaviour needed.
    public void BeginNightPhase() { }
    public void EndNightPhase()   { }
}
