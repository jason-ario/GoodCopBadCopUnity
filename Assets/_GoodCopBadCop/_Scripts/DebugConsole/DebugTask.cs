using UnityEngine;

/// <summary>
/// Lightweight debug-only task. Does not require a NetworkObject or server authority.
/// Toggle completion via DebugConsole (F5 by default).
/// </summary>
public class DebugTask : MonoBehaviour, IBetweenShiftTask
{
    private const string DefaultName = "Debug Task";
    private const string DefaultDescription = "A fake task added at runtime for testing the guidebook task list.";
    private const int DefaultXpReward = 99;

    public string TaskName => DefaultName;
    public string TaskDescription => DefaultDescription;
    public int XpReward => DefaultXpReward;
    public bool IsComplete { get; private set; }

    /// <summary>Marks the task as complete and notifies the registry and manager.</summary>
    public void Complete()
    {
        if (IsComplete) return;
        IsComplete = true;
        Debug.Log("[DebugTask] Marked complete.");

        GuidebookTaskRegistry.Instance.NotifyTaskStateChanged();

        if (BetweenShiftTaskManager.Instance != null && BetweenShiftTaskManager.Instance.IsSpawned)
            BetweenShiftTaskManager.Instance.NotifyTaskComplete(this);
    }

    public void ResetTask()
    {
        IsComplete = false;
        GuidebookTaskRegistry.Instance.NotifyTaskStateChanged();
        Debug.Log("[DebugTask] Reset.");
    }
}
