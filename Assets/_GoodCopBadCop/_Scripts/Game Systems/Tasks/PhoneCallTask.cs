/// <summary>
/// A runtime task instance created when the player answers a phone call from HQ.
/// Not a MonoBehaviour — instantiated directly by Telephone when a call is answered.
/// Register via GuidebookTaskRegistry.Instance.AddTask() on all clients.
/// </summary>
public class PhoneCallTask : IBetweenShiftTask
{
    private readonly string _taskName;
    private readonly string _taskDescription;
    private readonly int _couponReward;
    private bool _isComplete;

    public string TaskName => _taskName;
    public string TaskDescription => _taskDescription;
    public int CouponReward => _couponReward;
    public bool IsComplete => _isComplete;

    public PhoneCallTask(string taskName, string taskDescription, int couponReward)
    {
        _taskName = taskName;
        _taskDescription = taskDescription;
        _couponReward = couponReward;
    }

    public void ResetTask() => _isComplete = false;

    /// <summary>
    /// Marks this task complete and refreshes the guidebook task list.
    /// </summary>
    public void Complete()
    {
        if (_isComplete) return;
        _isComplete = true;
        GuidebookTaskRegistry.Instance?.NotifyTaskStateChanged();
    }
}
