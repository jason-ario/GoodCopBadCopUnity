using UnityEngine;

/// <summary>
/// Marks complete as soon as any player opens the tool shop during the night phase.
/// Subscribes to UIController.OnToolShopOpened — purchase is optional, visiting is the gate.
/// </summary>
public class ToolShopVisitTask : MonoBehaviour, IBetweenShiftTask
{
    private bool _isComplete;

    public string TaskName => "Visit Tool Shop";
    public bool IsComplete => _isComplete;

    private void OnDestroy()
    {
        UIController.OnToolShopOpened -= OnToolShopOpened;
    }

    /// <summary>Resets the task and re-subscribes to the shop-opened event.</summary>
    public void ResetTask()
    {
        _isComplete = false;
        UIController.OnToolShopOpened -= OnToolShopOpened;
        UIController.OnToolShopOpened += OnToolShopOpened;
    }

    private void OnToolShopOpened()
    {
        if (_isComplete) return;
        _isComplete = true;
        UIController.OnToolShopOpened -= OnToolShopOpened;

        if (BetweenShiftTaskManager.Instance != null)
            BetweenShiftTaskManager.Instance.NotifyTaskComplete(this);
    }
}
