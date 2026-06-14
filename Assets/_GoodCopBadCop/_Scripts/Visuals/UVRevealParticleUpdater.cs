using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Updates the MaterialPropertyBlock of a ParticleSystemRenderer to drive the UVReveal shader.
/// Allows trail particles to be hidden by default and only visible within UV light cones.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
[ExecuteAlways]
public class UVRevealParticleUpdater : MonoBehaviour
{
    private const int MaxLights = 4; // Must match UV_LIGHT_MAX_COUNT in UVReveal.shader.

    private static readonly int UVLightPositionsId  = Shader.PropertyToID("_UVLightPositions");
    private static readonly int UVLightDirectionsId = Shader.PropertyToID("_UVLightDirections");
    private static readonly int UVLightParamsId     = Shader.PropertyToID("_UVLightParams");
    private static readonly int UVLightCountId      = Shader.PropertyToID("_UVLightCount");

    [Header("Editor Preview")]
    [Tooltip("Preview UV light reveal in the Scene view without entering Play mode.")]
    [SerializeField] private bool _previewInEditor;

    private ParticleSystemRenderer _particleRenderer;
    private MaterialPropertyBlock _propertyBlock;

    private readonly Vector4[] _positionBuffer  = new Vector4[MaxLights];
    private readonly Vector4[] _directionBuffer = new Vector4[MaxLights];
    private readonly Vector4[] _paramsBuffer    = new Vector4[MaxLights];

    private void Awake()
    {
        _particleRenderer = GetComponent<ParticleSystemRenderer>();
    }

    private void OnEnable()
    {
        _propertyBlock ??= new MaterialPropertyBlock();
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
            PushLights(0);
    }

    private void Update()
    {
        if (!Application.isPlaying && !_previewInEditor) return;
        if (_particleRenderer == null)
        {
            _particleRenderer = GetComponent<ParticleSystemRenderer>();
            if (_particleRenderer == null) return;
        }

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

    private void PushLights(int count)
    {
        if (_particleRenderer == null) return;

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

        _particleRenderer.SetPropertyBlock(_propertyBlock);
    }
}
