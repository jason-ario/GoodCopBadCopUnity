using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Displays active <see cref="TutorialTask"/> entries from <see cref="TaskRegistry"/>
/// as a compact list in the player HUD. Only tutorial tasks (Day 1 step prompts, etc.)
/// appear here; regular systemic-threat tasks are shown exclusively on the Task Page.
/// Hides the container automatically when there are no tutorial tasks.
/// </summary>
public class HUDTaskList : MonoBehaviour
{
    [Tooltip("Prefab for a single task row. Must have a TextMeshProUGUI somewhere in its hierarchy for the task name.")]
    [SerializeField] private GameObject _taskRowPrefab;

    [Tooltip("Parent RectTransform that holds the spawned rows. Should have a VerticalLayoutGroup.")]
    [SerializeField] private RectTransform _rowContainer;

    private readonly List<GameObject> _rows = new();

    private void Awake()
    {
        if (_taskRowPrefab == null)
            Debug.LogWarning("[HUDTaskList] Task row prefab not assigned.", this);

        if (_rowContainer == null)
            Debug.LogWarning("[HUDTaskList] Row container not assigned.", this);
    }

    private void OnEnable()
    {
        TaskRegistry.OnTaskListChanged += Rebuild;
        TaskRegistry.OnTaskStateChanged += Rebuild;
        Rebuild();
    }

    private void OnDisable()
    {
        TaskRegistry.OnTaskListChanged -= Rebuild;
        TaskRegistry.OnTaskStateChanged -= Rebuild;
    }

    /// <summary>Clears and rebuilds all rows from the current registry state.</summary>
    private void Rebuild()
    {
        ClearRows();

        if (_rowContainer == null)
            return;

        IReadOnlyList<ISystemicThreat> allThreats = TaskRegistry.Instance != null
            ? TaskRegistry.Instance.Threats
            : System.Array.Empty<ISystemicThreat>();

        // Only display tutorial tasks in the HUD; regular tasks live on the Task Page.
        var tutorialTasks = new List<ISystemicThreat>();
        foreach (ISystemicThreat threat in allThreats)
        {
            if (threat is TutorialTask)
                tutorialTasks.Add(threat);
        }

        bool hasTutorialTasks = tutorialTasks.Count > 0;

        // Show/hide the row container rather than this MonoBehaviour so that
        // registry events are still received when the list is empty.
        _rowContainer.gameObject.SetActive(hasTutorialTasks);

        if (!hasTutorialTasks || _taskRowPrefab == null)
            return;

        foreach (ISystemicThreat threat in tutorialTasks)
            SpawnRow(threat);
    }

    /// <summary>Instantiates the task row prefab and populates its TMP label.</summary>
    private void SpawnRow(ISystemicThreat threat)
    {
        GameObject row = Instantiate(_taskRowPrefab, _rowContainer);
        row.name = "Task Row";

        TextMeshProUGUI label = row.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
        {
            bool hasDescription = !string.IsNullOrEmpty(threat.ThreatDescription);
            label.text = hasDescription
                ? $"{threat.ThreatName} {threat.ThreatDescription}"
                : threat.ThreatName;
        }
        else
            Debug.LogWarning("[HUDTaskList] Task row prefab has no TextMeshProUGUI in its hierarchy.", row);

        _rows.Add(row);
    }

    private void ClearRows()
    {
        foreach (GameObject row in _rows)
        {
            if (row != null)
                Destroy(row);
        }

        _rows.Clear();
    }
}
