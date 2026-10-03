using UnityEngine;

/// <summary>
/// Screen-space mood applied while the local player is in a conversation with a speaker that
/// carries a <see cref="DialogueAtmosphereSource"/> pointing at this profile (e.g. Ocho's
/// "talking to the devil" pulsing vignette). Played by <see cref="DialogueAtmosphereController"/>.
/// </summary>
[CreateAssetMenu(menuName = "GoodCopBadCop/Dialogue/Dialogue Atmosphere Profile", fileName = "DialogueAtmosphereProfile")]
public class DialogueAtmosphereProfile : ScriptableObject
{
    [Header("Vignette")]
    [Tooltip("Vignette color. Near-black with a hint of red reads as menacing without looking like damage.")]
    [SerializeField] private Color vignetteColor = new Color(0.08f, 0f, 0f, 1f);

    [Tooltip("Vignette intensity at the bottom of the pulse.")]
    [SerializeField, Range(0f, 1f)] private float minIntensity = 0.28f;

    [Tooltip("Vignette intensity at the top of the pulse.")]
    [SerializeField, Range(0f, 1f)] private float maxIntensity = 0.42f;

    [Tooltip("Higher = softer edge that bleeds further inward.")]
    [SerializeField, Range(0.01f, 1f)] private float smoothness = 0.55f;

    [Header("Pulse")]
    [Tooltip("Seconds per full pulse (low -> high -> low).")]
    [SerializeField, Min(0.1f)] private float pulsePeriod = 2.4f;

    [Tooltip("0 = smooth sine breathing. 1 = sharp heartbeat-like throb that snaps up and eases back down.")]
    [SerializeField, Range(0f, 1f)] private float pulseSharpness = 0.35f;

    [Header("Extras (0 = untouched)")]
    [Tooltip("URP Color Adjustments saturation while active. Negative drains color.")]
    [SerializeField, Range(-100f, 0f)] private float saturation;

    [Header("Fade")]
    [SerializeField, Min(0f)] private float fadeInDuration = 0.8f;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.6f;

    public Color VignetteColor => vignetteColor;
    public float MinIntensity => minIntensity;
    public float MaxIntensity => Mathf.Max(minIntensity, maxIntensity);
    public float Smoothness => smoothness;
    public float PulsePeriod => pulsePeriod;
    public float PulseSharpness => pulseSharpness;
    public float Saturation => saturation;
    public float FadeInDuration => fadeInDuration;
    public float FadeOutDuration => fadeOutDuration;

    /// <summary>Pulse value in [0,1] at <paramref name="time"/> seconds.</summary>
    public float EvaluatePulse(float time)
    {
        float phase = Mathf.Repeat(time / pulsePeriod, 1f);
        float sine = 0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f);
        // Sharpen toward a throb: fast rise, longer decay.
        float throb = phase < 0.2f ? Mathf.SmoothStep(0f, 1f, phase / 0.2f) : Mathf.Pow(1f - (phase - 0.2f) / 0.8f, 2f);
        return Mathf.Lerp(sine, throb, pulseSharpness);
    }
}
