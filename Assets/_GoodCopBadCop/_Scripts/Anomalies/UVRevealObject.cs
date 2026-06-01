using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Drives the UVReveal shader on one or more Renderers, revealing the hidden surface
/// wherever any active UVLight's world-space cone overlaps the mesh.
///
/// Each UVLight contributes: a position (xyz) + range (w) packed into _UVLightPositions,
/// a normalized forward direction in _UVLightDirections, and the cosine of its half-angle
/// in _UVLightParams.x — all consumed by UVReveal.shader to reconstruct the cone in the
/// fragment stage.
///
/// Up to UV_LIGHT_MAX_COUNT (4) simultaneous lights are supported, matching the constant
/// in UVReveal.shader.
/// </summary>
[ExecuteAlways]
public class UVRevealObject : MonoBehaviour
{
    private const int MaxLights = 4; // Must match UV_LIGHT_MAX_COUNT in UVReveal.shader.

    private static readonly int UVLightPositionsId  = Shader.PropertyToID("_UVLightPositions");
    private static readonly int UVLightDirectionsId = Shader.PropertyToID("_UVLightDirections");
    private static readonly int UVLightParamsId     = Shader.PropertyToID("_UVLightParams");
    private static readonly int UVLightCountId      = Shader.PropertyToID("_UVLightCount");

    [Header("Renderers")]
    [Tooltip("All Renderers that use the UVReveal shader and should be driven by this component.")]
    [SerializeField] private Renderer[] renderers;

    [Header("Editor Preview")]
    [Tooltip("Preview UV light reveal in the Scene view without entering Play mode.")]
    [SerializeField] private bool _previewInEditor;

    // Reusable arrays allocated once to avoid per-frame GC pressure.
    // _positionBuffer: xyz = world position, w = range.
    // _directionBuffer: xyz = normalized forward direction.
    // _paramsBuffer: x = cos(halfAngleDeg).
    private readonly Vector4[] _positionBuffer  = new Vector4[MaxLights];
    private readonly Vector4[] _directionBuffer = new Vector4[MaxLights];
    private readonly Vector4[] _paramsBuffer    = new Vector4[MaxLights];

    private MaterialPropertyBlock _propertyBlock;

    private void OnEnable()
    {
        _propertyBlock ??= new MaterialPropertyBlock();
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
            PushLights(0); // zero out so no reveal bleeds when disabled in editor
    }

    private void OnValidate()
    {
        if (Application.isPlaying) return;
        _propertyBlock ??= new MaterialPropertyBlock();
    }

    private void Update()
    {
        if (!Application.isPlaying && !_previewInEditor) return;
        if (renderers == null || renderers.Length == 0) return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorApplication.QueuePlayerLoopUpdate();
#endif

        List<UVLight> lights = UVLight.ActiveLights;
        int count = Mathf.Min(lights.Count, MaxLights);

        for (int i = 0; i < count; i++)
        {
            UVLight light = lights[i];

            _positionBuffer[i]  = new Vector4(light.Position.x, light.Position.y, light.Position.z, light.Range);
            _directionBuffer[i] = new Vector4(light.Direction.x, light.Direction.y, light.Direction.z, 0f);
            _paramsBuffer[i]    = new Vector4(Mathf.Cos(light.ConeHalfAngleDeg * Mathf.Deg2Rad), 0f, 0f, 0f);
        }

        PushLights(count);
    }

    /// <summary>
    /// Pushes position, direction, and cone parameter data for the given number of active
    /// lights to all renderers via the shared MaterialPropertyBlock. Slots beyond count
    /// are zeroed so stale data doesn't bleed through.
    /// </summary>
    private void PushLights(int count)
    {
        if (renderers == null || renderers.Length == 0) return;

        for (int i = count; i < MaxLights; i++)
        {
            _positionBuffer[i]  = Vector4.zero;
            _directionBuffer[i] = Vector4.zero;
            _paramsBuffer[i]    = Vector4.zero;
        }

        _propertyBlock ??= new MaterialPropertyBlock();
        _propertyBlock.SetVectorArray(UVLightPositionsId,  _positionBuffer);
        _propertyBlock.SetVectorArray(UVLightDirectionsId, _directionBuffer);
        _propertyBlock.SetVectorArray(UVLightParamsId,     _paramsBuffer);
        _propertyBlock.SetInteger(UVLightCountId,          count);

        foreach (Renderer r in renderers)
        {
            if (r != null)
                r.SetPropertyBlock(_propertyBlock);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (renderers == null) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        foreach (Renderer r in renderers)
        {
            if (r != null)
                Gizmos.DrawWireCube(r.bounds.center, r.bounds.size);
        }
    }
#endif
}
