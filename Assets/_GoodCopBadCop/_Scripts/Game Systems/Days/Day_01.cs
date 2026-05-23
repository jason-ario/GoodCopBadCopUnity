/// <summary>
/// Day 1 — the tutorial shift.
/// The exit door is locked and the welcome bark sequence plays.
/// Add Day 1-specific gameplay logic by overriding the lifecycle methods below.
/// </summary>
public class Day_01 : DayBase
{
    public override void DayActivated()
    {
        base.DayActivated();
        // Day 1-specific setup runs here.
        // MegaphoneDialogueManager handles the welcome bark via OnShiftStart subscription.
    }

    public override void ShiftEnded()
    {
        base.ShiftEnded();
    }

    public override void NightPhaseStarted()
    {
        base.NightPhaseStarted();
    }

    public override void DayCompleted()
    {
        base.DayCompleted();
    }

    public override void DayDeactivated()
    {
        base.DayDeactivated();
    }
}
