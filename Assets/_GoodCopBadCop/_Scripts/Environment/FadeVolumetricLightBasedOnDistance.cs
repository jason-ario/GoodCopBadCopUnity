using UnityEngine;
using VolumetricLights;

/// <summary>
/// Dims a VolumetricLight's brightness based on the distance between this
/// GameObject and the target camera. At or below <see cref="minDistance"/>
/// the light renders at its original brightness; at or above
/// <see cref="maxDistance"/> it uses <see cref="minBrightnessMultiplier"/>.
/// The transition shape is controlled by <see cref="falloffCurve"/>.
/// </summary>
[RequireComponent(typeof(VolumetricLight))]
public class FadeVolumetricLightBasedOnDistance : MonoBehaviour
{
    [Header("Distance Settings")]
    [Tooltip("Distance from the camera at which the light starts dimming.")]
    [SerializeField] private float minDistance = 10f;

    [Tooltip("Distance from the camera at which the light reaches its minimum brightness.")]
    [SerializeField] private float maxDistance = 40f;

    [Header("Brightness Settings")]
    [Tooltip("Minimum brightness multiplier applied at maxDistance (0 = fully invisible).")]
    [Range(0f, 1f)]
    [SerializeField] private float minBrightnessMultiplier = 0f;

    [Tooltip("Curve controlling the brightness falloff. X = normalized distance (0–1), Y = brightness multiplier (0–1).")]
    [SerializeField] private AnimationCurve falloffCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Target")]
    [Tooltip("How often (in seconds) to poll for Camera.main.")]
    [SerializeField] private float cameraPollInterval = 1f;

    private VolumetricLight _volumetricLight;
    private float _originalBrightness;
    private Camera _targetCamera;
    private float _nextPollTime;

    private void Awake()
    {
        _volumetricLight = GetComponent<VolumetricLight>();
        _originalBrightness = _volumetricLight.brightness;
        PollForCamera();
    }

    private void PollForCamera()
    {
        _targetCamera = Camera.main;
        _nextPollTime = Time.time + cameraPollInterval;
    }

    private void Update()
    {
        if (Time.time >= _nextPollTime)
        {
            PollForCamera();
        }

        if (_targetCamera == null)
        {
            return;
        }

        float distance = Vector3.Distance(transform.position, _targetCamera.transform.position);
        float normalizedDistance = Mathf.InverseLerp(minDistance, maxDistance, distance);
        float curveValue = falloffCurve.Evaluate(normalizedDistance);
        float brightnessMultiplier = Mathf.Lerp(1f, minBrightnessMultiplier, 1f - curveValue);

        _volumetricLight.brightness = _originalBrightness * brightnessMultiplier;
    }

    private void OnDisable()
    {
        if (_volumetricLight != null)
        {
            _volumetricLight.brightness = _originalBrightness;
        }
    }
}
