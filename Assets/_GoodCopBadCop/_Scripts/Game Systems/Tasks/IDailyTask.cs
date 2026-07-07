using System;

/// <summary>
/// Implemented by any MonoBehaviour/NetworkBehaviour that can be randomly scheduled
/// as a daily task by <see cref="DailyTaskScheduler"/>.
///
/// At the start of each campaign day the scheduler selects one unlocked task from its
/// pool and calls <see cref="TriggerDailyTask"/>. When the task finishes it fires
/// <see cref="OnDailyTaskCompleted"/> so the scheduler can automatically unlock it for
/// future days (when the entry's <c>UnlockOnFirstCompletion</c> flag is set).
/// </summary>
public interface IDailyTask
{
    /// <summary>
    /// Stable string identifier. Must match the <c>TaskId</c> field on the
    /// corresponding <see cref="DailyTaskEntry"/> in the scheduler's Inspector list,
    /// and is also used as the persistence key in <see cref="SaveDataManager"/>.
    /// </summary>
    string DailyTaskId { get; }

    /// <summary>
    /// Activates this task for the current day. Server-only.
    /// Implementations must guard internally with <c>if (!IsServer) return;</c>.
    /// </summary>
    void TriggerDailyTask();

    /// <summary>
    /// Fired on the server when this task has been fully completed for the current day.
    /// <see cref="DailyTaskScheduler"/> subscribes to this event to automatically unlock
    /// the task for future days when <c>UnlockOnFirstCompletion</c> is enabled.
    /// </summary>
    event Action OnDailyTaskCompleted;
}
