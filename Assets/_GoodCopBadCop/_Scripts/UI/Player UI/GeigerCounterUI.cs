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
    [Tooltip("Maximum random jitter in degrees, scales with current radiation level.")]
    [SerializeField] private float jitterAmplitude = 5f;
    [SerializeField] private float jitterFrequency = 14f;

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

        // Jitter scales with current radiation to simulate geiger counter activity.
        float normalized = _playerRadiation != null ? _playerRadiation.Normalized : 0f;
        float jitter     = Mathf.Sin(Time.time * jitterFrequency) * jitterAmplitude * normalized;

        needle.localRotation = Quaternion.Euler(0f, 0f, _currentAngle + jitter);
    }
}
