using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Page content object for the Tasks tab.
/// Reads from TaskRegistry — threats can be registered by any system at any time.
/// Rebuilds rows immediately when the registry changes, and refreshes threat levels
/// each time the tab is opened.
/// </summary>
public class GuidebookTaskListContents : GuidebookPageContents
{
    [Tooltip("Prefab containing a GuidebookTaskRow component (may be on a child). Instantiated once per threat.")]
    [SerializeField] private GameObject _taskRowPrefab;

    [Tooltip("RectTransform that acts as the parent for spawned rows. Should have a VerticalLayoutGroup.")]
    [SerializeField] private RectTransform _rowContainer;

    [Tooltip("Shown when there are no active threats.")]
    [SerializeField] private TextMeshProUGUI _fallbackLabel;

    private readonly List<GuidebookTaskRow> _rows = new();

    private void Awake()
    {
        if (_taskRowPrefab == null)
            Debug.LogWarning("[GuidebookTaskListContents] Task row prefab not assigned.");

        if (_rowContainer == null)
            Debug.LogWarning("[GuidebookTaskListContents] Row container not assigned.");

        // Subscribe persistently so we receive registry changes even while the guidebook is closed.
        TaskRegistry.OnTaskListChanged += OnTaskListChangedHandler;
    }

    private void OnDestroy()
    {
        TaskRegistry.OnTaskListChanged -= OnTaskListChangedHandler;
    }

    private void OnEnable()
    {
        // Catch up if threats were added while the guidebook was closed.
        BuildRows();
        TriggerRenderTextureRefresh();
    }

    /// <summary>
    /// Rebuilds rows then triggers a render texture refresh so the
    /// render camera recaptures the new content.
    /// </summary>
    private void OnTaskListChangedHandler()
    {
        BuildRows();
        TriggerRenderTextureRefresh();
    }

    private void TriggerRenderTextureRefresh()
    {
        GuidebookContentsContainer.Instance?.TriggerRender();
    }

    /// <summary>
    /// Refreshes threat levels on all rows.
    /// Called by GuidebookTabController when this tab becomes active.
    /// </summary>
    public override void Refresh()
    {
        if (TaskRegistry.Instance == null || TaskRegistry.Instance.Threats.Count == 0)
        {
            SetFallbackVisible(true);
            TriggerRenderTextureRefresh();
            return;
        }

        // If row count is out of sync (e.g. tab was hidden during a registry change), rebuild first.
        if (_rows.Count != TaskRegistry.Instance.Threats.Count)
            BuildRows();
        else
            RefreshRows();

        TriggerRenderTextureRefresh();
    }

    private void BuildRows()
    {
        ClearRows();

        if (TaskRegistry.Instance == null) return;

        IReadOnlyList<ISystemicThreat> threats = TaskRegistry.Instance.Threats;
        bool hasThreats = threats.Count > 0;
        SetFallbackVisible(!hasThreats);

        if (!hasThreats || _rowContainer == null || _taskRowPrefab == null) return;

        foreach (ISystemicThreat threat in threats)
        {
            GameObject instance = Instantiate(_taskRowPrefab, _rowContainer);
            GuidebookTaskRow row = instance.GetComponentInChildren<GuidebookTaskRow>();

            if (row == null)
            {
                Debug.LogWarning("[GuidebookTaskListContents] Row prefab has no GuidebookTaskRow component.", instance);
                continue;
            }

            row.Bind(threat);
            _rows.Add(row);
        }
    }

    private void RefreshRows()
    {
        IReadOnlyList<ISystemicThreat> threats = TaskRegistry.Instance.Threats;
        SetFallbackVisible(threats.Count == 0);

        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] != null)
                _rows[i].Bind(threats[i]);
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
