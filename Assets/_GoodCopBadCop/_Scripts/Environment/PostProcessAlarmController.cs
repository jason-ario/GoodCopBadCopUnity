using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Client-visual controller that pulses a red screen-edge Vignette on the Alert Volume while a
/// mutant breach alarm is active, then fades it back out.
/// Deliberately does NOT touch Color Adjustments (color filter / post exposure): URP volume blending
/// replaces overridden values, so tinting the full frame wiped out the base grade and the green fog.
/// Only the vignette's color/intensity/smoothness are overridden, pulsing from the base volume's own
/// vignette values so the rest of the look is untouched.
/// Uses the same Time.time-based pulse formula as <see cref="AlarmLightController"/> so the screen
/// pulse and the physical alarm lights stay in sync. Driven entirely by <see cref="MutantBreachManager"/>
/// via ClientRpc — this component has no networking of its own.
/// </summary>
public class PostProcessAlarmController : MonoBehaviour
{
    [Header("Alert Volume")]
    [Tooltip("Global Volume used for the alarm overrides. A Vignette override is added to its runtime profile if missing.")]
    [SerializeField] private Volume alertVolume;

    [Header("Pulse")]
    [Tooltip("Vignette color at the peak of each pulse.")]
    [SerializeField] private Color alarmColor = new Color(0.85f, 0.05f, 0.05f, 1f);

    [Tooltip("Pulses per second. Match AlarmLightController.pulseSpeed so the screen pulse and alarm lights stay in sync.")]
    [SerializeField] private float pulseSpeed = 1.3f;

    [Tooltip("Vignette intensity at the peak of each pulse (the trough is the base volume's own vignette intensity).")]
    [SerializeField, Range(0f, 1f)] private float peakVignetteIntensity = 0.45f;

    [Tooltip("Vignette smoothness at the peak of each pulse. Higher = softer edge that bleeds further inward.")]
    [SerializeField, Range(0.01f, 1f)] private float peakVignetteSmoothness = 0.6f;

    [Tooltip("Seconds to fade the vignette out when the alarm stops.")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.75f;

    [Header("Base Look Source")]
    [Tooltip("Base volume whose vignette the pulse starts from. Auto-found in the scene if left empty.")]
    [SerializeField] private GoodCopBadCop.Settings.GraphicsPreferencesVolumeAnchor baseVolumeAnchor;

    private Vignette _alarmVignette;
    private Coroutine _routine;

    private void Awake()
    {
        if (alertVolume == null)
        {
            Debug.LogWarning("[PostProcessAlarmController] No alertVolume assigned — alarm vignette disabled.", this);
            return;
        }

        // The Alert Volume GameObject starts disabled in the scene; keep it enabled (weight 0 = no effect).
        alertVolume.gameObject.SetActive(true);
        alertVolume.weight = 0f;

        // Runtime instance, so edits never touch the profile asset.
        VolumeProfile profile = alertVolume.profile;
        if (profile == null)
        {
            Debug.LogWarning("[PostProcessAlarmController] Alert Volume has no profile — alarm vignette disabled.", this);
            return;
        }

        // Never let the alert volume override the base color grade (this is what killed the green fog).
        if (profile.TryGet(out ColorAdjustments colorAdjustments))
            colorAdjustments.active = false;

        if (!profile.TryGet(out _alarmVignette))
            _alarmVignette = profile.Add<Vignette>();

        _alarmVignette.active = true;
        _alarmVignette.color.overrideState = true;
        _alarmVignette.intensity.overrideState = true;
        _alarmVignette.smoothness.overrideState = true;
    }

    /// <summary>Starts the pulsing red vignette. Safe to call if already running.</summary>
    public void StartAlarm()
    {
        if (_alarmVignette == null || alertVolume == null)
            return;

        if (_routine != null)
            StopCoroutine(_routine);

        EnsureAlertVolumeWinsTies();
        alertVolume.weight = 1f;
        _routine = StartCoroutine(PulseLoop());
    }

    /// <summary>Stops the pulse and fades the vignette back to the base look.</summary>
    public void StopAlarm()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        if (alertVolume == null) return;

        if (isActiveAndEnabled && fadeOutDuration > 0f && alertVolume.weight > 0f)
            _routine = StartCoroutine(FadeOut());
        else
            alertVolume.weight = 0f;
    }

    private void OnDisable()
    {
        _routine = null;
        if (alertVolume != null)
            alertVolume.weight = 0f;
    }

    private Volume ResolveBaseVolume()
    {
        if (baseVolumeAnchor == null)
            baseVolumeAnchor = FindAnyObjectByType<GoodCopBadCop.Settings.GraphicsPreferencesVolumeAnchor>();

        Volume baseVolume = baseVolumeAnchor != null ? baseVolumeAnchor.Volume : null;
        return baseVolume == alertVolume ? null : baseVolume;
    }

    /// <summary>
    /// Both volumes are global; at equal priority URP's tie-break order is not deterministic across
    /// game instances, so make sure the alert vignette always layers on top of the base one.
    /// </summary>
    private void EnsureAlertVolumeWinsTies()
    {
        Volume baseVolume = ResolveBaseVolume();
        if (baseVolume != null && alertVolume.priority <= baseVolume.priority)
            alertVolume.priority = baseVolume.priority + 1f;
    }

    private void GetBaseVignette(out Color color, out float intensity, out float smoothness)
    {
        color = Color.black;
        intensity = 0f;
        smoothness = 0.2f;

        Volume baseVolume = ResolveBaseVolume();
        if (baseVolume == null) return;

        VolumeProfile baseProfile = baseVolume.HasInstantiatedProfile() ? baseVolume.profile : baseVolume.sharedProfile;
        if (baseProfile == null || !baseProfile.TryGet(out Vignette baseVignette) || !baseVignette.active)
            return;

        if (baseVignette.color.overrideState) color = baseVignette.color.value;
        if (baseVignette.intensity.overrideState) intensity = baseVignette.intensity.value;
        if (baseVignette.smoothness.overrideState) smoothness = baseVignette.smoothness.value;
    }

    private void ApplyPulse(float t)
    {
        GetBaseVignette(out Color baseColor, out float baseIntensity, out float baseSmoothness);

        _alarmVignette.color.value = Color.Lerp(baseColor, alarmColor, t);
        _alarmVignette.intensity.value = Mathf.Lerp(baseIntensity, Mathf.Max(baseIntensity, peakVignetteIntensity), t);
        _alarmVignette.smoothness.value = Mathf.Lerp(baseSmoothness, peakVignetteSmoothness, t);
    }

    private IEnumerator PulseLoop()
    {
        while (true)
        {
            // Same PingPong(Time.time * pulseSpeed, 1f) formula as AlarmLightController.PulseLoop.
            float t = Mathf.PingPong(Time.time * pulseSpeed, 1f);
            // Smoothstep softens the turnaround at each end so the pulse reads as a throb, not a strobe.
            ApplyPulse(t * t * (3f - 2f * t));
            yield return null;
        }
    }

    private IEnumerator FadeOut()
    {
        float startWeight = alertVolume.weight;
        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            alertVolume.weight = Mathf.Lerp(startWeight, 0f, elapsed / fadeOutDuration);
            yield return null;
        }

        alertVolume.weight = 0f;
        _routine = null;
    }
}
