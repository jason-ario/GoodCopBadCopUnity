using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Page content object for the Tasks tab.
/// Reads from GuidebookTaskRegistry — tasks can be added by any system at any time.
/// Rebuilds rows immediately when the registry changes, and refreshes completion
/// states each time the tab is opened.
/// </summary>
public class GuidebookTaskListContents : GuidebookPageContents
{
    [Tooltip("Prefab containing a GuidebookTaskRow component (may be on a child). Instantiated once per task.")]
    [SerializeField] private GameObject _taskRowPrefab;

    [Tooltip("RectTransform that acts as the parent for spawned rows. Should have a VerticalLayoutGroup.")]
    [SerializeField] private RectTransform _rowContainer;

    [Tooltip("Shown when there are no active tasks.")]
    [SerializeField] private TextMeshProUGUI _fallbackLabel;

    private readonly List<GuidebookTaskRow> _rows = new();

    private void Awake()
    {
        if (_taskRowPrefab == null)
            Debug.LogWarning("[GuidebookTaskListContents] Task row prefab not assigned.");

        if (_rowContainer == null)
            Debug.LogWarning("[GuidebookTaskListContents] Row container not assigned.");
    }

    private void OnEnable()
    {
        GuidebookTaskRegistry.OnTaskListChanged += OnTaskListChanged;
    }

    private void OnDisable()
    {
        GuidebookTaskRegistry.OnTaskListChanged -= OnTaskListChanged;
    }

    /// <summary>Immediately rebuilds rows when the registry changes (task added/removed/replaced).</summary>
    private void OnTaskListChanged()
    {
        BuildRows();
    }

    /// <summary>
    /// Refreshes completion states on all rows.
    /// Called by GuidebookTabController when this tab becomes active.
    /// </summary>
    public override void Refresh()
    {
        if (GuidebookTaskRegistry.Instance == null || GuidebookTaskRegistry.Instance.Tasks.Count == 0)
        {
            SetFallbackVisible(true);
            return;
        }

        // If row count is out of sync (e.g. tab was hidden during a registry change), rebuild first.
        if (_rows.Count != GuidebookTaskRegistry.Instance.Tasks.Count)
            BuildRows();
        else
            RefreshRows();
    }

    private void BuildRows()
    {
        ClearRows();

        if (GuidebookTaskRegistry.Instance == null) return;

        IReadOnlyList<IBetweenShiftTask> tasks = GuidebookTaskRegistry.Instance.Tasks;
        bool hasTasks = tasks.Count > 0;
        SetFallbackVisible(!hasTasks);

        if (!hasTasks || _rowContainer == null || _taskRowPrefab == null) return;

        foreach (IBetweenShiftTask task in tasks)
        {
            GameObject instance = Instantiate(_taskRowPrefab, _rowContainer);
            GuidebookTaskRow row = instance.GetComponentInChildren<GuidebookTaskRow>();

            if (row == null)
            {
                Debug.LogWarning("[GuidebookTaskListContents] Row prefab has no GuidebookTaskRow component.", instance);
                continue;
            }

            row.Bind(task);
            _rows.Add(row);
        }
    }

    private void RefreshRows()
    {
        IReadOnlyList<IBetweenShiftTask> tasks = GuidebookTaskRegistry.Instance.Tasks;
        SetFallbackVisible(tasks.Count == 0);

        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] != null)
                _rows[i].Bind(tasks[i]);
        }
    }

    private void ClearRows()
    {
        foreach (GuidebookTaskRow row in _rows)
        {
            if (row != null)
                Destroy(row.transform.parent == _rowContainer ? row.gameObject : row.transform.parent.gameObject);
        }

        _rows.Clear();
    }

    private void SetFallbackVisible(bool visible)
    {
        if (_fallbackLabel != null)
            _fallbackLabel.gameObject.SetActive(visible);
    }
}
