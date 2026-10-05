using UnityEngine;

/// <summary>
/// Drives the full-screen radiation shader overlay for the local HUD player.
/// Intensity is the stronger of two signals:
///   - Accrued: radiation level above <see cref="effectStartThreshold"/> (sickness).
///   - Exposure: current gain rate (<see cref="PlayerRadiation.RadiationRate"/>), so walking off
///     the safe trail or into a hotspot shows feedback immediately, not only once the bar is high.
/// </summary>
public class IrradiatedOverlay : MonoBehaviour
{
    [SerializeField] private PlayerRadiation playerRadiation;

    [Header("Shader Material")]
    [SerializeField] private Material radiationMaterial;

    [Header("Activation")]
    [SerializeField] private float effectStartThreshold = 0.75f;

    [Header("Exposure (gain rate, units/sec)")]
    [Tooltip("Radiation gain rate at which the exposure signal starts contributing. Passive gain is ~0.15/s.")]
    [SerializeField] private float exposureRateStart = 1f;
    [Tooltip("Radiation gain rate at which the exposure signal reaches its cap.")]
    [SerializeField] private float exposureRateFull = 4f;
    [Tooltip("Maximum overlay intensity the exposure signal alone can produce.")]
    [SerializeField, Range(0f, 1f)] private float maxExposureIntensity = 0.5f;

    [Header("Intensity")]
    [SerializeField] private float maxNoiseAmount = 0.08f;
    [SerializeField] private float maxDistortionAmount = 0.015f;
    [SerializeField] private float maxOpacityMultiply = 0.7f;

    [Header("Smoothing")]
    [SerializeField] private float smoothSpeed = 4f;

    private float currentIntensity;

    private static readonly int RadiationIntensityID = Shader.PropertyToID("_OpacityMultiply");

    private void Update()
    {
        if (playerRadiation == null)
        {
            if (PlayerInstance.Instance != null)
            {
                playerRadiation = PlayerInstance.Instance.PlayerRadiation;
            }

            return;
        }

        float accruedSignal = Mathf.InverseLerp(effectStartThreshold, 1f, playerRadiation.Normalized);
        float exposureSignal = Mathf.InverseLerp(exposureRateStart, exposureRateFull, playerRadiation.RadiationRate)
                               * maxExposureIntensity;

        float targetIntensity = Mathf.Max(accruedSignal, exposureSignal);

        currentIntensity = Mathf.Lerp(
            currentIntensity,
            targetIntensity,
            Time.deltaTime * smoothSpeed
        );

        radiationMaterial.SetFloat(RadiationIntensityID, Mathf.Min(currentIntensity, maxOpacityMultiply));
    }
}
