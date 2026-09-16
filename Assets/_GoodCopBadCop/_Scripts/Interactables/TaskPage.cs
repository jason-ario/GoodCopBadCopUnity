using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders a dynamic task list onto the Task Page paper as a world-space Canvas.
/// Rows are instantiated from a prefab and parented to a container inside Task Page Contents.
/// Row layout is handled by a Vertical Layout Group on the container.
/// Only currently-active tasks are shown — a task's row is removed the moment it's no longer
/// in the registry, so nothing ever lingers crossed-out. Tutorial tasks are excluded.
///
/// Scene setup:
///   - Assign _taskRowPrefab  → Task Item prefab (has TaskPageRow component)
///   - Assign _rowContainer   → Task Page Contents/Canvas/Tasks (Transform)
/// </summary>
public class TaskPage : MonoBehaviour
{
    /// <summary>Scene-local instance, used by CampaignManager to reset the page on day change.</summary>
    public static TaskPage Instance { get; private set; }

    [Header("Row Spawning")]
    [Tooltip("Prefab with a TaskPageRow component. Instantiated once per tracked task.")]
    [SerializeField] private GameObject _taskRowPrefab;

    [Tooltip("Parent Transform under which task rows are spawned. Should have a Vertical Layout Group component.")]
    [SerializeField] private Transform _rowContainer;

    private readonly List<TaskPageRow> _rows = new();

    /// <summary>Ordered list of every currently-active, non-tutorial task from the registry.</summary>
    private readonly List<ISystemicThreat> _knownTasks = new();

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        TaskRegistry.OnTaskListChanged  += OnTaskListChanged;
        TaskRegistry.OnTaskStateChanged += OnTaskStateChanged;
        RefreshTaskList();
    }

    private void OnDisable()
    {
        TaskRegistry.OnTaskListChanged  -= OnTaskListChanged;
        TaskRegistry.OnTaskStateChanged -= OnTaskStateChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Clears every task the page has ever tracked and rebuilds the (now empty) row list.
    /// Call this when a day ends so the page doesn't keep accumulating stale entries from
    /// previous days.
    /// </summary>
    public void ResetTasks()
    {
        _knownTasks.Clear();
        RebuildRows();
    }

    // ── TaskRegistry event handlers ───────────────────────────────────────────

    private void OnTaskListChanged()  => RefreshTaskList();
    private void OnTaskStateChanged() => RebuildRows();

    // ── Task list management ──────────────────────────────────────────────────

    /// <summary>
    /// Syncs _knownTasks with the current registry then rebuilds all rows.
    ///   - New non-tutorial threats are appended.
    ///   - Threats no longer in the registry are dropped entirely (no crossed-out lingering).
    /// </summary>
    private void RefreshTaskList()
    {
        if (TaskRegistry.Instance == null)
        {
            _knownTasks.Clear();
            RebuildRows();
            return;
        }

        IReadOnlyList<ISystemicThreat> current = TaskRegistry.Instance.Threats;

        _knownTasks.RemoveAll(threat =>
        {
            foreach (ISystemicThreat t in current)
                if (ReferenceEquals(t, threat)) return false;
            return true;
        });

        foreach (ISystemicThreat threat in current)
        {
            if (threat is TutorialTask) continue;
            if (_knownTasks.Exists(t => ReferenceEquals(t, threat))) continue;
            _knownTasks.Add(threat);
        }

        RebuildRows();
    }

    // ── Row spawning ──────────────────────────────────────────────────────────

    /// <summary>
    /// Destroys all existing row instances and respawns them from _knownTasks.
    /// Layout is handled by the Vertical Layout Group on _rowContainer.
    /// </summary>
    private void RebuildRows()
    {
        ClearRows();

        if (_taskRowPrefab == null || _rowContainer == null) return;

        foreach (ISystemicThreat threat in _knownTasks)
        {
            GameObject instance = Instantiate(_taskRowPrefab, _rowContainer);
            TaskPageRow row = instance.GetComponent<TaskPageRow>();

            if (row == null)
            {
                Debug.LogWarning("[TaskPage] Task row prefab is missing a TaskPageRow component.", instance);
                continue;
            }

            row.Bind(threat);
            _rows.Add(row);
        }
    }

    /// <summary>Destroys all instantiated row GameObjects and clears the row list.</summary>
    private void ClearRows()
    {
        foreach (TaskPageRow row in _rows)
        {
            if (row != null)
                Destroy(row.gameObject);
        }
        _rows.Clear();
    }
}
