using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BatteryBar : MonoBehaviour
{
    [SerializeField] private Image fillImage;

    [Header("Segments")]
    [Tooltip("Optional discrete cells ordered left to right. When assigned they are used instead of the fill image.")]
    [SerializeField] private Image[] segments;
    [Range(0f, 1f)] [SerializeField] private float emptySegmentAlpha = 0.15f;

    [Header("Optional Visuals")]
    [Tooltip("Lightning icon tinted with the current charge colour.")]
    [SerializeField] private Image iconImage;
    [Tooltip("Label showing the charge as a percentage.")]
    [SerializeField] private TMP_Text percentText;

    [Header("Charge Colours")]
    [SerializeField] private Color highColor = new Color(0.42f, 0.86f, 0.38f, 1f);
    [SerializeField] private Color midColor = new Color(1f, 0.74f, 0.2f, 1f);
    [SerializeField] private Color lowColor = new Color(0.95f, 0.22f, 0.16f, 1f);
    [Range(0f, 1f)] [SerializeField] private float midThreshold = 0.5f;
    [Range(0f, 1f)] [SerializeField] private float lowThreshold = 0.2f;

    [Header("Low Charge Pulse")]
    [SerializeField] private float pulseSpeed = 6f;
    [Range(0f, 1f)] [SerializeField] private float pulseMinAlpha = 0.35f;

    private float _fill = 1f;
    private int _lastPercent = -1;

    private void Awake()
    {
        if (fillImage != null)
        {
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        }
    }

    private void Update()
    {
        Color color = EvaluateColor(_fill);

        if (_fill <= lowThreshold)
        {
            float t = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f;
            color.a = Mathf.Lerp(pulseMinAlpha, 1f, t);
        }

        if (fillImage != null) fillImage.color = color;
        if (iconImage != null) iconImage.color = new Color(color.r, color.g, color.b, 1f);
        UpdateSegments(color);
    }

    private void UpdateSegments(Color color)
    {
        if (segments == null || segments.Length == 0) return;

        float lit = _fill * segments.Length;
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null) continue;
            float cellFill = Mathf.Clamp01(lit - i);
            Color c = color;
            c.a = Mathf.Lerp(emptySegmentAlpha, color.a, cellFill);
            segments[i].color = c;
        }
    }

    public void UpdateBar(InternalBattery internalBattery)
    {
        if (internalBattery != null)
            UpdateBar(internalBattery.GetBatteryPercentage());
    }

    /// <summary>Updates the bar fill directly from a 0–1 normalised percentage.</summary>
    public void UpdateBar(float fillPercent)
    {
        _fill = Mathf.Clamp01(fillPercent);

        if (fillImage != null)
            fillImage.fillAmount = _fill;

        int percent = Mathf.RoundToInt(_fill * 100f);
        if (percentText != null && percent != _lastPercent)
        {
            _lastPercent = percent;
            percentText.SetText("{0}%", percent);
        }
    }

    public void Show()
    {
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private Color EvaluateColor(float fill)
    {
        if (fill <= lowThreshold) return lowColor;
        if (fill <= midThreshold)
            return Color.Lerp(lowColor, midColor, Mathf.InverseLerp(lowThreshold, midThreshold, fill));
        return Color.Lerp(midColor, highColor, Mathf.InverseLerp(midThreshold, 1f, fill));
    }
}
