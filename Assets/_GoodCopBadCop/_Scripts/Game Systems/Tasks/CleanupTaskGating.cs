/// <summary>
/// Shared gating rule for the checkpoint-maintenance tasks: broken perimeter fences
/// (<see cref="FenceRepairTask"/>), uncollected trash/gore (<see cref="TakeOutTrashTask"/>), and
/// unscrubbed graffiti (<see cref="CleanGraffitiTask"/>).
///
/// Day 1 is a hard tutorial requirement: these tasks still block clock-out and still get a row
/// in the HUD task list, exactly as before.
///
/// Every day after that, the same three tasks keep spawning, keep showing their compass pips,
/// and keep feeding <see cref="CheckpointIntegrityService"/>'s payout multiplier — but they
/// become optional. They no longer register as a <see cref="ShiftManager.RegisterPendingDailyTask"/>
/// clock-out blocker and no longer get a row in <see cref="HUDTaskList"/>'s task list. Players can
/// clock out with fences broken, trash on the ground, or graffiti still up; it just costs them
/// Checkpoint Integrity.
/// </summary>
public static class CleanupTaskGating
{
    /// <summary>
    /// True while the current day still treats fence/trash/graffiti cleanup as a mandatory,
    /// clock-out-blocking task (Day 1 only). Defaults to true if <see cref="CampaignManager"/>
    /// hasn't reported a day yet, matching its own Day 1 default.
    /// </summary>
    public static bool IsMandatoryDay =>
        CampaignManager.Instance == null || CampaignManager.Instance.CurrentDay <= 1;
}
