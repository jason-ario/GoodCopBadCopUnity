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

    [Header("Integrity Colors (Fill + Percent Text)")]
    [SerializeField] private Color goodColor = Color.white;
    [SerializeField] private Color warningColor = new(1f, 0.85f, 0.1f, 1f);
    [SerializeField] private Color criticalColor = new(0.9f, 0.12f, 0.1f, 1f);
    [Tooltip("Normalized score (0-1) at which the readout is fully the warning color.")]
    [SerializeField, Range(0f, 1f)] private float warningThreshold = 0.7f;
    [Tooltip("Normalized score (0-1) at and below which the readout is fully the critical color.")]
    [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.6f;

    [Header("Empty Square Colors")]
    [SerializeField] private Color emptyGoodColor = new(0.18f, 0.19f, 0.08f, 1f);
    [SerializeField] private Color emptyWarningColor = new(0.24f, 0.19f, 0.04f, 1f);
    [SerializeField] private Color emptyCriticalColor = new(0.26f, 0.05f, 0.04f, 1f);

    [Header("Square Outlines")]
    [Tooltip("How far filled-square outlines are pushed toward white (0 = same as fill, 1 = pure white).")]
    [SerializeField, Range(0f, 1f)] private float filledOutlineWhiteBlend = 0.55f;
    [SerializeField, Range(0f, 1f)] private float filledOutlineAlpha = 0.9f;
    [SerializeField] private Color emptyOutlineGoodColor = new(0.55f, 0.59f, 0.17f, 0.8f);
    [SerializeField] private Color emptyOutlineWarningColor = new(0.62f, 0.5f, 0.12f, 0.8f);
    [SerializeField] private Color emptyOutlineCriticalColor = new(0.6f, 0.16f, 0.12f, 0.8f);

    [Header("Panel Background")]
    [Tooltip("Panel background image. Defaults to the Image on this GameObject.")]
    [SerializeField] private Image panelBackground;
    [Tooltip("Tints multiplied onto the panel's authored color.")]
    [SerializeField] private Color panelGoodTint = Color.white;
    [SerializeField] private Color panelWarningTint = new(1f, 0.88f, 0.6f, 1f);
    [SerializeField] private Color panelCriticalTint = new(0.85f, 0.38f, 0.34f, 1f);

    [Header("Low Integrity Shake")]
    [Tooltip("UIWobble on the readout root. Its intensity is driven by the score.")]
    [SerializeField] private UIWobble lowIntegrityWobble;
    [Tooltip("Normalized score (0-1) below which the readout starts shaking.")]
    [SerializeField, Range(0f, 1f)] private float shakeThreshold = 0.6f;
    [Tooltip("Shake intensity just below the threshold; ramps to Max Shake Intensity at 0%.")]
    [SerializeField, Min(0f)] private float minShakeIntensity = 0.4f;
    [SerializeField, Min(0f)] private float maxShakeIntensity = 1f;

    private Outline[] _squareOutlines;
    private Color _panelAuthoredColor = Color.white;
    private bool _isInitialized;

    private void Awake() => Initialize();

    private void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        // Fall back to the readout's UIWobble child if the reference wasn't wired in the inspector.
        if (lowIntegrityWobble == null)
            lowIntegrityWobble = GetComponentInChildren<UIWobble>(true);

        if (panelBackground == null)
            panelBackground = GetComponent<Image>();
        if (panelBackground != null)
            _panelAuthoredColor = panelBackground.color;

        int squareCount = integritySquares != null ? integritySquares.Length : 0;
        _squareOutlines = new Outline[squareCount];
        for (int i = 0; i < squareCount; i++)
        {
            if (integritySquares[i] != null)
                _squareOutlines[i] = integritySquares[i].GetComponent<Outline>();
        }
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
        Initialize();
        UpdateBar(current, max);

        float normalizedScore = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        Color integrityColor = EvaluateThreeStop(normalizedScore, goodColor, warningColor, criticalColor);

        if (PercentageText != null)
            PercentageText.color = integrityColor;

        if (panelBackground != null)
        {
            Color tint = EvaluateThreeStop(normalizedScore, panelGoodTint, panelWarningTint, panelCriticalTint);
            panelBackground.color = _panelAuthoredColor * tint;
        }

        UpdateShake(normalizedScore);

        if (integritySquares == null || integritySquares.Length == 0)
            return;

        Color emptyColor = EvaluateThreeStop(normalizedScore, emptyGoodColor, emptyWarningColor, emptyCriticalColor);
        Color emptyOutline = EvaluateThreeStop(normalizedScore, emptyOutlineGoodColor, emptyOutlineWarningColor, emptyOutlineCriticalColor);
        Color filledOutline = Color.Lerp(integrityColor, Color.white, filledOutlineWhiteBlend);
        filledOutline.a = filledOutlineAlpha;

        int filledSquareCount = Mathf.Clamp(
            Mathf.RoundToInt(normalizedScore * integritySquares.Length),
            0,
            integritySquares.Length);

        for (int i = 0; i < integritySquares.Length; i++)
        {
            bool isFilled = i < filledSquareCount;

            if (integritySquares[i] != null)
                integritySquares[i].color = isFilled ? integrityColor : emptyColor;

            if (_squareOutlines[i] != null)
                _squareOutlines[i].effectColor = isFilled ? filledOutline : emptyOutline;
        }
    }

    /// <summary>
    /// Good at 100% → warning at the warning threshold → critical at the critical threshold and below.
    /// </summary>
    private Color EvaluateThreeStop(float normalizedScore, Color good, Color warning, Color critical)
    {
        if (normalizedScore >= warningThreshold)
        {
            float t = Mathf.InverseLerp(warningThreshold, 1f, normalizedScore);
            return Color.Lerp(warning, good, t);
        }

        float criticalT = Mathf.InverseLerp(criticalThreshold, warningThreshold, normalizedScore);
        return Color.Lerp(critical, warning, criticalT);
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
