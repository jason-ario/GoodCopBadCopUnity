using UnityEngine;

public class IrradiatedOverlay : MonoBehaviour
{
    [SerializeField] private PlayerRadiation playerRadiation;

    [Header("Shader Material")]
    [SerializeField] private Material radiationMaterial;

    [Header("Activation")]
    [SerializeField] private float effectStartThreshold = 0.75f;

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
            playerRadiation = PlayerInstance.Instance.PlayerRadiation;
            return;
        }

        float radiation01 = playerRadiation.Normalized;

        float targetIntensity = Mathf.InverseLerp(
            effectStartThreshold,
            1,
            radiation01
        );

        currentIntensity = Mathf.Lerp(
            currentIntensity,
            targetIntensity,
            Time.deltaTime * smoothSpeed
        );

        radiationMaterial.SetFloat(RadiationIntensityID, Mathf.Min(currentIntensity, maxOpacityMultiply));
    }
}