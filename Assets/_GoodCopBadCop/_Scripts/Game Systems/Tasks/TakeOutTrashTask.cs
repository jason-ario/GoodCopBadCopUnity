using UnityEngine;

/// <summary>
/// Controls the "Take Out the Trash" between-shift task.
/// Place this MonoBehaviour on a child of the Task Manager GameObject
/// and assign it to BetweenShiftTaskManager's task list via the Inspector.
/// Call <see cref="Complete"/> from any trigger, interactable, or other script
/// to mark the task done and notify the manager.
/// </summary>
public class TakeOutTrashTask : MonoBehaviour, IBetweenShiftTask
{
    [Header("Task Properties")]
    [SerializeField] private string _taskName        = "Take Out the Trash";
    [SerializeField] private string _taskDescription = "The trash bins are overflowing.\nFind them and take out the trash.";
    [SerializeField] private int    _couponReward     = 10;

    public string TaskName        => _taskName;
    public string TaskDescription => _taskDescription;
    public int    CouponReward    => _couponReward;
    public bool   IsComplete      => _isComplete;

    private bool _isComplete;

    /// <summary>
    /// Marks this task as complete and notifies BetweenShiftTaskManager.
    /// Safe to call from any trigger, interactable, or timeline event.
    /// </summary>
    public void Complete()
    {
        if (_isComplete) return;

        _isComplete = true;
        GuidebookTaskRegistry.Instance.NotifyTaskStateChanged();

        if (BetweenShiftTaskManager.Instance != null)
            BetweenShiftTaskManager.Instance.NotifyTaskComplete(this);
    }

    /// <summary>Resets task state at the start of each night phase.</summary>
    public void ResetTask()
    {
        _isComplete = false;
    }
}
