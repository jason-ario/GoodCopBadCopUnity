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
    [Tooltip("Z-rotation in degrees when radiation is 0 (needle pointing upper-left).")]
    [SerializeField] private float minNeedleAngle = 65f;
    [Tooltip("Z-rotation in degrees when radiation is at max (needle pointing upper-right).")]
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
    [Tooltip("The Image whose fill represents the current radiation level.")]
    [SerializeField] private Image arcFillImage;
    [Tooltip("The Image used as the full-arc background gauge.")]
    [SerializeField] private Image arcBgImage;
    [SerializeField] private Color arcColorLow  = new Color(0.13f, 1.00f, 0.27f);  // green
    [SerializeField] private Color arcColorHigh = new Color(1.00f, 0.13f, 0.09f);  // red

    [Header("Display")]
    [SerializeField] private TMP_Text radiationValueText;
    [Tooltip("C# format string for the radiation value. {0} is the float.")]
    [SerializeField] private string valueFormat = "{0:F1}";
    [SerializeField] private string valueSuffix = " Sv";

    private PlayerRadiation _playerRadiation;
    private float _targetAngle;
    private float _currentAngle;

    // ── Exposure-rate tracking (drives jitter) ─────────────────────────────────

    /// <summary>Smoothed 0-1 value representing how fast radiation is currently rising.</summary>
    private float _jitterScale;
    private float _previousRadiation;
    private float _lastRadiationTime = float.NegativeInfinity;

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
    }

    private void OnEnable()
    {
        if (PlayerInstance.Instance?.PlayerRadiation != null)
            SubscribeTo(PlayerInstance.Instance.PlayerRadiation);
    }

    private void Update()
    {
        if (_playerRadiation == null && PlayerInstance.Instance?.PlayerRadiation != null)
            SubscribeTo(PlayerInstance.Instance.PlayerRadiation);

        AnimateNeedle();
    }

    private void OnDisable()
    {
        if (_playerRadiation != null)
        {
            _playerRadiation.OnRadiationChanged.RemoveListener(OnRadiationChanged);
            _playerRadiation = null;
        }
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
            arcBgImage.fillOrigin    = (int)Image.Origin180.Top;
            arcBgImage.fillClockwise = false;   // sweeps left → right
            arcBgImage.fillAmount    = 1f;      // always full – shows the gauge range
        }

        if (arcFillImage != null)
        {
            arcFillImage.type          = Image.Type.Filled;
            arcFillImage.fillMethod    = Image.FillMethod.Radial180;
            arcFillImage.fillOrigin    = (int)Image.Origin180.Top;
            arcFillImage.fillClockwise = false;
            arcFillImage.fillAmount    = 0f;
            arcFillImage.color         = arcColorLow;
        }
    }

    // ── PlayerRadiation subscription ───────────────────────────────────────────

    private void SubscribeTo(PlayerRadiation rad)
    {
        _playerRadiation = rad;
        _playerRadiation.OnRadiationChanged.AddListener(OnRadiationChanged);
        OnRadiationChanged(_playerRadiation.CurrentRadiation, _playerRadiation.MaxRadiation);
    }

    private void OnRadiationChanged(float current, float max)
    {
        float normalized = max > 0f ? current / max : 0f;
        _targetAngle = Mathf.Lerp(minNeedleAngle, maxNeedleAngle, normalized);

        // ── Measure exposure rate ──────────────────────────────────────────────
        float now     = Time.time;
        float elapsed = now - _lastRadiationTime;

        if (elapsed > 0f && elapsed < 2f)   // ignore stale gaps (scene load, pause, etc.)
        {
            float instantRate      = Mathf.Max(0f, current - _previousRadiation) / elapsed;
            float normalizedRate   = Mathf.Clamp01(instantRate / Mathf.Max(0.001f, maxExposureRate));
            // Square-root curve so even slow passive exposure produces visible jitter.
            float targetJitter     = Mathf.Sqrt(normalizedRate);
            _jitterScale = Mathf.Lerp(_jitterScale, targetJitter, elapsed * jitterSmoothing);
        }

        _previousRadiation = current;
        _lastRadiationTime = now;

        // ── Arc and text ───────────────────────────────────────────────────────
        if (arcFillImage != null)
        {
            arcFillImage.fillAmount = normalized;
            arcFillImage.color      = Color.Lerp(arcColorLow, arcColorHigh, normalized);
        }

        if (radiationValueText != null)
            radiationValueText.text = string.Format(valueFormat, current) + valueSuffix;
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
}
