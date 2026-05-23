using UnityEngine;

/// <summary>
/// STUB: Represents surviving the night mutant attack wave.
/// Currently auto-completes immediately so it does not block the task list.
/// Replace the ResetTask body with a real wave-manager hook when enemy AI is built.
/// </summary>
public class MutantNightAttackTask : MonoBehaviour, IBetweenShiftTask
{
    private bool _isComplete;

    public string TaskName        => "Survive the Night";
    public string TaskDescription => "Hold your ground until dawn.\nDon't let the mutants break through.";
    public int    XpReward        => 100;
    public bool   IsComplete      => _isComplete;

    /// <summary>
    /// Stub: marks itself complete immediately, then notifies the manager.
    /// Future: set _isComplete = false here and let the wave manager call NotifyTaskComplete.
    /// </summary>
    public void ResetTask()
    {
        _isComplete = true;

        if (BetweenShiftTaskManager.Instance != null)
            BetweenShiftTaskManager.Instance.NotifyTaskComplete(this);
    }
}
