using System;
using UnityEngine;

/// <summary>
/// Local manager for the shift task list.
/// Tracks task completion and notifies ShiftManager when all tasks are done.
/// Plain MonoBehaviour — no NetworkObject required. Each client manages its own
/// registry; ShiftManager handles the networked all-tasks-complete broadcast.
/// </summary>
public class BetweenShiftTaskManager : MonoBehaviour
{
    public static BetweenShiftTaskManager Instance;

    /// <summary>
    /// Fired locally when every registered task has been completed.
    /// ShiftManager subscribes to this to trigger the shift-start button and announcer line.
    /// </summary>
    public static event Action OnAllTasksComplete;

    /// <summary>Read-only view of all registered tasks.</summary>
    public IBetweenShiftTask[] Tasks => _tasks;

    /// <summary>
    /// Assign all IBetweenShiftTask MonoBehaviours here via the Inspector.
    /// Each entry must implement IBetweenShiftTask.
    /// </summary>
    [SerializeField] private MonoBehaviour[] _taskBehaviours;

    private IBetweenShiftTask[] _tasks;
    private int _completedTaskCount;
    private bool _allTasksComplete;

    public bool AllTasksComplete => _allTasksComplete;

    private void Awake()
    {
        Instance = this;
        BuildTaskList();
    }

    private void BuildTaskList()
    {
        _tasks = new IBetweenShiftTask[_taskBehaviours.Length];
        for (int i = 0; i < _taskBehaviours.Length; i++)
        {
            _tasks[i] = _taskBehaviours[i] as IBetweenShiftTask;
            if (_tasks[i] == null)
                Debug.LogWarning($"[BetweenShiftTaskManager] Entry {i} ({_taskBehaviours[i]?.name}) does not implement IBetweenShiftTask.");
        }
    }

    /// <summary>
    /// Resets all tasks and populates GuidebookTaskRegistry for the new task set.
    /// Call this on every client — typically via ShiftManager.
    /// </summary>
    public void ActivateTasks()
    {
        _completedTaskCount = 0;
        _allTasksComplete = false;

        foreach (var task in _tasks)
            task?.ResetTask();

        if (GuidebookTaskRegistry.Instance != null)
            GuidebookTaskRegistry.Instance.SetTasks(_tasks);

        Debug.Log($"[BetweenShiftTaskManager] Tasks activated. {_tasks.Length} task(s) registered.");
    }

    /// <summary>
    /// Resets task counters and calls <see cref="IBetweenShiftTask.ResetTask"/> on every registered
    /// task without touching <see cref="GuidebookTaskRegistry"/>.
    /// Use this when tasks are delivered via the telephone — the registry is populated separately
    /// by <see cref="Telephone"/> when the player answers the call.
    /// </summary>
    public void ResetTaskPhysics()
    {
        _completedTaskCount = 0;
        _allTasksComplete = false;

        foreach (var task in _tasks)
            task?.ResetTask();

        Debug.Log($"[BetweenShiftTaskManager] Task physics reset. {_tasks.Length} task(s) prepared.");
    }

    /// <summary>
    /// Called by individual task scripts when their task is completed.
    /// Awards the task's coupon reward to the shared money pool (server-only),
    /// then routes the completion count to the server via ShiftManager.
    /// </summary>
    public void NotifyTaskComplete(IBetweenShiftTask task)
    {
        if (task.CouponReward > 0 && GlobalHostVariables.Instance != null)
            GlobalHostVariables.Instance.AddMoney(task.CouponReward);

        ShiftManager.Instance.NotifyTaskCompleteServerRpc();
    }

    /// <summary>
    /// Called by ShiftManager's ClientRpc when the server confirms all tasks are done.
    /// </summary>
    public void HandleAllTasksComplete()
    {
        if (_allTasksComplete) return;
        _allTasksComplete = true;
        OnAllTasksComplete?.Invoke();
    }

    /// <summary>
    /// Debug helper — immediately fires OnAllTasksComplete locally and notifies the server.
    /// </summary>
    public void ForceCompleteAllTasks()
    {
        ShiftManager.Instance.ForceCompleteAllTasksServerRpc();
    }
}
