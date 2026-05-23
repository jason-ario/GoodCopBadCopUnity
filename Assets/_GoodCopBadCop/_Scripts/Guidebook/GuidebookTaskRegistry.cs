using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central registry for all tasks shown in the guidebook task list.
/// Tasks can be added or removed at any time by any system — no dependency
/// on BetweenShiftTaskManager or network state required.
/// Self-instantiates on first access so no manual scene placement is needed.
/// </summary>
public class GuidebookTaskRegistry : MonoBehaviour
{
    public static GuidebookTaskRegistry Instance => GetOrCreate();

    /// <summary>Fired whenever the task list changes (task added, removed, replaced, or cleared).</summary>
    public static event Action OnTaskListChanged;

    /// <summary>
    /// Fired only when one or more tasks are added to the registry.
    /// GuidebookIcon subscribes to this to show the notification badge.
    /// </summary>
    public static event Action OnTasksAdded;

    /// <summary>
    /// Fired when a task's completion state changes without the list itself changing.
    /// GuidebookTaskRow subscribes to refresh its checkmark without rebuilding rows.
    /// </summary>
    public static event Action OnTaskStateChanged;

    private readonly List<IBetweenShiftTask> _tasks = new();

    /// <summary>Read-only snapshot of the current task list.</summary>
    public IReadOnlyList<IBetweenShiftTask> Tasks => _tasks;

    private static GuidebookTaskRegistry _instance;
    private bool _subscribedToShiftManager;

    private static GuidebookTaskRegistry GetOrCreate()
    {
        if (_instance != null) return _instance;

        _instance = FindFirstObjectByType<GuidebookTaskRegistry>();
        if (_instance != null) return _instance;

        var go = new GameObject("[GuidebookTaskRegistry]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<GuidebookTaskRegistry>();
        return _instance;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        if (_subscribedToShiftManager) return;
        if (ShiftManager.Instance == null) return;

        ShiftManager.Instance.OnDayStart += ClearTasks;
        _subscribedToShiftManager = true;
        Debug.Log("[GuidebookTaskRegistry] Subscribed to ShiftManager.OnDayStart.");
    }

    private void OnDestroy()
    {
        if (_subscribedToShiftManager && ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= ClearTasks;
    }

    /// <summary>Adds a task to the registry and fires <see cref="OnTaskListChanged"/> and <see cref="OnTasksAdded"/>.</summary>
    public void AddTask(IBetweenShiftTask task)
    {
        if (task == null || _tasks.Contains(task)) return;
        _tasks.Add(task);
        OnTaskListChanged?.Invoke();
        OnTasksAdded?.Invoke();
        Debug.Log($"[GuidebookTaskRegistry] Task added: '{task.TaskName}'. Total: {_tasks.Count}");
    }

    /// <summary>Removes a task from the registry and fires <see cref="OnTaskListChanged"/>.</summary>
    public void RemoveTask(IBetweenShiftTask task)
    {
        if (task == null || !_tasks.Contains(task)) return;
        _tasks.Remove(task);
        OnTaskListChanged?.Invoke();
        Debug.Log($"[GuidebookTaskRegistry] Task removed: '{task.TaskName}'. Total: {_tasks.Count}");
    }

    /// <summary>
    /// Replaces the entire task list and fires <see cref="OnTaskListChanged"/>.
    /// Also fires <see cref="OnTasksAdded"/> if the new list is non-empty.
    /// Null entries in the source are silently skipped.
    /// </summary>
    public void SetTasks(IEnumerable<IBetweenShiftTask> tasks)
    {
        _tasks.Clear();
        if (tasks != null)
        {
            foreach (var t in tasks)
                if (t != null) _tasks.Add(t);
        }

        OnTaskListChanged?.Invoke();
        if (_tasks.Count > 0)
            OnTasksAdded?.Invoke();
        Debug.Log($"[GuidebookTaskRegistry] Task list set. Total: {_tasks.Count}");
    }

    /// <summary>Clears all tasks and fires <see cref="OnTaskListChanged"/>.</summary>
    public void ClearTasks()
    {
        _tasks.Clear();
        OnTaskListChanged?.Invoke();
        Debug.Log("[GuidebookTaskRegistry] Task list cleared.");
    }

    /// <summary>
    /// Call this when a task's IsComplete state changes so the guidebook can
    /// refresh checkmarks without rebuilding the row list.
    /// </summary>
    public void NotifyTaskStateChanged()
    {
        Debug.Log($"[GuidebookTaskRegistry] NotifyTaskStateChanged fired. Subscriber count: {OnTaskStateChanged?.GetInvocationList().Length ?? 0}");
        OnTaskStateChanged?.Invoke();
    }
}
