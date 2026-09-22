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
        SubscribeTo(PlayerInstance.Instance);
    }

    private void Update()
    {
        // Compare against the current PlayerInstance rather than just checking for a null
        // PlayerRadiation: death/respawn keeps the old (corpse) PlayerInstance alive instead of
        // destroying it (see PlayerInstance.DetachFromPlayerObject), so a cached PlayerRadiation
        // reference never becomes null on its own — it just stops firing events on the corpse,
        // freezing the gauge at whatever radiation value it had at death.
        if (PlayerInstance.Instance != _subscribedInstance)
            SubscribeTo(PlayerInstance.Instance);

        DecayExposureRate();
        AnimateNeedle();
        AnimateCrackOverlay();
    }

    private void OnDisable()
    {
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
            arcFillImage.color         = arcColorLow;
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

        // ── Arc and text – still reflect the overall/accumulated radiation ─────
        if (arcFillImage != null)
        {
            arcFillImage.fillAmount = normalized;
            arcFillImage.color      = Color.Lerp(arcColorLow, arcColorHigh, normalized);
        }

        if (radiationValueText != null)
            radiationValueText.text = string.Format(valueFormat, current) + valueSuffix;

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
}
