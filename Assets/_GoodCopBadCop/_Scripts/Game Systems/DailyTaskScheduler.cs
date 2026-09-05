using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Describes a single entry in the <see cref="DailyTaskScheduler"/> pool.
/// </summary>
[Serializable]
public class DailyTaskEntry
{
    [Tooltip("Stable identifier matching IDailyTask.DailyTaskId. Used as the save-data persistence key.")]
    public string TaskId;

    [Tooltip("The MonoBehaviour that implements IDailyTask. Must be assigned.")]
    public MonoBehaviour TaskComponent;

    [Tooltip("If true, this task is available from the very first day without requiring an in-game unlock.")]
    public bool IsUnlockedByDefault;

    [Tooltip("If true, DailyTaskScheduler automatically unlocks this task for future days the first time it is completed.")]
    public bool UnlockOnFirstCompletion;
}

/// <summary>
/// Server-authoritative scheduler that randomly selects one unlocked daily task at the
/// start of each campaign day and triggers it.
///
/// Task unlock state is persisted to the active save slot via <see cref="SaveDataManager"/>.
///
/// Setup:
///   1. Add one <see cref="DailyTaskEntry"/> per task in the Inspector.
///   2. Set <c>TaskId</c> to match <see cref="IDailyTask.DailyTaskId"/> on the component.
///   3. Assign the <c>MonoBehaviour</c> that implements <see cref="IDailyTask"/>.
///   4. Toggle <c>IsUnlockedByDefault</c> for tasks available from the start.
///   5. Toggle <c>UnlockOnFirstCompletion</c> to auto-unlock a task after its first completion.
///
/// Day 1 example:
///   - Pool has TakeOutTrash (not default-unlocked, <c>UnlockOnFirstCompletion = true</c>).
///   - On Day 1, AlexeiController triggers trash via its own script — pool has no unlocked entries.
///   - When trash completes, OnDailyTaskCompleted fires → TakeOutTrash is unlocked and saved.
///   - On Day 2, the scheduler picks TakeOutTrash (only unlocked entry) and triggers it automatically.
/// </summary>
public class DailyTaskScheduler : MonoBehaviour
{
    public static DailyTaskScheduler Instance { get; private set; }

    [Tooltip("All tasks eligible for daily random selection. Only unlocked entries are drawn.")]
    [SerializeField] private List<DailyTaskEntry> _taskPool = new();

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly HashSet<string>       _unlockedTaskIds    = new();
    private readonly Dictionary<string, Action> _completionHandlers = new();
    private bool _saveLoaded;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        CampaignManager.OnDayChanged += OnDayActivated;
    }

    private void OnDisable()
    {
        CampaignManager.OnDayChanged -= OnDayActivated;
        UnsubscribeCompletionEvents();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads unlock state from the active save slot and re-applies default-unlocked entries.
    /// Called automatically on the first <see cref="CampaignManager.OnDayChanged"/> event.
    /// Safe to call manually from dev tooling at any point after the save slot is selected.
    /// </summary>
    public void LoadFromSave()
    {
        _unlockedTaskIds.Clear();
        UnsubscribeCompletionEvents();

        // Apply default-unlocked entries first.
        foreach (DailyTaskEntry entry in _taskPool)
        {
            if (entry.IsUnlockedByDefault)
                _unlockedTaskIds.Add(entry.TaskId);
        }

        // Restore persisted unlocks.
        if (SaveDataManager.Instance != null)
        {
            string[] saved = SaveDataManager.Instance.GetUnlockedDailyTaskIds();
            if (saved != null)
                foreach (string id in saved)
                    _unlockedTaskIds.Add(id);
        }

        SubscribeCompletionEvents();

        Debug.Log($"[DailyTaskScheduler] Loaded. Unlocked tasks ({_unlockedTaskIds.Count}): " +
                  string.Join(", ", _unlockedTaskIds));
    }

    /// <summary>
    /// Marks the task with the given ID as unlocked and persists to save data.
    /// Safe to call multiple times — duplicates are silently ignored. Server-only.
    /// </summary>
    public void UnlockTask(string taskId)
    {
        if (!IsServer) return;
        if (string.IsNullOrEmpty(taskId)) return;
        if (_unlockedTaskIds.Contains(taskId)) return;

        _unlockedTaskIds.Add(taskId);
        SaveDataManager.Instance?.UnlockDailyTask(taskId);

        Debug.Log($"[DailyTaskScheduler] Task unlocked: '{taskId}'.");
    }

    /// <summary>Returns true if the task with the given ID is currently unlocked.</summary>
    public bool IsTaskUnlocked(string taskId) => _unlockedTaskIds.Contains(taskId);

    // ── Day activation ────────────────────────────────────────────────────────

    private void OnDayActivated(int day)
    {
        // Load save data on the first event so the active slot is guaranteed to be selected.
        if (!_saveLoaded)
        {
            LoadFromSave();
            _saveLoaded = true;
        }

        // Task triggering is server-authoritative — clients only need their unlocked state loaded.
        if (!IsServer) return;

        // A resumed day restores its previously selected task from WorkdaySaveState. Do not roll
        // another task during DayActivated(), otherwise it would replace the player’s saved work.
        if (CampaignManager.Instance != null && CampaignManager.Instance.HasPendingWorkdayRestore)
            return;

        // Build the list of eligible (unlocked and valid) entries.
        var eligible = new List<DailyTaskEntry>();
        foreach (DailyTaskEntry entry in _taskPool)
        {
            if (!_unlockedTaskIds.Contains(entry.TaskId)) continue;
            if (entry.TaskComponent == null)
            {
                Debug.LogWarning($"[DailyTaskScheduler] Entry '{entry.TaskId}' has no TaskComponent assigned — skipping.");
                continue;
            }
            if (entry.TaskComponent is not IDailyTask)
            {
                Debug.LogWarning($"[DailyTaskScheduler] Entry '{entry.TaskId}' component does not implement IDailyTask — skipping.");
                continue;
            }
            eligible.Add(entry);
        }

        if (eligible.Count == 0)
        {
            Debug.Log($"[DailyTaskScheduler] Day {day} — no unlocked daily tasks to trigger.");
            return;
        }

        DailyTaskEntry chosen = eligible[UnityEngine.Random.Range(0, eligible.Count)];
        IDailyTask task = chosen.TaskComponent as IDailyTask;

        Debug.Log($"[DailyTaskScheduler] Day {day} — triggering daily task '{chosen.TaskId}' " +
                  $"(pool had {eligible.Count} eligible task(s)).");

        task?.TriggerDailyTask();
    }

    // ── Completion event wiring ───────────────────────────────────────────────

    private void SubscribeCompletionEvents()
    {
        foreach (DailyTaskEntry entry in _taskPool)
        {
            if (!entry.UnlockOnFirstCompletion) continue;
            if (entry.TaskComponent is not IDailyTask task) continue;
            if (_completionHandlers.ContainsKey(entry.TaskId)) continue;

            string capturedId = entry.TaskId;
            Action handler = () => OnTaskCompleted(capturedId);
            _completionHandlers[capturedId] = handler;
            task.OnDailyTaskCompleted += handler;
        }
    }

    private void UnsubscribeCompletionEvents()
    {
        foreach (DailyTaskEntry entry in _taskPool)
        {
            if (entry.TaskComponent is not IDailyTask task) continue;
            if (!_completionHandlers.TryGetValue(entry.TaskId, out Action handler)) continue;
            task.OnDailyTaskCompleted -= handler;
        }
        _completionHandlers.Clear();
    }

    private void OnTaskCompleted(string taskId)
    {
        UnlockTask(taskId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsServer =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
}
