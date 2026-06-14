/// <summary>
/// Implemented by every MonoBehaviour that represents a between-shift night-phase task.
/// Register instances on BetweenShiftTaskManager via the Inspector.
/// </summary>
[System.Obsolete("Use ISystemicThreat instead. The between-shift task system has been replaced by the systemic threat model.")]
public interface IBetweenShiftTask
{
    /// <summary>Display name shown in the guidebook task list.</summary>
    string TaskName { get; }

    /// <summary>Short description of what the player must do to complete this task.</summary>
    string TaskDescription { get; }

    /// <summary>Money awarded upon completion. Shown in the guidebook task list.</summary>
    int CouponReward { get; }

    /// <summary>True once this task has been completed for the current night phase.</summary>
    bool IsComplete { get; }

    /// <summary>
    /// Resets task state at the start of each night phase.
    /// Always called on the server by BetweenShiftTaskManager.BeginNightPhase().
    /// </summary>
    void ResetTask();
}
