using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Persistent HUD readout for the Checkpoint Integrity Score — how much of the base ATM
/// payout the player currently earns based on the booth's graffiti, trash, and perimeter
/// fence condition. It displays the precise score as text and as a row of discrete
/// filled/unfilled integrity squares.
///
/// Each square represents an equal portion of the full payout multiplier. The indicator is
/// rounded to the nearest square while the percentage label preserves the exact score.
///
/// Filled squares and the percentage label are tinted by score: white at full integrity,
/// lerping to yellow at <see cref="warningThreshold"/>, then to red at
/// <see cref="criticalThreshold"/> and below. Below <see cref="shakeThreshold"/> the
/// optional <see cref="lowIntegrityWobble"/> shakes the readout, harder the lower it goes.
/// </summary>
public class CheckpointIntegrityBar : StatBar
{
    [Header("Integrity Squares")]
    [SerializeField] private Image[] integritySquares;
    [SerializeField] private Color emptySquareColor = new(0.18f, 0.19f, 0.08f, 1f);

    [Header("Integrity Colors")]
    [SerializeField] private Color goodColor = Color.white;
    [SerializeField] private Color warningColor = new(1f, 0.85f, 0.1f, 1f);
    [SerializeField] private Color criticalColor = new(0.9f, 0.12f, 0.1f, 1f);
    [Tooltip("Normalized score (0-1) at which the readout is fully the warning color.")]
    [SerializeField, Range(0f, 1f)] private float warningThreshold = 0.7f;
    [Tooltip("Normalized score (0-1) at and below which the readout is fully the critical color.")]
    [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.6f;

    [Header("Low Integrity Shake")]
    [Tooltip("UIWobble on the readout root. Its intensity is driven by the score.")]
    [SerializeField] private UIWobble lowIntegrityWobble;
    [Tooltip("Normalized score (0-1) below which the readout starts shaking.")]
    [SerializeField, Range(0f, 1f)] private float shakeThreshold = 0.6f;
    [Tooltip("Shake intensity just below the threshold; ramps to Max Shake Intensity at 0%.")]
    [SerializeField, Min(0f)] private float minShakeIntensity = 0.4f;
    [SerializeField, Min(0f)] private float maxShakeIntensity = 1f;

    private void Awake()
    {
        // Fall back to the readout's UIWobble child if the reference wasn't wired in the inspector.
        if (lowIntegrityWobble == null)
            lowIntegrityWobble = GetComponentInChildren<UIWobble>(true);
    }

    private void OnEnable()
    {
        // Day 1 keeps the integrity system disabled, so the bar has nothing meaningful to show
        // yet — hide immediately rather than displaying a static 100% bar. Day_01 calls
        // CheckpointIntegrityService.SetEnabled(true) and Show() together right when the
        // trash/graffiti tasks are assigned, which re-triggers this OnEnable with the system
        // already enabled.
        if (!CheckpointIntegrityService.IsEnabled)
        {
            gameObject.SetActive(false);
            return;
        }

        CheckpointIntegrityService.OnIntegrityScoreChanged += OnIntegrityScoreChanged;

        // Force a fresh read so the bar is correct the moment it becomes visible
        // (e.g. right after a scene load or late UI enable).
        CheckpointIntegrityService service = CheckpointIntegrityService.Instance;
        service.Recalculate();
        UpdateIntegrityDisplay(service.IntegrityScore, service.MaxScore);
    }

    protected override void OnDisable()
    {
        CheckpointIntegrityService.OnIntegrityScoreChanged -= OnIntegrityScoreChanged;
        base.OnDisable();
    }

    private void OnIntegrityScoreChanged(float newScore)
    {
        CheckpointIntegrityService service = CheckpointIntegrityService.Instance;
        UpdateIntegrityDisplay(newScore, service.MaxScore);
    }

    private void UpdateIntegrityDisplay(float current, float max)
    {
        UpdateBar(current, max);

        float normalizedScore = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        Color integrityColor = EvaluateIntegrityColor(normalizedScore);

        if (PercentageText != null)
            PercentageText.color = integrityColor;

        UpdateShake(normalizedScore);

        if (integritySquares == null || integritySquares.Length == 0)
            return;

        int filledSquareCount = Mathf.Clamp(
            Mathf.RoundToInt(normalizedScore * integritySquares.Length),
            0,
            integritySquares.Length);

        for (int i = 0; i < integritySquares.Length; i++)
        {
            if (integritySquares[i] != null)
                integritySquares[i].color = i < filledSquareCount ? integrityColor : emptySquareColor;
        }
    }

    /// <summary>
    /// White at 100% → yellow at the warning threshold → red at the critical threshold and below.
    /// </summary>
    private Color EvaluateIntegrityColor(float normalizedScore)
    {
        if (normalizedScore >= warningThreshold)
        {
            float t = Mathf.InverseLerp(warningThreshold, 1f, normalizedScore);
            return Color.Lerp(warningColor, goodColor, t);
        }

        float criticalT = Mathf.InverseLerp(criticalThreshold, warningThreshold, normalizedScore);
        return Color.Lerp(criticalColor, warningColor, criticalT);
    }

    private void UpdateShake(float normalizedScore)
    {
        if (lowIntegrityWobble == null)
            return;

        if (normalizedScore >= shakeThreshold || shakeThreshold <= 0f)
        {
            lowIntegrityWobble.SetIntensity(0f);
            return;
        }

        // 0 just under the threshold, 1 at empty.
        float severity = 1f - normalizedScore / shakeThreshold;
        lowIntegrityWobble.SetIntensity(Mathf.Lerp(minShakeIntensity, maxShakeIntensity, severity));
    }
}
