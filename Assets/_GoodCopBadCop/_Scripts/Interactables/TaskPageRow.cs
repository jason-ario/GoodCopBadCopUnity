using TMPro;
using UnityEngine;

/// <summary>
/// A single row on the Task Page paper.
/// Call <see cref="Bind"/> to populate the row from a threat and completion state.
/// Active tasks show their ThreatDescription (e.g. "2/5") inline after the name.
/// Completed tasks are displayed with a TMP strikethrough and no description.
/// Subscribes to TaskRegistry.OnTaskStateChanged so the description stays live.
/// </summary>
public class TaskPageRow : MonoBehaviour
{
    [Tooltip("TextMeshProUGUI label that displays the task name and progress (inside a world-space Canvas).")]
    [SerializeField] private TextMeshProUGUI _label;

    private ISystemicThreat _threat;
    private bool _completed;

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

    /// <summary>Populates the row with the given threat and completion state.</summary>
    public void Bind(ISystemicThreat threat, bool completed)
    {
        _threat    = threat;
        _completed = completed;
        Refresh();
    }

    private void Refresh()
    {
        if (_label == null || _threat == null) return;

        if (_completed)
        {
            _label.text = $"<s>- {_threat.ThreatName}</s>";
            return;
        }

        string description = _threat.ThreatDescription;
        _label.text = string.IsNullOrWhiteSpace(description)
            ? $"- {_threat.ThreatName}"
            : $"- {_threat.ThreatName} ({description})";
    }
}
