using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Client-visual controller that pulses a full-screen red Color Adjustments post-processing
/// override while a mutant breach alarm is active, then restores the profile's original values.
/// Uses the exact same Time.time-based pulse formula as <see cref="AlarmLightController"/> so the
/// screen flash and the physical alarm lights stay in sync. Driven entirely by
/// <see cref="MutantBreachManager"/> via ClientRpc — this component has no networking of its own
/// and should never be triggered directly except for local testing.
/// </summary>
public class PostProcessAlarmController : MonoBehaviour
{
    [Header("Alert Volume")]
    [Tooltip("Global Volume whose profile's Color Adjustments override gets pulsed red while the alarm is active.")]
    [SerializeField] private Volume alertVolume;

    [Header("Pulse")]
    [Tooltip("Color the screen tints toward at the peak of each pulse.")]
    [SerializeField] private Color alarmColor = Color.red;

    [Tooltip("Pulses per second. Match AlarmLightController.pulseSpeed on the same breach so the screen flash and the physical alarm lights stay in sync.")]
    [SerializeField] private float pulseSpeed = 1.3f;

    [Tooltip("Minimum blend strength of the red tint pulse (0 = no tint).")]
    [SerializeField, Range(0f, 1f)] private float minWeight = 0f;

    [Tooltip("Maximum blend strength of the red tint pulse (1 = fully replaces the image color with alarmColor).")]
    [SerializeField, Range(0f, 1f)] private float maxWeight = 0.85f;

    [Tooltip("Extra exposure boost applied at the peak of each pulse, so the red reads clearly even over dark scenery.")]
    [SerializeField] private float peakPostExposure = 0.5f;

    private ColorAdjustments _colorAdjustments;
    private VolumeProfile _profile;
    private bool _originalActive;
    private Color _originalColorFilter;
    private float _originalPostExposure;
    private Coroutine _pulseCoroutine;

    private void Awake()
    {
        if (alertVolume == null)
        {
            Debug.LogWarning("[PostProcessAlarmController] No alertVolume assigned — screen flash disabled.", this);
            return;
        }

        // The Alert Volume GameObject starts disabled in the scene; keep it enabled at all times
        // (weight 0 = no visual effect) so this controller's coroutines can run when triggered.
        alertVolume.gameObject.SetActive(true);
        alertVolume.weight = 0f;

        _profile = alertVolume.profile != null ? alertVolume.profile : alertVolume.sharedProfile;
        if (_profile == null || !_profile.TryGet(out _colorAdjustments))
            Debug.LogWarning("[PostProcessAlarmController] Alert Volume's profile has no Color Adjustments override — screen flash disabled.", this);
    }

    /// <summary>Starts the red pulsing screen flash. Safe to call if already running.</summary>
    public void StartAlarm()
    {
        if (_pulseCoroutine != null || _colorAdjustments == null || alertVolume == null)
            return;

        CacheOriginalState();
        alertVolume.weight = 1f;
        _pulseCoroutine = StartCoroutine(PulseLoop());
    }

    /// <summary>Stops the pulsing loop, restores the profile's original values, and zeroes the volume weight.</summary>
    public void StopAlarm()
    {
        if (_pulseCoroutine != null)
        {
            StopCoroutine(_pulseCoroutine);
            _pulseCoroutine = null;
        }

        RestoreOriginalState();

        if (alertVolume != null)
            alertVolume.weight = 0f;
    }

    private void CacheOriginalState()
    {
        _originalActive = _colorAdjustments.active;
        _originalColorFilter = _colorAdjustments.colorFilter.value;
        _originalPostExposure = _colorAdjustments.postExposure.value;

        _colorAdjustments.active = true;
        _colorAdjustments.colorFilter.overrideState = true;
        _colorAdjustments.postExposure.overrideState = true;
    }

    private void RestoreOriginalState()
    {
        if (_colorAdjustments == null) return;

        _colorAdjustments.active = _originalActive;
        _colorAdjustments.colorFilter.value = _originalColorFilter;
        _colorAdjustments.postExposure.value = _originalPostExposure;
    }

    private IEnumerator PulseLoop()
    {
        while (true)
        {
            // Same PingPong(Time.time * pulseSpeed, 1f) formula as AlarmLightController.PulseLoop —
            // keeps the screen flash in lockstep with the physical alarm lights.
            float t = Mathf.PingPong(Time.time * pulseSpeed, 1f);
            float weight = Mathf.Lerp(minWeight, maxWeight, t);

            _colorAdjustments.colorFilter.value = Color.Lerp(_originalColorFilter, alarmColor, weight);
            _colorAdjustments.postExposure.value = Mathf.Lerp(_originalPostExposure, peakPostExposure, weight);

            yield return null;
        }
    }
}
