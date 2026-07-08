using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders a dynamic task list onto the Task Page paper via a render texture.
/// Rows are instantiated from a prefab and parented to a container inside Task Page Contents.
/// Row layout is handled by a Vertical Layout Group on the container.
/// Tasks removed from the registry are kept with a strikethrough to show completion.
/// Tutorial tasks are excluded.
///
/// Follows the same stateless snapshot pattern as DailyFaxContentsController and ATMScreenController:
/// no render texture or material instances are created at runtime. The camera's targetTexture
/// and the paper mesh material are pre-wired directly to Task Checklist.renderTexture in the
/// scene, so the only runtime work is spawning rows and toggling the camera for one frame.
///
/// Scene setup:
///   - Camera (1).targetTexture       → Task Checklist.renderTexture  (set in Inspector)
///   - Task List.mat._OverlayMap      → Task Checklist.renderTexture  (set in material)
///   - Assign _taskRowPrefab          → Task Item prefab (has TaskPageRow component)
///   - Assign _rowContainer           → Task Page Contents/Canvas/Tasks (Transform)
///   - Assign _renderCamera           → Task Page Contents/Camera (1)  (disabled by default)
/// </summary>
public class TaskPage : MonoBehaviour
{
    [Header("Row Spawning")]
    [Tooltip("Prefab with a TaskPageRow component. Instantiated once per tracked task.")]
    [SerializeField] private GameObject _taskRowPrefab;

    [Tooltip("Parent Transform under which task rows are spawned. Should have a Vertical Layout Group component.")]
    [SerializeField] private Transform _rowContainer;

    [Header("Render Texture")]
    [Tooltip("Orthographic camera inside Task Page Contents. targetTexture must be Task Checklist.renderTexture. Disabled by default.")]
    [SerializeField] private Camera _renderCamera;

    private readonly List<TaskPageRow> _rows = new();

    /// <summary>
    /// Ordered list of every non-tutorial task seen in the registry.
    /// The bool is true when the task has been completed (removed from the registry).
    /// </summary>
    private readonly List<(ISystemicThreat threat, bool completed)> _knownTasks = new();

    // ── Unity lifecycle ───────────────────────────────────────────────────────

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

    // ── TaskRegistry event handlers ───────────────────────────────────────────

    private void OnTaskListChanged()  => RefreshTaskList();
    private void OnTaskStateChanged() => RebuildRows();

    // ── Task list management ──────────────────────────────────────────────────

    /// <summary>
    /// Syncs _knownTasks with the current registry then rebuilds all rows.
    ///   - New non-tutorial threats are appended as active.
    ///   - Previously tracked threats no longer in the registry are marked completed.
    /// </summary>
    private void RefreshTaskList()
    {
        if (TaskRegistry.Instance == null)
        {
            RebuildRows();
            return;
        }

        IReadOnlyList<ISystemicThreat> current = TaskRegistry.Instance.Threats;

        foreach (ISystemicThreat threat in current)
        {
            if (threat is TutorialTask) continue;
            if (_knownTasks.Exists(e => ReferenceEquals(e.threat, threat))) continue;
            _knownTasks.Add((threat, false));
        }

        for (int i = 0; i < _knownTasks.Count; i++)
        {
            (ISystemicThreat threat, bool completed) = _knownTasks[i];
            if (completed) continue;

            bool stillActive = false;
            foreach (ISystemicThreat t in current)
            {
                if (ReferenceEquals(t, threat)) { stillActive = true; break; }
            }

            if (!stillActive)
                _knownTasks[i] = (threat, true);
        }

        RebuildRows();
    }

    // ── Row spawning ──────────────────────────────────────────────────────────

    /// <summary>
    /// Destroys all existing row instances and respawns them from _knownTasks,
    /// then fires a camera snapshot so the render texture is captured after TMP
    /// has fully submitted its mesh geometry.
    /// Layout is handled by the Vertical Layout Group on _rowContainer.
    /// </summary>
    private void RebuildRows()
    {
        ClearRows();

        if (_taskRowPrefab == null || _rowContainer == null) return;

        foreach ((ISystemicThreat threat, bool completed) in _knownTasks)
        {
            GameObject instance = Instantiate(_taskRowPrefab, _rowContainer);
            TaskPageRow row = instance.GetComponent<TaskPageRow>();

            if (row == null)
            {
                Debug.LogWarning("[TaskPage] Task row prefab is missing a TaskPageRow component.", instance);
                continue;
            }

            row.Bind(threat, completed);
            _rows.Add(row);
        }

        StartCoroutine(CameraSnapshot());
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

    // ── Snapshot ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Activates the render camera for exactly one frame after TMP geometry has been submitted.
    /// Identical to DailyFaxContentsController.CameraSnapshot().
    /// </summary>
    private IEnumerator CameraSnapshot()
    {
        if (_renderCamera == null) yield break;

        yield return new WaitForEndOfFrame();
        _renderCamera.gameObject.SetActive(true);
        yield return new WaitForEndOfFrame();
        _renderCamera.gameObject.SetActive(false);
    }
}
