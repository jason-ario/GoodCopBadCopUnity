/// <summary>
/// Shared rule for Day 1 tutorial gates (ink-stamp slots, stack of folders): they may only be
/// locked while Day 1 is the current campaign day. From Day 2 onward they are always usable,
/// regardless of entry path (normal advance, resumed save, Restart Day, debug skip).
/// </summary>
public static class TutorialGateRules
{
    /// <summary>The server's current campaign day, or 0 if the campaign has not started.</summary>
    public static int CurrentDay => CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : 0;

    /// <summary>True once the campaign is on Day 2 or later.</summary>
    public static bool IsPastDay1 => CurrentDay > 1;
}
