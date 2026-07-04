using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders a dynamic task list onto the Task Page paper via a render texture.
/// Rows are instantiated from a prefab and parented to a container inside Task Page Contents.
/// Row layout is handled by a Vertical Layout Group on the container.
/// Tasks removed from the registry are kept with a strikethrough to show completion.
/// Tutorial tasks are excluded. The render camera stays permanently active so all content
/// (including the static Header) is always captured without timing issues.
///
/// Scene setup:
///   - Assign _taskRowPrefab         → Task Item prefab (has TaskPageRow component)
///   - Assign _rowContainer          → Task Page Contents/Contents/Task Rows (Transform, has Vertical Layout Group)
///   - Assign _renderCamera          → Task Page Contents/Camera (1)
///   - Assign _paperRenderer         → root/GLTF_SceneRootNode/Cube.001_1/Object_6 (MeshRenderer)
///   - Assign _renderTextureTemplate → Assets/_GoodCopBadCop/_Textures/Render Textures/Task Checklist.renderTexture
/// </summary>
public class TaskPage : MonoBehaviour
{
    [Header("Row Spawning")]
    [Tooltip("Prefab with a TaskPageRow component. Instantiated once per tracked task.")]
    [SerializeField] private GameObject _taskRowPrefab;

    [Tooltip("Parent Transform under which task rows are spawned. Should have a Vertical Layout Group component.")]
    [SerializeField] private Transform _rowContainer;

    [Header("Render Texture")]
    [Tooltip("Orthographic camera inside Task Page Contents that renders all content into the render texture.")]
    [SerializeField] private Camera _renderCamera;

    [Tooltip("MeshRenderer on the paper mesh whose material exposes _OverlayMap.")]
    [SerializeField] private MeshRenderer _paperRenderer;

    [Tooltip("Project-asset RenderTexture used as a descriptor template. A runtime clone is created per instance.")]
    [SerializeField] private RenderTexture _renderTextureTemplate;

    private static readonly int OverlayMapProperty = Shader.PropertyToID("_OverlayMap");

    private RenderTexture _renderTexture;
    private Material _paperMaterialInstance;

    private readonly List<TaskPageRow> _rows = new();

    /// <summary>
    /// Ordered list of every non-tutorial task seen in the registry.
    /// The bool is true when the task has been completed (removed from the registry).
    /// </summary>
    private readonly List<(ISystemicThreat threat, bool completed)> _knownTasks = new();

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        SetupRenderTexture();
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
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }

        if (_paperMaterialInstance != null)
            Destroy(_paperMaterialInstance);
    }

    // ── Render texture setup ──────────────────────────────────────────────────

    /// <summary>
    /// Clones the RT template, assigns it to the render camera, and stamps it onto a
    /// per-instance material. The camera stays active so all TMP content is always captured.
    /// </summary>
    private void SetupRenderTexture()
    {
        if (_renderCamera == null || _paperRenderer == null)
        {
            Debug.LogWarning("[TaskPage] Render camera or paper renderer not assigned.", this);
            return;
        }

        RenderTextureDescriptor desc = _renderTextureTemplate != null
            ? _renderTextureTemplate.descriptor
            : new RenderTextureDescriptor(1024, 1024, RenderTextureFormat.Default, 24);

        _renderTexture = new RenderTexture(desc)
        {
            name       = "TaskPageRT",
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        _renderTexture.Create();

        _renderCamera.targetTexture = _renderTexture;

        _paperMaterialInstance = new Material(_paperRenderer.sharedMaterial);
        _paperMaterialInstance.SetTexture(OverlayMapProperty, _renderTexture);

        Material[] slots = new Material[_paperRenderer.sharedMaterials.Length];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = _paperMaterialInstance;
        _paperRenderer.materials = slots;

        _renderCamera.gameObject.SetActive(true);
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
    /// Destroys all existing row instances and respawns them from _knownTasks.
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
