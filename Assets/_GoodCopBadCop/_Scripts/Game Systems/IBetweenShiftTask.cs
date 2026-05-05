/// <summary>
/// Implemented by every MonoBehaviour that represents a between-shift night-phase task.
/// Register instances on BetweenShiftTaskManager via the Inspector.
/// </summary>
public interface IBetweenShiftTask
{
    /// <summary>Display name used for debugging and future UI.</summary>
    string TaskName { get; }

    /// <summary>True once this task has been completed for the current night phase.</summary>
    bool IsComplete { get; }

    /// <summary>
    /// Resets task state at the start of each night phase.
    /// Always called on the server by BetweenShiftTaskManager.BeginNightPhase().
    /// </summary>
    void ResetTask();
}
