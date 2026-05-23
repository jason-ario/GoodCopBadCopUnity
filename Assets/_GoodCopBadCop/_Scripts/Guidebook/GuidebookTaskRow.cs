using TMPro;
using UnityEngine;

/// <summary>
/// Represents a single task row in the guidebook task list canvas.
/// Call Bind() to populate the row from an IBetweenShiftTask.
/// The row self-manages its checkmark by subscribing to GuidebookTaskRegistry.OnTaskStateChanged.
/// </summary>
public class GuidebookTaskRow : MonoBehaviour
{
    [Tooltip("Child GameObject shown when the task is complete, hidden otherwise.")]
    [SerializeField] private GameObject _checkmark;

    [Tooltip("Displays the task name in uppercase bold.")]
    [SerializeField] private TextMeshProUGUI _nameLabel;

    [Tooltip("Displays the short task description.")]
    [SerializeField] private TextMeshProUGUI _descriptionLabel;

    [Tooltip("Displays the XP reward, e.g. '★ 75 XP'.")]
    [SerializeField] private TextMeshProUGUI _xpLabel;

    private const string XpFormat = "★ {0} XP";

    private IBetweenShiftTask _task;

    private void OnEnable()
    {
        GuidebookTaskRegistry.OnTaskStateChanged += OnTaskStateChanged;
        Debug.Log($"[GuidebookTaskRow] Subscribed to OnTaskStateChanged. Task: {_task?.TaskName ?? "none"}", this);
    }

    private void OnDisable()
    {
        GuidebookTaskRegistry.OnTaskStateChanged -= OnTaskStateChanged;
    }

    /// <summary>Populates all row fields from the given task and syncs the checkmark state.</summary>
    public void Bind(IBetweenShiftTask task)
    {
        if (task == null) return;

        _task = task;

        if (_nameLabel != null)
            _nameLabel.text = task.TaskName.ToUpper();

        if (_descriptionLabel != null)
            _descriptionLabel.text = task.TaskDescription;

        if (_xpLabel != null)
            _xpLabel.text = string.Format(XpFormat, task.XpReward);

        Debug.Log($"[GuidebookTaskRow] Bind called. Task: '{task.TaskName}', IsComplete: {task.IsComplete}, Checkmark ref: {(_checkmark != null ? _checkmark.name : "NULL")}", this);
        SetComplete(task.IsComplete);
    }

    private void OnTaskStateChanged()
    {
        Debug.Log($"[GuidebookTaskRow] OnTaskStateChanged received. Task: '{_task?.TaskName ?? "null"}', IsComplete: {_task?.IsComplete}", this);
        if (_task != null)
            SetComplete(_task.IsComplete);
    }

    private void SetComplete(bool complete)
    {
        Debug.Log($"[GuidebookTaskRow] SetComplete({complete}). Checkmark: {(_checkmark != null ? _checkmark.name : "NULL")}", this);
        if (_checkmark != null)
            _checkmark.SetActive(complete);
    }
}
