using System.Collections;
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
/// When the displayed score climbs back to 100% from below, a restore pulse plays (scale
/// punch, highlight flash, and a left-to-right sweep across the squares).
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
    [Tooltip("How much darker filled-square outlines are than the fill (0 = same as fill, 1 = black).")]
    [SerializeField, Range(0f, 1f)] private float filledOutlineDarken = 0.25f;
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

    [Header("Restored Pulse (back to 100%)")]
    [Tooltip("Play a pulse/highlight when the displayed score climbs back to 100% from a lower value.")]
    [SerializeField] private bool pulseOnRestore = true;
    [Tooltip("RectTransform scaled by the pulse. Defaults to this panel.")]
    [SerializeField] private RectTransform pulseTarget;
    [SerializeField, Min(0.05f)] private float pulseDuration = 0.55f;
    [Tooltip("Extra scale at the pulse peak (0.08 = 108%).")]
    [SerializeField, Min(0f)] private float pulseScale = 0.08f;
    [Tooltip("Pulse weight over normalized time (0-1). Peak should be 1.")]
    [SerializeField] private AnimationCurve pulseCurve = new(
        new Keyframe(0f, 0f, 0f, 8f),
        new Keyframe(0.2f, 1f),
        new Keyframe(1f, 0f));
    [Tooltip("Color filled squares and the percent text flash toward at the pulse peak.")]
    [SerializeField] private Color pulseHighlightColor = new(0.65f, 1f, 0.55f, 1f);
    [Tooltip("Color the panel background flashes toward at the pulse peak.")]
    [SerializeField] private Color pulsePanelHighlightColor = new(0.75f, 1f, 0.65f, 1f);
    [SerializeField, Range(0f, 1f)] private float pulsePanelFlashStrength = 0.35f;
    [Tooltip("Extra scale per square in the left-to-right sweep (0 disables the sweep).")]
    [SerializeField, Min(0f)] private float squareSweepScale = 0.35f;
    [Tooltip("Delay between consecutive squares in the sweep, in seconds.")]
    [SerializeField, Min(0f)] private float squareSweepStagger = 0.035f;

    private Outline[] _squareOutlines;
    private Vector3[] _squareBaseScales;
    private Vector3 _pulseTargetBaseScale = Vector3.one;
    private Color _panelAuthoredColor = Color.white;
    private bool _isInitialized;
    private bool _isPreview;
    private float _lastNormalizedScore = -1f;
    private float _pulseWeight;
    private Coroutine _pulseCoroutine;

    private void Awake() => Initialize();

    /// <summary>
    /// Turns this instance into a static display copy (e.g. the guidebook's printed panel): it no
    /// longer hides itself or listens to <see cref="CheckpointIntegrityService"/>. Call before the
    /// copy is first enabled, then use <see cref="ShowPreview"/>.
    /// </summary>
    public void MarkAsPreview() => _isPreview = true;

    /// <summary>Renders a fixed normalized score (0-1) with the HUD's colors. Preview copies only.</summary>
    public void ShowPreview(float normalizedScore)
    {
        _isPreview = true;
        UpdateIntegrityDisplay(Mathf.Clamp01(normalizedScore), 1f);
    }

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

        if (pulseTarget == null)
            pulseTarget = transform as RectTransform;
        if (pulseTarget != null)
            _pulseTargetBaseScale = pulseTarget.localScale;

        int squareCount = integritySquares != null ? integritySquares.Length : 0;
        _squareOutlines = new Outline[squareCount];
        _squareBaseScales = new Vector3[squareCount];
        for (int i = 0; i < squareCount; i++)
        {
            if (integritySquares[i] == null) continue;
            _squareOutlines[i] = integritySquares[i].GetComponent<Outline>();
            _squareBaseScales[i] = integritySquares[i].rectTransform.localScale;
        }
    }

    private void OnEnable()
    {
        if (_isPreview) return;
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
        StopPulse();
        base.OnDisable();
    }

    private void OnIntegrityScoreChanged(float newScore)
    {
        CheckpointIntegrityService service = CheckpointIntegrityService.Instance;
        float previousNormalized = _lastNormalizedScore;
        UpdateIntegrityDisplay(newScore, service.MaxScore);

        // Pulse when the displayed percentage climbs back to 100% from a lower value.
        if (pulseOnRestore && !_isPreview && previousNormalized >= 0f
            && ToDisplayedPercent(previousNormalized) < 100
            && ToDisplayedPercent(_lastNormalizedScore) >= 100)
        {
            PlayRestorePulse();
        }
    }

    private static int ToDisplayedPercent(float normalizedScore) => Mathf.RoundToInt(normalizedScore * 100f);

    private void UpdateIntegrityDisplay(float current, float max)
    {
        Initialize();
        UpdateBar(current, max);

        float normalizedScore = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        _lastNormalizedScore = normalizedScore;
        ApplyIntegrityVisuals(normalizedScore);
    }

    private void ApplyIntegrityVisuals(float normalizedScore)
    {
        Color integrityColor = EvaluateThreeStop(normalizedScore, goodColor, warningColor, criticalColor);
        integrityColor = Color.Lerp(integrityColor, pulseHighlightColor, _pulseWeight);

        if (PercentageText != null)
            PercentageText.color = integrityColor;

        if (panelBackground != null)
        {
            Color tint = EvaluateThreeStop(normalizedScore, panelGoodTint, panelWarningTint, panelCriticalTint);
            Color panelColor = _panelAuthoredColor * tint;
            Color flash = pulsePanelHighlightColor;
            flash.a = panelColor.a;
            panelBackground.color = Color.Lerp(panelColor, flash, _pulseWeight * pulsePanelFlashStrength);
        }

        UpdateShake(normalizedScore);

        if (integritySquares == null || integritySquares.Length == 0)
            return;

        Color emptyColor = EvaluateThreeStop(normalizedScore, emptyGoodColor, emptyWarningColor, emptyCriticalColor);
        Color emptyOutline = EvaluateThreeStop(normalizedScore, emptyOutlineGoodColor, emptyOutlineWarningColor, emptyOutlineCriticalColor);
        Color filledOutline = Color.Lerp(integrityColor, Color.black, filledOutlineDarken);
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

    private void PlayRestorePulse()
    {
        if (!isActiveAndEnabled) return;
        StopPulse();
        _pulseCoroutine = StartCoroutine(RestorePulseRoutine());
    }

    /// <summary>
    /// Scale punch on <see cref="pulseTarget"/>, color flash on squares/text/panel, and a
    /// left-to-right scale sweep across the integrity squares. Unscaled time so it plays
    /// regardless of timeScale.
    /// </summary>
    private IEnumerator RestorePulseRoutine()
    {
        int squareCount = integritySquares != null ? integritySquares.Length : 0;
        float sweepTail = squareSweepScale > 0f ? squareSweepStagger * Mathf.Max(0, squareCount - 1) : 0f;
        float totalDuration = pulseDuration + sweepTail;
        float elapsed = 0f;

        while (elapsed < totalDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            float mainT = Mathf.Clamp01(elapsed / pulseDuration);
            _pulseWeight = Mathf.Clamp01(pulseCurve.Evaluate(mainT));

            if (pulseTarget != null)
                pulseTarget.localScale = _pulseTargetBaseScale * (1f + pulseScale * pulseCurve.Evaluate(mainT));

            if (squareSweepScale > 0f)
            {
                for (int i = 0; i < squareCount; i++)
                {
                    if (integritySquares[i] == null) continue;
                    float squareT = Mathf.Clamp01((elapsed - i * squareSweepStagger) / pulseDuration);
                    float squareWeight = squareT > 0f ? pulseCurve.Evaluate(squareT) : 0f;
                    integritySquares[i].rectTransform.localScale = _squareBaseScales[i] * (1f + squareSweepScale * squareWeight);
                }
            }

            ApplyIntegrityVisuals(_lastNormalizedScore);
            yield return null;
        }

        _pulseCoroutine = null;
        ResetPulseVisuals();
    }

    private void StopPulse()
    {
        if (_pulseCoroutine != null)
        {
            StopCoroutine(_pulseCoroutine);
            _pulseCoroutine = null;
        }

        ResetPulseVisuals();
    }

    private void ResetPulseVisuals()
    {
        if (!_isInitialized) return;

        bool wasPulsing = _pulseWeight > 0f;
        _pulseWeight = 0f;

        if (pulseTarget != null)
            pulseTarget.localScale = _pulseTargetBaseScale;

        if (integritySquares != null)
        {
            for (int i = 0; i < integritySquares.Length; i++)
            {
                if (integritySquares[i] != null)
                    integritySquares[i].rectTransform.localScale = _squareBaseScales[i];
            }
        }

        if (wasPulsing && _lastNormalizedScore >= 0f)
            ApplyIntegrityVisuals(_lastNormalizedScore);
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
