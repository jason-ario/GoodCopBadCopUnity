using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives a geiger-counter-style radiation meter with a rotating needle and a
/// colour-shifting arc fill. Subscribes to <see cref="PlayerRadiation.OnRadiationChanged"/>.
///
/// The arc fill image must be assigned as the <see cref="arcFillImage"/> field.
/// <see cref="Awake"/> configures the fill method to Radial180 at runtime, so the
/// sprite used can be any solid-colour image.
/// </summary>
[DisallowMultipleComponent]
public class GeigerCounterUI : MonoBehaviour
{
    [Header("Needle")]
    [Tooltip("RectTransform of the needle. Its pivot must be at (0.5, 0) – bottom-centre.")]
    [SerializeField] private RectTransform needle;
    [Tooltip("Z-rotation in degrees when current exposure rate is 0 (needle pointing upper-left).")]
    [SerializeField] private float minNeedleAngle = 65f;
    [Tooltip("Z-rotation in degrees when current exposure rate is at/above maxExposureRate (needle pointing upper-right).")]
    [SerializeField] private float maxNeedleAngle = -65f;
    [SerializeField] private float needleSmoothSpeed = 6f;

    [Header("Jitter – Geiger Counter Feel")]
    [Tooltip("Maximum jitter in degrees at full exposure rate.")]
    [SerializeField] private float jitterAmplitude = 8f;
    [Tooltip("Base frequency of the needle oscillation.")]
    [SerializeField] private float jitterFrequency = 10f;
    [Tooltip("Radiation units/sec considered 'maximum' exposure for jitter scaling. " +
             "Passive rate is ~0.15 u/s; hotspots are typically 1-3 u/s.")]
    [SerializeField] private float maxExposureRate = 2f;
    [Tooltip("How quickly the jitter scale smooths toward the measured exposure rate.")]
    [SerializeField] private float jitterSmoothing = 4f;

    [Header("Arc Fill")]
    [Tooltip("The Image whose fill represents the current radiation level. " +
             "Tinted with the status light's band colour (steady, no pulse).")]
    [SerializeField] private Image arcFillImage;
    [Tooltip("The Image used as the full-arc background gauge.")]
    [SerializeField] private Image arcBgImage;

    [Header("Display")]
    [Tooltip("Readout showing the radiation band as \"Radiation: LOW / MEDIUM / HIGH\". Uses the status light " +
             "thresholds. Written from code, so its LocalizedText (if any) must have an empty key.")]
    [SerializeField] private TMP_Text radiationValueText;
    [Tooltip("Colour of the \"Radiation:\" prefix. The LOW/MEDIUM/HIGH word uses the status light's band colour.")]
    [SerializeField] private Color valueLabelColor = new Color(0.85f, 0.85f, 0.85f);

    [Header("Crack Overlay – High Radiation Damage")]
    [Tooltip("The Image using the GoodCopBadCop/GlassCrackOverlay material, layered over the gauge glass.")]
    [SerializeField] private Image crackedGlassImage;
    [Tooltip("Normalised radiation (0-1) at which cracks start to appear. Reaches full crack at 1.")]
    [SerializeField] [Range(0f, 1f)] private float crackStartThreshold = 0.5f;
    [Tooltip("How quickly the crack overlay eases toward its target progress.")]
    [SerializeField] private float crackSmoothSpeed = 4f;

    [Header("High Radiation Shake")]
    [Tooltip("UIWobble on the gauge, enabled once radiation crosses shakeActivateThreshold. " +
             "Should start disabled on the GameObject.")]
    [SerializeField] private UIWobble shakeWobble;
    [Tooltip("Normalised radiation (0-1) above which the gauge starts shaking.")]
    [SerializeField] [Range(0f, 1f)] private float shakeActivateThreshold = 0.75f;

    [Header("Status Light")]
    [Tooltip("Bulb Image above the gauge, tinted by the overall/accumulated radiation level.")]
    [SerializeField] private Image statusLightImage;
    [Tooltip("Normalised radiation (0-1) at which the light turns yellow and blinks slowly.")]
    [SerializeField] [Range(0f, 1f)] private float lightWarningThreshold = 0.5f;
    [Tooltip("Normalised radiation (0-1) at which the light turns red and blinks fast. " +
             "Matches PlayerRadiation's damage threshold by default.")]
    [SerializeField] [Range(0f, 1f)] private float lightDangerThreshold = 0.75f;
    [SerializeField] private Color lightColorSafe    = new Color(0.00f, 0.45f, 0.16f);  // dark green
    [SerializeField] private Color lightColorWarning = new Color(0.68f, 0.52f, 0.00f);  // dark amber-yellow
    [SerializeField] private Color lightColorDanger  = new Color(0.60f, 0.05f, 0.03f);  // dark red
    [Tooltip("Seconds per full pulse (bright → dim → bright) while in the safe band.")]
    [SerializeField] private float lightSafePulsePeriod = 3f;
    [Tooltip("Brightness multiplier at the dimmest point of the safe (green) pulse. Close to 1 = subtle.")]
    [SerializeField] [Range(0f, 1f)] private float lightSafeDimBrightness = 0.8f;
    [Tooltip("Seconds per full pulse (bright → dim → bright) while in the warning band.")]
    [SerializeField] private float lightWarningBlinkPeriod = 2f;
    [Tooltip("Seconds per full pulse (bright → dim → bright) while in the danger band.")]
    [SerializeField] private float lightDangerBlinkPeriod = 1f;
    [Tooltip("Brightness multiplier at the dimmest point of the warning/danger pulse.")]
    [SerializeField] [Range(0f, 1f)] private float lightOffBrightness = 0.5f;

    private float _normalizedRadiation;

    private PlayerRadiation _playerRadiation;
    private PlayerInstance _subscribedInstance;
    private float _targetAngle;
    private float _currentAngle;

    private static readonly int CrackProgressId = Shader.PropertyToID("_CrackProgress");
    private Material _crackMaterialInstance;
    private float _crackProgressTarget;
    private float _crackProgressCurrent;

    // ── Exposure-rate tracking (drives jitter) ─────────────────────────────────

    /// <summary>Smoothed 0-1 value representing how fast radiation is currently rising.</summary>
    private float _jitterScale;
    private float _previousRadiation;
    private float _lastRadiationTime = float.NegativeInfinity;
    private bool _isFull;
    private bool _isPreview;

    /// <summary>Band currently shown by the readout (0 low, 1 medium, 2 high; -1 = not written yet).</summary>
    private int _shownBand = -1;

    // ── Preview (static display copies, e.g. the guidebook) ────────────────────

    /// <summary>
    /// Turns this instance into a static display copy: it no longer follows any player or animates.
    /// Call before the copy is first enabled, then use <see cref="ShowPreview"/>.
    /// </summary>
    public void MarkAsPreview() => _isPreview = true;

    /// <summary>
    /// Renders fixed readings with the HUD's colors. Preview copies only.
    /// </summary>
    /// <param name="normalizedRadiation">Accumulated radiation (0-1): arc fill, colour and readout.</param>
    /// <param name="exposureRate01">Needle position (0 = resting, 1 = pegged).</param>
    /// <param name="maxRadiation">Radiation units at 100%, for the readout.</param>
    public void ShowPreview(float normalizedRadiation, float exposureRate01, float maxRadiation)
    {
        _isPreview = true;
        float normalized = Mathf.Clamp01(normalizedRadiation);

        _currentAngle = _targetAngle = Mathf.Lerp(minNeedleAngle, maxNeedleAngle, Mathf.Clamp01(exposureRate01));
        if (needle != null)
            needle.localRotation = Quaternion.Euler(0f, 0f, _currentAngle);

        if (arcFillImage != null)
        {
            arcFillImage.fillAmount = normalized;
            arcFillImage.color      = GetLightColor(normalized, out _, out _);
        }

        // Steady (non-blinking) bulb colour for the band.
        _normalizedRadiation = normalized;
        RefreshValueText(force: true);
        if (statusLightImage != null)
            statusLightImage.color = GetLightColor(normalized, out _, out _);

        // No damage effects on a printed copy.
        if (crackedGlassImage != null) crackedGlassImage.gameObject.SetActive(false);
        if (shakeWobble != null) shakeWobble.enabled = false;
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _targetAngle = minNeedleAngle;
        _currentAngle = minNeedleAngle;

        // Apply starting rotation immediately so the needle is in position before
        // the first Update frame.
        if (needle != null)
            needle.localRotation = Quaternion.Euler(0f, 0f, minNeedleAngle);

        ConfigureArcImages();
        BuildCrackMaterialInstance();
    }

    private void OnEnable()
    {
        Loc.LanguageChanged += OnLanguageChanged;
        if (_isPreview)
        {
            RefreshValueText(force: true);
            return;
        }
        SubscribeTo(SpectateManager.HudSubject);
    }

    private void OnLanguageChanged() => RefreshValueText(force: true);

    private void Update()
    {
        if (_isPreview) return;

        // Compare against the current PlayerInstance rather than just checking for a null
        // PlayerRadiation: death/respawn keeps the old (corpse) PlayerInstance alive instead of
        // destroying it (see PlayerInstance.DetachFromPlayerObject), so a cached PlayerRadiation
        // reference never becomes null on its own — it just stops firing events on the corpse,
        // freezing the gauge at whatever radiation value it had at death.
        // HudSubject also follows the watched teammate while spectating.
        PlayerInstance subject = SpectateManager.HudSubject;
        if (subject != _subscribedInstance)
            SubscribeTo(subject);

        DecayExposureRate();
        AnimateNeedle();
        AnimateCrackOverlay();
        AnimateStatusLight();
    }

    private void OnDisable()
    {
        Loc.LanguageChanged -= OnLanguageChanged;
        SubscribeTo(null);
    }

    // ── Gauge configuration ────────────────────────────────────────────────────

    /// <summary>
    /// Configures the fill images as Radial180 / Top-origin gauges.
    /// Called once in Awake; safe to call again if images are swapped at runtime.
    /// </summary>
    private void ConfigureArcImages()
    {
        if (arcBgImage != null)
        {
            arcBgImage.type          = Image.Type.Filled;
            arcBgImage.fillMethod    = Image.FillMethod.Radial180;
            arcBgImage.fillClockwise = false;   // sweeps left → right
            arcBgImage.fillAmount    = 1f;      // always full – shows the gauge range
        }

        if (arcFillImage != null)
        {
            arcFillImage.fillAmount    = 0f;
            arcFillImage.color         = lightColorSafe;
        }
    }

    /// <summary>
    /// Instantiates a private copy of the crack overlay's material so runtime changes to
    /// _CrackProgress never leak into the shared material asset (mirrors the pattern used by
    /// <see cref="BreakableGlassController"/>, which uses a MaterialPropertyBlock for the same
    /// reason on a MeshRenderer).
    /// </summary>
    private void BuildCrackMaterialInstance()
    {
        if (crackedGlassImage == null || crackedGlassImage.material == null) return;

        _crackMaterialInstance = new Material(crackedGlassImage.material);
        crackedGlassImage.material = _crackMaterialInstance;
        _crackMaterialInstance.SetFloat(CrackProgressId, 0f);
    }

    // ── PlayerRadiation subscription ───────────────────────────────────────────

    private void SubscribeTo(PlayerInstance playerInstance)
    {
        if (_playerRadiation != null)
        {
            _playerRadiation.OnRadiationChanged.RemoveListener(OnRadiationChanged);
            _playerRadiation = null;
        }

        _subscribedInstance = playerInstance;

        // Forget the previous subject's reading so switching spectate targets doesn't register
        // the difference between two players' radiation as a sudden exposure spike.
        _lastRadiationTime = float.NegativeInfinity;
        _isFull = false;

        if (playerInstance == null || playerInstance.PlayerRadiation == null) return;

        _playerRadiation = playerInstance.PlayerRadiation;
        _playerRadiation.OnRadiationChanged.AddListener(OnRadiationChanged);
        OnRadiationChanged(_playerRadiation.CurrentRadiation, _playerRadiation.MaxRadiation);
    }

    private void OnRadiationChanged(float current, float max)
    {
        float normalized = max > 0f ? current / max : 0f;

        // ── Measure exposure rate (current velocity, not accumulated total) ────
        // The needle reflects how fast radiation is being gained right now; the
        // accumulated/overall radiation is shown by a separate meter (RadiationBarUI).
        float now     = Time.time;
        float elapsed = now - _lastRadiationTime;

        if (elapsed > 0f && elapsed < 2f)   // ignore stale gaps (scene load, pause, etc.)
        {
            float instantRate    = Mathf.Max(0f, current - _previousRadiation) / elapsed;
            float normalizedRate = Mathf.Clamp01(instantRate / Mathf.Max(0.001f, maxExposureRate));

            _targetAngle = Mathf.Lerp(minNeedleAngle, maxNeedleAngle, normalizedRate);

            // Square-root curve so even slow passive exposure produces visible jitter.
            float targetJitter = Mathf.Sqrt(normalizedRate);
            _jitterScale = Mathf.Lerp(_jitterScale, targetJitter, elapsed * jitterSmoothing);
        }

        _previousRadiation = current;
        _lastRadiationTime = now;
        _isFull = max > 0f && current >= max - 0.001f;

        // ── Arc and text – still reflect the overall/accumulated radiation ─────
        if (arcFillImage != null)
        {
            arcFillImage.fillAmount = normalized;
            arcFillImage.color      = GetLightColor(normalized, out _, out _);
        }

        _normalizedRadiation = normalized;
        RefreshValueText(force: false);

        // ── Crack overlay and shake – both scale with the overall/accumulated radiation ────
        _crackProgressTarget = Mathf.Clamp01(Mathf.InverseLerp(crackStartThreshold, 1f, normalized));

        if (shakeWobble != null)
            shakeWobble.enabled = normalized >= shakeActivateThreshold;
    }

    /// <summary>
    /// If no radiation event has arrived recently, the exposure rate is treated as
    /// having dropped to zero so the needle settles back to <see cref="minNeedleAngle"/>
    /// instead of freezing at the last measured rate.
    /// </summary>
    private void DecayExposureRate()
    {
        // At max radiation the value is clamped and stops changing, so no more change events
        // arrive and the measured rate reads as zero. Keep the needle pegged and shaking instead.
        if (_isFull)
        {
            _targetAngle = maxNeedleAngle;
            _jitterScale = Mathf.Lerp(_jitterScale, 1f, Time.deltaTime * jitterSmoothing);
            return;
        }

        float sinceLastUpdate = Time.time - _lastRadiationTime;
        if (sinceLastUpdate <= 0.5f)
            return;

        _targetAngle = minNeedleAngle;
        _jitterScale = Mathf.Lerp(_jitterScale, 0f, Time.deltaTime * jitterSmoothing);
    }

    // ── Needle animation ───────────────────────────────────────────────────────

    private void AnimateNeedle()
    {
        if (needle == null) return;

        _currentAngle = Mathf.Lerp(
            _currentAngle, _targetAngle,
            Time.deltaTime * needleSmoothSpeed);

        // Multi-frequency noise gives an organic, irregular geiger-counter feel
        // rather than a simple repeating sine wave.
        float t = Time.time * jitterFrequency;
        float noise = Mathf.Sin(t)               * 0.50f
                    + Mathf.Sin(t * 2.71f + 1f)  * 0.30f
                    + Mathf.Sin(t * 6.83f + 4f)  * 0.20f;

        needle.localRotation = Quaternion.Euler(0f, 0f, _currentAngle + noise * jitterAmplitude * _jitterScale);
    }

    /// <summary>Eases the crack overlay's _CrackProgress toward the radiation-driven target.</summary>
    private void AnimateCrackOverlay()
    {
        if (_crackMaterialInstance == null) return;

        _crackProgressCurrent = Mathf.Lerp(
            _crackProgressCurrent, _crackProgressTarget,
            Time.deltaTime * crackSmoothSpeed);

        _crackMaterialInstance.SetFloat(CrackProgressId, _crackProgressCurrent);
    }

    // ── Readout ────────────────────────────────────────────────────────────────

    /// <summary>0 = low, 1 = medium, 2 = high, using the same thresholds as the status light.</summary>
    private int GetRadiationBand(float normalized)
    {
        if (normalized >= lightDangerThreshold)  return 2;
        if (normalized >= lightWarningThreshold) return 1;
        return 0;
    }

    /// <summary>
    /// Writes "Radiation: LOW / MEDIUM / HIGH". Only rewrites when the band changes (or when forced,
    /// e.g. after a language change) because radiation events arrive every frame.
    /// </summary>
    private void RefreshValueText(bool force)
    {
        if (radiationValueText == null) return;

        int band = GetRadiationBand(_normalizedRadiation);
        if (!force && band == _shownBand) return;
        _shownBand = band;

        string level = band switch
        {
            2 => Loc.T("HIGH"),
            1 => Loc.T("MEDIUM"),
            _ => Loc.T("LOW"),
        };

        // Prefix in the label colour, level word tinted with the bulb's steady band colour (rich text).
        string levelHex = ColorUtility.ToHtmlStringRGB(GetLightColor(_normalizedRadiation, out _, out _));
        string coloredLevel = "<color=#" + levelHex + ">" + level + "</color>";

        radiationValueText.color = valueLabelColor;
        // Already localized: Verbatim stops render-time localization from processing the combined string again.
        radiationValueText.text  = Loc.Verbatim(Loc.T("Radiation: {0}", coloredLevel));
    }

    // ── Status light ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the bulb colour for a normalised radiation level, plus the pulse period and the
    /// dimmest brightness for its band: green = safe, yellow = warning, red = danger.
    /// </summary>
    private Color GetLightColor(float normalized, out float pulsePeriod, out float dimBrightness)
    {
        if (normalized >= lightDangerThreshold)
        {
            pulsePeriod   = lightDangerBlinkPeriod;
            dimBrightness = lightOffBrightness;
            return lightColorDanger;
        }

        if (normalized >= lightWarningThreshold)
        {
            pulsePeriod   = lightWarningBlinkPeriod;
            dimBrightness = lightOffBrightness;
            return lightColorWarning;
        }

        pulsePeriod   = lightSafePulsePeriod;
        dimBrightness = lightSafeDimBrightness;
        return lightColorSafe;
    }

    /// <summary>
    /// Tints the bulb by radiation band and pulses it continuously: the brightness fades from full
    /// down to the band's dim level over the first half of the period and back up over the second,
    /// so it is always fading rather than holding on or off.
    /// </summary>
    private void AnimateStatusLight()
    {
        if (statusLightImage == null) return;

        Color color = GetLightColor(_normalizedRadiation, out float period, out float dim);

        if (period > 0f)
        {
            // Plain cosine: 1 at the start of the period, 0 halfway, 1 again at the end.
            float on = 0.5f + 0.5f * Mathf.Cos(Time.time / period * 2f * Mathf.PI);
            float brightness = Mathf.Lerp(dim, 1f, on);
            color = new Color(color.r * brightness, color.g * brightness, color.b * brightness, color.a);
        }

        statusLightImage.color = color;
    }
}
