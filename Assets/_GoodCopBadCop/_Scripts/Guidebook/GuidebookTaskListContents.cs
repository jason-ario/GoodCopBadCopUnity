using TMPro;
using UnityEngine;

/// <summary>
/// Page content object for the Tasks tab.
/// Rebuilds task labels from BetweenShiftTaskManager each time the tab is opened.
/// Follows the NewspaperContentsController pattern: TextMeshPro (3D) children
/// positioned in local space, rendered to a RenderTexture by an orthographic camera.
/// </summary>
public class GuidebookTaskListContents : GuidebookPageContents
{
    [Tooltip("Pre-placed TextMeshPro label objects, one per task slot. "
           + "Extras are hidden if there are fewer active tasks than slots.")]
    [SerializeField] private TextMeshPro[] _taskLabels;

    [Tooltip("Shown when there are no active tasks or BetweenShiftTaskManager is unavailable.")]
    [SerializeField] private TextMeshPro _fallbackLabel;

    private void Awake()
    {
        if (_taskLabels == null || _taskLabels.Length == 0)
            Debug.LogWarning("[GuidebookTaskListContents] No task label slots assigned.");
    }

    /// <summary>
    /// Rebuilds all task label text from BetweenShiftTaskManager.Tasks.
    /// Hides unused label slots and shows the fallback if no tasks are available.
    /// </summary>
    public override void Refresh()
    {
        IBetweenShiftTask[] tasks = BetweenShiftTaskManager.Instance != null
            ? BetweenShiftTaskManager.Instance.Tasks
            : null;

        bool hasTasks = tasks != null && tasks.Length > 0;

        if (_fallbackLabel != null)
            _fallbackLabel.gameObject.SetActive(!hasTasks);

        if (_taskLabels == null) return;

        for (int i = 0; i < _taskLabels.Length; i++)
        {
            if (_taskLabels[i] == null) continue;

            bool hasTask = hasTasks && i < tasks.Length;
            _taskLabels[i].gameObject.SetActive(hasTask);

            if (hasTask)
            {
                string prefix = tasks[i].IsComplete ? "[x] " : "[ ] ";
                _taskLabels[i].text = prefix + tasks[i].TaskName;
            }
        }
    }
}
