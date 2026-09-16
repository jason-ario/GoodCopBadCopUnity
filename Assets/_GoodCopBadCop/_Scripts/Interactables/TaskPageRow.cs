using TMPro;
using UnityEngine;

/// <summary>
/// A single row on the Task Page paper.
/// Call <see cref="Bind"/> to populate the row from a threat.
/// Rows always show their ThreatDescription (e.g. "2/5") inline after the name — a task's row
/// is destroyed by <see cref="TaskPage"/> the moment it's no longer active, so no completed
/// state or strikethrough is ever rendered here.
/// Subscribes to TaskRegistry.OnTaskStateChanged so the description stays live.
/// </summary>
public class TaskPageRow : MonoBehaviour
{
    [Tooltip("TextMeshProUGUI label that displays the task name and progress (inside a world-space Canvas).")]
    [SerializeField] private TextMeshProUGUI _label;

    private ISystemicThreat _threat;

    private void Awake()
    {
        if (_label == null)
            _label = GetComponent<TextMeshProUGUI>();
    }

    private void OnEnable()
    {
        TaskRegistry.OnTaskStateChanged += Refresh;
    }

    private void OnDisable()
    {
        TaskRegistry.OnTaskStateChanged -= Refresh;
    }

    /// <summary>Populates the row with the given threat.</summary>
    public void Bind(ISystemicThreat threat)
    {
        _threat = threat;
        Refresh();
    }

    private void Refresh()
    {
        if (_label == null || _threat == null) return;

        string description = _threat.ThreatDescription;
        _label.text = string.IsNullOrWhiteSpace(description)
            ? $"- {_threat.ThreatName}"
            : $"- {_threat.ThreatName} ({description})";
    }
}
