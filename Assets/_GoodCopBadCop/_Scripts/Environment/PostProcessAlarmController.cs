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

    [Tooltip("Extra exposure boost (EV, added on top of the player's Brightness setting) applied at the peak of each pulse, so the red reads clearly even over dark scenery.")]
    [SerializeField] private float peakPostExposure = 0.5f;

    [Header("Brightness Source")]
    [Tooltip("Base volume that carries the player's Brightness preference. Auto-found in the scene if left empty.")]
    [SerializeField] private GoodCopBadCop.Settings.GraphicsPreferencesVolumeAnchor brightnessVolumeAnchor;

    private ColorAdjustments _colorAdjustments;
    private ColorAdjustments _brightnessAdjustments;
    private VolumeProfile _profile;
    private bool _originalActive;
    private bool _originalPostExposureOverride;
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
        _originalPostExposureOverride = _colorAdjustments.postExposure.overrideState;
        _originalColorFilter = _colorAdjustments.colorFilter.value;
        _originalPostExposure = _colorAdjustments.postExposure.value;

        ResolveBrightnessAdjustments();

        _colorAdjustments.active = true;
        _colorAdjustments.colorFilter.overrideState = true;
        _colorAdjustments.postExposure.overrideState = true;
    }

    private void RestoreOriginalState()
    {
        if (_colorAdjustments == null) return;

        _colorAdjustments.active = _originalActive;
        _colorAdjustments.postExposure.overrideState = _originalPostExposureOverride;
        _colorAdjustments.colorFilter.value = _originalColorFilter;
        _colorAdjustments.postExposure.value = _originalPostExposure;
    }

    /// <summary>
    /// Finds the ColorAdjustments override on the base volume that GraphicsPreferencesApplier writes
    /// the Brightness preference into. The alert volume's postExposure override replaces (not adds to)
    /// that value while active, so the pulse must be expressed relative to it.
    /// </summary>
    private void ResolveBrightnessAdjustments()
    {
        if (_brightnessAdjustments != null) return;

        if (brightnessVolumeAnchor == null)
            brightnessVolumeAnchor = FindAnyObjectByType<GoodCopBadCop.Settings.GraphicsPreferencesVolumeAnchor>();

        Volume baseVolume = brightnessVolumeAnchor != null ? brightnessVolumeAnchor.Volume : null;
        if (baseVolume == null || baseVolume == alertVolume) return;

        // Use the instanced profile (same one GraphicsPreferencesApplier modifies at runtime).
        VolumeProfile baseProfile = baseVolume.profile;
        if (baseProfile != null)
            baseProfile.TryGet(out _brightnessAdjustments);
    }

    private float GetBrightnessExposure()
    {
        if (_brightnessAdjustments != null && _brightnessAdjustments.active && _brightnessAdjustments.postExposure.overrideState)
            return _brightnessAdjustments.postExposure.value;
        return _originalPostExposure;
    }

    private IEnumerator PulseLoop()
    {
        while (true)
        {
            // Same PingPong(Time.time * pulseSpeed, 1f) formula as AlarmLightController.PulseLoop —
            // keeps the screen flash in lockstep with the physical alarm lights.
            float t = Mathf.PingPong(Time.time * pulseSpeed, 1f);
            float weight = Mathf.Lerp(minWeight, maxWeight, t);

            // Sampled every frame so a Brightness change mid-alarm is respected.
            float baseExposure = GetBrightnessExposure();

            _colorAdjustments.colorFilter.value = Color.Lerp(_originalColorFilter, alarmColor, weight);
            _colorAdjustments.postExposure.value = Mathf.Lerp(baseExposure, baseExposure + peakPostExposure, weight);

            yield return null;
        }
    }
}
