using UnityEngine;

[CreateAssetMenu(menuName = "UI/TMP Wobble Profile")]
public class TMPWobbleProfile : ScriptableObject
{
    [Header("Amplitude")]
    public float amountX = 0.35f;
    public float amountY = 0.15f;

    [Header("Speed")]
    public float speed = 2f;

    [Header("Noise")]
    public float noiseAmount = 0.05f;

    [Header("Random Phase Range")]
    public float randomPhaseMin = 0f;
    public float randomPhaseMax = 6.28318f; // 2π recommended

    [Header("Axis Frequency Multipliers")]
    public float xFrequencyMultiplier = 1f;
    public float yFrequencyMultiplier = 1.21f;
}