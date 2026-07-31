using System;

/// <summary>
/// Minimal <see cref="IDailyTask"/> adapter with no gameplay of its own — it exists purely to
/// block <see cref="ShiftManager.TryEnableClockOut"/> (and therefore the timecard machine
/// physically unlocking) until a day script's own scripted sequence has fully resolved.
///
/// Registered by <see cref="Day_01"/> at <c>DayActivated</c> so the timecard machine cannot be
/// unlocked until the first mutant breach — and any resulting fence-repair follow-up — has
/// completed, even though the trash/graffiti tasks (the only other pending daily tasks that day)
/// may finish well before then. Call <see cref="Complete"/> once the gated sequence is done.
/// </summary>
public class MutantBreachGateTask : IDailyTask
{
    public string DailyTaskId => "Day1_MutantBreachGate";

    /// <summary>No-op — this task is never triggered by <see cref="DailyTaskScheduler"/>, only
    /// registered/completed manually by the owning day script.</summary>
    public void TriggerDailyTask() { }

    public event Action OnDailyTaskCompleted;

    /// <summary>Resolves the gate, letting ShiftManager proceed with clock-out once every other pending task is also done. Safe to call more than once.</summary>
    public void Complete() => OnDailyTaskCompleted?.Invoke();
}
