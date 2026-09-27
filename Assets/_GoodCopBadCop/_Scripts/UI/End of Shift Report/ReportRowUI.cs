using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EndOfShiftReportRow : MonoBehaviour
{
    /// <summary>Visual tone of a row: neutral grey, positive green, or negative orange.</summary>
    public enum Tone
    {
        Neutral,
        Positive,
        Negative
    }

    [Serializable]
    public struct ToneStyle
    {
        public Sprite barSprite;
        public Sprite tokenSprite;
        public Color labelColor;
        public Color valueColor;
    }

    [Header("Row")]
    [SerializeField] private GameObject root;
    [Tooltip("Drives the fade-in. Added at runtime if missing.")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("Background bar image whose sprite is swapped per tone.")]
    [SerializeField] private Image barImage;

    [Header("Left Side")]
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TMPTextReveal labelReveal;
    [SerializeField] private TMPWobbleText labelWobble;

    [Header("Right Side")]
    [SerializeField] private TextMeshProUGUI valueText;
    [SerializeField] private TMPTextReveal valueReveal;
    [SerializeField] private TMPWobbleText valueWobble;

    [Header("Coupon Icon")]
    [Tooltip("Icon that activates once the value text has finished revealing.")]
    [SerializeField] private GameObject couponIcon;
    [Tooltip("Image on the coupon icon whose sprite is swapped per tone.")]
    [SerializeField] private Image couponImage;

    [Header("Tone Styles")]
    [SerializeField] private ToneStyle neutralStyle = new ToneStyle
    {
        labelColor = new Color(0.36f, 0.22f, 0.04f),
        valueColor = new Color(0.36f, 0.22f, 0.04f)
    };
    [SerializeField] private ToneStyle positiveStyle = new ToneStyle
    {
        labelColor = new Color(0.2f, 0.45f, 0.1f),
        valueColor = new Color(0.2f, 0.45f, 0.1f)
    };
    [SerializeField] private ToneStyle negativeStyle = new ToneStyle
    {
        labelColor = new Color(0.72f, 0.3f, 0.06f),
        valueColor = new Color(0.72f, 0.3f, 0.06f)
    };

    [Header("Fade In")]
    [Tooltip("Scale the row starts at before easing up to 1 while fading in.")]
    [SerializeField] private float fadeStartScale = 0.96f;

    private ToneStyle _currentStyle;

    private void Awake()
    {
        EnsureCanvasGroup();
    }

    private void EnsureCanvasGroup()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void Hide()
    {
        if (root != null)
            root.SetActive(false);

        if (couponIcon != null)
            couponIcon.SetActive(false);

        SetFade(0f);
    }

    public void Clear()
    {
        labelText.text = " ";
        valueText.text = " ";

        if (couponIcon != null)
            couponIcon.SetActive(false);
    }

    public void Show()
    {
        if (root != null)
            root.SetActive(true);

        // Subject names make labels longer than the old fixed rows; keep them on one line.
        if (labelText != null)
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
    }

    /// <summary>Applies the bar sprite, token sprite and text colors for <paramref name="tone"/>.</summary>
    public void SetTone(Tone tone)
    {
        _currentStyle = tone switch
        {
            Tone.Positive => positiveStyle,
            Tone.Negative => negativeStyle,
            _ => neutralStyle
        };

        if (barImage != null && _currentStyle.barSprite != null)
            barImage.sprite = _currentStyle.barSprite;

        if (couponImage != null && _currentStyle.tokenSprite != null)
            couponImage.sprite = _currentStyle.tokenSprite;

        if (labelText != null)
            labelText.color = _currentStyle.labelColor;

        if (valueText != null)
            valueText.color = _currentStyle.valueColor;
    }

    /// <summary>0 = invisible and slightly shrunk, 1 = fully visible at normal scale.</summary>
    public void SetFade(float t)
    {
        EnsureCanvasGroup();
        t = Mathf.Clamp01(t);
        canvasGroup.alpha = t;
        transform.localScale = Vector3.one * Mathf.LerpUnclamped(fadeStartScale, 1f, t);
    }

    /// <summary>
    /// Eases the row in. Returned as a plain enumerator so it runs inside the caller's coroutine —
    /// the row never owns a Coroutine the report has to wait on.
    /// </summary>
    public IEnumerator FadeIn(float duration)
    {
        if (duration <= 0f)
        {
            SetFade(1f);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Ease-out cubic: quick start, soft landing.
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            SetFade(eased);
            yield return null;
        }

        SetFade(1f);
    }

    public void SetLabelInstant(string text, TMPWobbleProfile wobbleProfile)
    {
        StartWobble(labelWobble, wobbleProfile);

        if (labelReveal != null)
            labelReveal.SetTextInstant(text);
        else if (labelText != null)
            labelText.text = text;
    }

    public IEnumerator RevealLabel(string text, TMPWobbleProfile wobbleProfile, float timeout = 6f)
    {
        labelText.text = "";
        StartWobble(labelWobble, wobbleProfile);

        // Bounded + caller-driven: never yield on a Coroutine owned by the child text object, or a
        // deactivation mid-reveal deadlocks the whole report. See TMPTextReveal.RevealTextBounded.
        if (labelReveal != null)
            yield return labelReveal.RevealTextBounded(text, timeout);
        else if (labelText != null)
            labelText.text = text;
    }

    public IEnumerator RevealValue(string text, TMPWobbleProfile wobbleProfile, float timeout = 6f)
    {
        if (valueText != null)
            valueText.color = _currentStyle.valueColor;

        StartWobble(valueWobble, wobbleProfile);

        if (couponIcon != null)
            couponIcon.SetActive(true);

        if (valueReveal != null)
            yield return valueReveal.RevealTextBounded(text, timeout);
        else if (valueText != null)
            valueText.text = text;
    }

    /// <summary>
    /// Fills the row instantly, skipping all animation. Used by the report's failsafe path so a
    /// stalled or skipped reveal still ends up showing the player their actual results.
    /// </summary>
    public void SetInstant(string label, string value, Tone tone)
    {
        Show();
        SetTone(tone);
        SetFade(1f);

        if (labelReveal != null)
            labelReveal.SetTextInstant(label);
        else if (labelText != null)
            labelText.text = label;

        bool showValue = value != null;

        if (couponIcon != null)
            couponIcon.SetActive(showValue);

        if (valueReveal != null)
            valueReveal.SetTextInstant(showValue ? value : " ");
        else if (valueText != null)
            valueText.text = showValue ? value : " ";
    }

    /// <summary>
    /// Row text is intentionally static — wobble reads as noise across a dense list. The profile
    /// parameter is kept so callers don't change; the wobble component is simply held stopped.
    /// </summary>
    private static void StartWobble(TMPWobbleText wobble, TMPWobbleProfile profile)
    {
        if (wobble != null)
            wobble.StopWobble();
    }
}
