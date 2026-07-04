using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Displays the active task names from <see cref="TaskRegistry"/> as a
/// compact list in the player HUD. Sits below the currency display in the top-right
/// corner. Instantiates a <see cref="_taskRowPrefab"/> per task and hides the
/// container automatically when there are no tasks.
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

        IReadOnlyList<ISystemicThreat> threats = TaskRegistry.Instance != null
            ? TaskRegistry.Instance.Threats
            : System.Array.Empty<ISystemicThreat>();

        bool hasThreats = threats.Count > 0;

        // Show/hide the row container rather than this MonoBehaviour so that
        // registry events are still received when the list is empty.
        _rowContainer.gameObject.SetActive(hasThreats);

        if (!hasThreats || _taskRowPrefab == null)
            return;

        foreach (ISystemicThreat threat in threats)
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
