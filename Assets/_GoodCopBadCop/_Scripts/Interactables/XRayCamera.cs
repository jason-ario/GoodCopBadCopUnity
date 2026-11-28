using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Camera that lives on the X-ray monitor and renders the X-ray feed into a RenderTexture.
///
/// The look comes from <see cref="_xRayRenderer"/> (XRay_Renderer): it draws no regular geometry
/// (black background). <see cref="XRayBodyFeature"/> draws what this component collects from
/// nearby suspects, using the current scan mode's materials:
///   - body renderers (any layer)                      -> <see cref="Mode.BodyMaterial"/>
///   - <see cref="XRayInternalPart"/> skeleton parts   -> <see cref="Mode.SkeletonMaterial"/>
///   - <see cref="XRayInternalPart"/> organ parts      -> <see cref="Mode.OrganMaterial"/> (tinted per organ)
/// Internal parts sit on the XRaySkeleton layer, which normal renderers exclude, so only the
/// X-ray feed shows them. In Play Mode, suspects without authored internals get
/// <see cref="XRayPlaceholderInternals"/> automatically.
///
/// Scan modes (<see cref="Mode"/>) also set the feed background and monitor tint.
/// <see cref="CycleMode"/> / <see cref="SetMode"/> are driven (and network-synced) by
/// <see cref="XRayJoystickController"/>.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class XRayCamera : MonoBehaviour
{
    /// <summary>Layer for bones/organs/parasites: hidden from normal cameras, drawn only on the X-ray.</summary>
    public const string SkeletonLayer = "XRaySkeleton";

    public struct Target
    {
        public Renderer Renderer;
        public int SubMeshCount;
        public Color Color;
    }

    /// <summary>One scan mode of the machine (e.g. Skeletal, Deep Tissue, Organs).</summary>
    [System.Serializable]
    public class Mode
    {
        public string Name = "Mode";

        [Tooltip("Material for suspect bodies in this mode. Null = the XRayBodyFeature's default material.")]
        public Material BodyMaterial;

        [Tooltip("Material for skeleton parts (XRayInternalPart, Skeleton). Null = skeleton hidden.")]
        public Material SkeletonMaterial;

        [Tooltip("Material for organ parts (XRayInternalPart, Organ), tinted per organ. Null = organs hidden.")]
        public Material OrganMaterial;

        [Tooltip("Clear color of the X-ray feed.")]
        public Color FeedBackground = Color.black;

        [Tooltip("Monitor tint (_Tint on the screen shader).")]
        public Color ScreenTint = new Color(0.8f, 0.92f, 1f, 1f);

        [Tooltip("Monitor background glow (_BackgroundColor on the screen shader).")]
        public Color ScreenBackground = new Color(0f, 0.008f, 0.02f, 1f);
    }

    private static readonly int TintId = Shader.PropertyToID("_Tint");
    private static readonly int ScreenBackgroundId = Shader.PropertyToID("_BackgroundColor");

    [Header("Modes")]
    [Tooltip("Scan modes, cycled in order by CycleMode(). The first entry is the default.")]
    [SerializeField] private List<Mode> _modes = new List<Mode>();

    [Tooltip("Monitor screen renderer (XRay Screen shader). Receives each mode's tint via a MaterialPropertyBlock.")]
    [SerializeField] private Renderer _screenRenderer;

    [Header("Internals")]
    [Tooltip("In Play Mode, add XRayPlaceholderInternals (primitive skeleton + organs) to scanned suspects " +
             "that have no authored XRayInternalParts.")]
    [SerializeField] private bool _autoPlaceholderInternals = true;

    [Header("Feed")]
    [Tooltip("The URP renderer used for the X-ray look. Must be in the active URP asset's Renderer List.")]
    [SerializeField] private ScriptableRendererData _xRayRenderer;

    [Tooltip("Render texture shown on the X-ray monitor.")]
    [SerializeField] private RenderTexture _targetTexture;

    [Tooltip("Suspects further than this from the camera are ignored.")]
    [SerializeField] private float _scanRadius = 15f;

    [Tooltip("Seconds between rescans for suspects / their renderers.")]
    [SerializeField] private float _refreshInterval = 0.5f;

    private Camera _camera;
    private readonly List<Target> _bodyTargets = new List<Target>();
    private readonly List<Target> _skeletonTargets = new List<Target>();
    private readonly List<Target> _organTargets = new List<Target>();
    private readonly List<Renderer> _rendererBuffer = new List<Renderer>();
    private float _nextRefreshTime;
    private int _modeIndex;
    private MaterialPropertyBlock _screenBlock;

    /// <summary>Suspect body renderers drawn by <see cref="XRayBodyFeature"/>.</summary>
    public IReadOnlyList<Target> BodyTargets => _bodyTargets;
    public IReadOnlyList<Target> SkeletonTargets => _skeletonTargets;
    public IReadOnlyList<Target> OrganTargets => _organTargets;

    public IReadOnlyList<Mode> Modes => _modes;
    public int ModeIndex => _modeIndex;
    public Mode CurrentMode => _modes.Count > 0 ? _modes[_modeIndex] : null;

    /// <summary>Body material for the current mode, or null to use the feature's default.</summary>
    public Material CurrentBodyMaterial => CurrentMode?.BodyMaterial;
    public Material CurrentSkeletonMaterial => CurrentMode?.SkeletonMaterial;
    public Material CurrentOrganMaterial => CurrentMode?.OrganMaterial;

    /// <summary>Raised with the new mode index whenever the mode changes.</summary>
    public event System.Action<int> ModeChanged;

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
        if (_targetTexture != null)
            _camera.targetTexture = _targetTexture;

        ApplyRenderer();
        ApplyMode();
        RefreshTargets();
    }

    /// <summary>Advances to the next mode in the list (wraps around).</summary>
    public void CycleMode()
    {
        if (_modes.Count == 0) return;
        SetMode((_modeIndex + 1) % _modes.Count);
    }

    public void SetMode(int index)
    {
        if (_modes.Count == 0) return;
        index = Mathf.Clamp(index, 0, _modes.Count - 1);
        if (index == _modeIndex) return;

        _modeIndex = index;
        ApplyMode();
        ModeChanged?.Invoke(_modeIndex);
    }

    /// <summary>Rescans suspects immediately instead of waiting for the refresh interval.</summary>
    public void ForceRefresh() => RefreshTargets();

    private void ApplyMode()
    {
        if (_camera == null) _camera = GetComponent<Camera>();
        _modeIndex = Mathf.Clamp(_modeIndex, 0, Mathf.Max(0, _modes.Count - 1));

        // Everything is drawn explicitly by XRayBodyFeature; the camera itself culls nothing.
        _camera.cullingMask = 0;

        Mode mode = CurrentMode;
        if (mode == null) return;
        _camera.backgroundColor = mode.FeedBackground;

        if (_screenRenderer != null)
        {
            _screenBlock ??= new MaterialPropertyBlock();
            _screenRenderer.GetPropertyBlock(_screenBlock);
            _screenBlock.SetColor(TintId, mode.ScreenTint);
            _screenBlock.SetColor(ScreenBackgroundId, mode.ScreenBackground);
            _screenRenderer.SetPropertyBlock(_screenBlock);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Live-preview inspector edits to the current mode.
        if (isActiveAndEnabled) ApplyMode();
    }
#endif

    private void OnDisable()
    {
        _bodyTargets.Clear();
        _skeletonTargets.Clear();
        _organTargets.Clear();
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup >= _nextRefreshTime)
            RefreshTargets();
    }

    private void RefreshTargets()
    {
        _nextRefreshTime = Time.realtimeSinceStartup + _refreshInterval;
        _bodyTargets.Clear();
        _skeletonTargets.Clear();
        _organTargets.Clear();

        int skeletonLayer = LayerMask.NameToLayer(SkeletonLayer);
        float sqrRadius = _scanRadius * _scanRadius;
        Vector3 origin = transform.position;

        var suspects = FindObjectsByType<SuspectCharacter>(FindObjectsSortMode.None);
        foreach (var suspect in suspects)
        {
            if (Application.isPlaying && _autoPlaceholderInternals && IsSuspectInRange(suspect, origin, sqrRadius))
            {
                if (!suspect.TryGetComponent(out XRayPlaceholderInternals placeholder))
                    placeholder = suspect.gameObject.AddComponent<XRayPlaceholderInternals>();
                placeholder.EnsureBuilt();
            }

            suspect.GetComponentsInChildren(false, _rendererBuffer);
            foreach (var r in _rendererBuffer)
            {
                // Root transforms can sit far from the animated mesh, so range-check the mesh itself.
                if (r.bounds.SqrDistance(origin) > sqrRadius) continue;

                int subMeshes = GetSubMeshCount(r);
                if (subMeshes == 0) continue;

                if (r.TryGetComponent(out XRayInternalPart part))
                {
                    var target = new Target { Renderer = r, SubMeshCount = subMeshes, Color = part.Color };
                    if (part.Kind == XRayInternalPart.PartKind.Organ) _organTargets.Add(target);
                    else _skeletonTargets.Add(target);
                    continue;
                }

                if (r.gameObject.layer == skeletonLayer) continue; // untagged hidden content
                _bodyTargets.Add(new Target { Renderer = r, SubMeshCount = subMeshes, Color = Color.white });
            }
        }
        _rendererBuffer.Clear();
    }

    private bool IsSuspectInRange(SuspectCharacter suspect, Vector3 origin, float sqrRadius)
    {
        suspect.GetComponentsInChildren(false, _rendererBuffer);
        foreach (var r in _rendererBuffer)
            if (r is SkinnedMeshRenderer && r.bounds.SqrDistance(origin) <= sqrRadius) return true;
        return false;
    }

    private static int GetSubMeshCount(Renderer r)
    {
        if (r is SkinnedMeshRenderer smr)
            return smr.sharedMesh != null ? smr.sharedMesh.subMeshCount : 0;
        if (r is MeshRenderer && r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
            return mf.sharedMesh.subMeshCount;
        return 0;
    }

    private void ApplyRenderer()
    {
        if (_xRayRenderer == null) return;

        var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset == null) return;

        var list = urpAsset.rendererDataList;
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] != _xRayRenderer) continue;

            var data = _camera.GetUniversalAdditionalCameraData();
            data.SetRenderer(i);
            data.renderPostProcessing = false;
            data.renderShadows = false;
            return;
        }

        Debug.LogWarning($"[XRayCamera] Renderer '{_xRayRenderer.name}' is not in the Renderer List of '{urpAsset.name}'. The X-ray screen will render with the default renderer.", this);
    }
}
