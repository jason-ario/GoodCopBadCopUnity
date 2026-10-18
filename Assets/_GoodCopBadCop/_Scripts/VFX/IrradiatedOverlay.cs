using GoodCopBadCop.Effects;
using UnityEngine;

/// <summary>
/// Drives the sustained full-screen radiation effect (<see cref="GlitchEffectView"/>) for the local player:
/// the hurt effect's look (colored vignette, desaturation) in yellow. Glitching is supplied separately by
/// <c>GlitchController</c> (the mutant glitch volume), which follows <see cref="LocalTargetIntensity"/>.
/// Intensity is the stronger of two signals, smoothed so it builds and fades instead of popping:
///   - Exposure: current gain rate (<see cref="PlayerRadiation.RadiationRate"/>) — the primary "being irradiated"
///     signal. Reference rates: passive ~0.15/s, hotspots 0.1-0.5/s, off-trail ~2.5/s, irradiated water 5/s.
///   - Accrued: radiation level above <see cref="accruedStartThreshold"/> (sickness), so the effect lingers
///     while the bar is high even after leaving the source.
/// </summary>
public class IrradiatedOverlay : MonoBehaviour
{
    [Header("Exposure (gain rate, units/sec)")]
    [Tooltip("Gain rate at which the effect starts. Keep above passive gain (~0.15/s).")]
    [SerializeField] private float exposureRateStart = 0.2f;
    [Tooltip("Gain rate at which the exposure signal reaches full strength.")]
    [SerializeField] private float exposureRateFull = 2.5f;
    [Tooltip("Strength produced by the weakest noticeable exposure (just above the start rate), so small hotspots still read.")]
    [SerializeField, Range(0f, 1f)] private float exposureMinIntensity = 0.35f;

    [Header("Accrued Radiation")]
    [Tooltip("Normalized radiation (0-1) at which the sickness signal starts. Reaches full at 1.")]
    [SerializeField, Range(0f, 1f)] private float accruedStartThreshold = 0.5f;
    [Tooltip("Strength of the sickness signal at 100% radiation.")]
    [SerializeField, Range(0f, 1f)] private float accruedMaxIntensity = 0.8f;

    [Header("Look (values at full intensity — defaults mirror the hurt effect)")]
    [SerializeField] private Color tint = new Color(1f, 0.86f, 0.15f, 1f);
    [SerializeField, Range(0f, 1f)] private float vignetteIntensity = 0.5f;
    [SerializeField, Range(0.01f, 1f)] private float vignetteSmoothness = 0.75f;
    [Tooltip("0 = off (default).")]
    [SerializeField, Range(0f, 1f)] private float chromaticAberration = 0f;
    [Tooltip("0 = untouched, 1 = greyscale. Hurt uses -20 saturation (0.2).")]
    [SerializeField, Range(0f, 1f)] private float desaturate = 0.2f;
    [Tooltip("Additive yellow lift at the edges so the tint reads in dark scenes.")]
    [SerializeField, Range(0f, 1f)] private float glowStrength = 0.35f;

    [Header("Smoothing")]
    [SerializeField] private float fadeInSpeed = 4f;
    [SerializeField] private float fadeOutSpeed = 1.2f;

    private GlitchEffectView view;
    private float currentIntensity;
    private bool lookDirty = true;

    /// <summary>Current rendered intensity (0-1), for debugging.</summary>
    public float CurrentIntensity => currentIntensity;

    /// <summary>
    /// Unsmoothed 0-1 radiation signal for the local player, computed by the active overlay each frame.
    /// <c>GlitchController</c> reads this so the mutant glitch accompanies the radiation vignette.
    /// </summary>
    public static float LocalTargetIntensity { get; private set; }

    private void Update()
    {
        float target = ComputeTargetIntensity();
        LocalTargetIntensity = target;

        float speed = target > currentIntensity ? fadeInSpeed : fadeOutSpeed;
        currentIntensity = Mathf.Lerp(currentIntensity, target, 1f - Mathf.Exp(-speed * Time.deltaTime));
        if (target <= 0f && currentIntensity < 0.002f)
            currentIntensity = 0f;

        if (view == null)
        {
            if (currentIntensity <= 0f)
                return;

            view = GlitchEffectView.GetOrCreate();
            lookDirty = true;
        }

        if (lookDirty)
        {
            view.SetLook(tint, vignetteIntensity, vignetteSmoothness, chromaticAberration,
                desaturate, glowStrength);
            lookDirty = false;
        }

        view.SetSustainedIntensity(currentIntensity);
    }

    private float ComputeTargetIntensity()
    {
        PlayerInstance player = PlayerInstance.Instance;
        PlayerRadiation radiation = player != null ? player.PlayerRadiation : null;
        if (radiation == null || !radiation.IsActivePlayer)
            return 0f;

        float exposure = 0f;
        float rate = radiation.RadiationRate;
        if (rate > exposureRateStart)
        {
            float t = Mathf.InverseLerp(exposureRateStart, exposureRateFull, rate);
            exposure = Mathf.Lerp(exposureMinIntensity, 1f, t);
        }

        float accrued = Mathf.InverseLerp(accruedStartThreshold, 1f, radiation.Normalized) * accruedMaxIntensity;

        return Mathf.Max(exposure, accrued);
    }

    private void OnValidate()
    {
        lookDirty = true;
    }

    private void OnDisable()
    {
        currentIntensity = 0f;
        LocalTargetIntensity = 0f;
        if (view != null)
            view.SetSustainedIntensity(0f);
    }
}
