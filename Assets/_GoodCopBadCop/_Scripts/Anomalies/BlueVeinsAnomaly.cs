using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Reveals a vein texture on character renderers wherever any active UVLight's
/// world-space sphere overlaps them. UVLight components self-register into a shared
/// static list, so no direct references are needed here — the flashlight just enables
/// or disables its UVLight component to participate.
///
/// Up to UV_LIGHT_MAX_COUNT (4) simultaneous lights are supported, matching the
/// fixed-size array declared in the Character Shader.
///
/// Supports editor-time preview via the 'Preview In Editor' toggle. In edit mode,
/// keywords are toggled on the shared material and cleaned up on disable.
/// </summary>
[ExecuteAlways]
public class BlueVeinsAnomaly : MutationAnomaly
{
    private const string BlueVeinsKeyword  = "TCP2_BLUE_VEINS";
    private const int    MaxLights         = 4; // Must match UV_LIGHT_MAX_COUNT in the shader.

    private static readonly int UVLightPositionsId = Shader.PropertyToID("_UVLightPositions");
    private static readonly int UVLightRadiiId     = Shader.PropertyToID("_UVLightRadii");
    private static readonly int UVLightCountId     = Shader.PropertyToID("_UVLightCount");

    [Header("Renderers")]
    [SerializeField] private Renderer[] renderers;

    [Header("Editor Preview")]
    [Tooltip("Enable to preview the UV light reveal in the Scene view without entering Play mode.")]
    [SerializeField] private bool _previewInEditor;

    // Reusable arrays — allocated once to avoid per-frame GC pressure.
    private readonly Vector4[] _positionBuffer = new Vector4[MaxLights];
    private readonly float[]   _radiusBuffer   = new float[MaxLights];

    // Runtime-only: per-instance materials for keyword isolation between characters.
    private Material[]         _materialInstances;
    private MaterialPropertyBlock _propertyBlock;
    private bool _isActive;

    private void OnEnable()
    {
        if (Application.isPlaying)
            InitializeRuntime();
        else
        {
            _propertyBlock ??= new MaterialPropertyBlock();
            SetSharedKeywords(_previewInEditor);
        }
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
            SetSharedKeywords(false);
    }

    private void OnValidate()
    {
        if (Application.isPlaying) return;

        _propertyBlock ??= new MaterialPropertyBlock();
        SetSharedKeywords(_previewInEditor);
    }

    /// <summary>Enables the blue veins keyword on all renderers and begins tracking active UV lights.</summary>
    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        if (_materialInstances == null) return;

        foreach (Material mat in _materialInstances)
            mat?.EnableKeyword(BlueVeinsKeyword);

        _isActive = true;
    }

    /// <summary>Stops tracking UV lights and disables the blue veins keyword on all renderers.</summary>
    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();

        _isActive = false;

        if (_materialInstances == null) return;

        foreach (Material mat in _materialInstances)
            mat?.DisableKeyword(BlueVeinsKeyword);
    }

    private void Update()
    {
        if (Application.isPlaying)
        {
            if (!_isActive || renderers == null) return;
        }
        else
        {
            if (!_previewInEditor || renderers == null) return;
            _propertyBlock ??= new MaterialPropertyBlock();

#if UNITY_EDITOR
            EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        PushActiveLights();
    }

    private void InitializeRuntime()
    {
        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogWarning($"[BlueVeinsAnomaly] No renderers assigned on '{gameObject.name}'. Anomaly will not function.", this);
            return;
        }

        _materialInstances = new Material[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                _materialInstances[i] = renderers[i].material;
            else
                Debug.LogWarning($"[BlueVeinsAnomaly] Renderer at index {i} is null on '{gameObject.name}'.", this);
        }

        _propertyBlock = new MaterialPropertyBlock();
    }

    /// <summary>
    /// Reads all active UVLights (capped at MaxLights), fills the position and radius
    /// buffers, and pushes them to every renderer via a shared MaterialPropertyBlock.
    /// </summary>
    private void PushActiveLights()
    {
        List<UVLight> lights = UVLight.ActiveLights;
        int count = Mathf.Min(lights.Count, MaxLights);

        for (int i = 0; i < count; i++)
        {
            _positionBuffer[i] = lights[i].Position;
            _radiusBuffer[i]   = lights[i].Radius;
        }

        // Zero out any unused slots so stale data doesn't bleed through.
        for (int i = count; i < MaxLights; i++)
        {
            _positionBuffer[i] = Vector4.zero;
            _radiusBuffer[i]   = 0f;
        }

        _propertyBlock.SetVectorArray(UVLightPositionsId, _positionBuffer);
        _propertyBlock.SetFloatArray(UVLightRadiiId, _radiusBuffer);
        _propertyBlock.SetInteger(UVLightCountId, count);

        foreach (Renderer r in renderers)
            r?.SetPropertyBlock(_propertyBlock);
    }

    /// <summary>
    /// Toggles TCP2_BLUE_VEINS on each renderer's shared materials. Used in edit mode only.
    /// OnDisable ensures the keyword is cleaned up when preview is turned off.
    /// </summary>
    private void SetSharedKeywords(bool enable)
    {
        if (renderers == null) return;

        foreach (Renderer r in renderers)
        {
            if (r == null) continue;

            foreach (Material mat in r.sharedMaterials)
            {
                if (mat == null) continue;

                if (enable) mat.EnableKeyword(BlueVeinsKeyword);
                else        mat.DisableKeyword(BlueVeinsKeyword);
            }
        }
    }
}
