using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Base class for all stat bars. Drives a Filled Image from a float current/max pair
/// and activates a Glow child RectTransform whenever the stat increases.
/// Subclasses subscribe to their data source and call <see cref="UpdateBar"/>.
/// </summary>
public class StatBar : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private RectTransform glowRect;
    [SerializeField] private TMP_Text percentageText;
    [SerializeField] private bool glowEnabled = true;
    [SerializeField] private float glowLingerDuration = 0.5f;
    [SerializeField] private bool glowOnDecrease = false;
    [Tooltip("Minimum fill increase per second required to activate the glow. Higher = only fast heals glow.")]
    [SerializeField] private float glowRateThreshold = 0.1f;

    private const float GlowHorizontalPadding = 30f;
    private const float GlowHeight = 59.34f;
    private const float GlowAnchoredX = -10f;
    private const float GlowAnchoredY = 13.43f;

    // Initialized to negative infinity so the very first UpdateBar call
    // produces an infinite deltaTime, yielding a rate of zero and never
    // falsely triggering the glow on startup.
    private float _lastUpdateTime = float.NegativeInfinity;
    private float _previousFillAmount;
    private Coroutine _glowCoroutine;

    protected virtual void OnDisable()
    {
        if (_glowCoroutine != null)
        {
            StopCoroutine(_glowCoroutine);
            _glowCoroutine = null;
        }

        if (glowRect != null)
            glowRect.gameObject.SetActive(false);
    }

    /// <summary>
    /// Updates the fill image and glow state. Call this from subclasses
    /// whenever the underlying stat value changes.
    /// </summary>
    protected void UpdateBar(float current, float max)
    {
        float newFill = max > 0f ? current / max : 0f;

        // fillImage is optional — some stat bars (e.g. CheckpointIntegrityBar) are
        // percentage-text-only with no fill graphic. Previously this bailed out entirely
        // when fillImage was null, which also silently skipped the percentageText update
        // below, leaving those text-only bars permanently stuck at their editor-authored
        // placeholder value no matter how the underlying stat changed.
        if (fillImage != null)
            fillImage.fillAmount = newFill;

        if (percentageText != null)
            percentageText.text = $"{Mathf.RoundToInt(newFill * 100f)}%";

        bool isChangeSignificant = glowOnDecrease 
            ? newFill < _previousFillAmount 
            : newFill > _previousFillAmount;

        if (isChangeSignificant && glowEnabled && glowRect != null)
        {
            float deltaTime = Time.time - _lastUpdateTime;
            float rate = deltaTime > 0f ? Mathf.Abs(newFill - _previousFillAmount) / deltaTime : 0f;

            if (rate >= glowRateThreshold)
            {
                glowRect.gameObject.SetActive(true);

                if (_glowCoroutine != null)
                    StopCoroutine(_glowCoroutine);

                _glowCoroutine = StartCoroutine(DisableGlowAfterDelay());
            }
        }

        _previousFillAmount = newFill;
        _lastUpdateTime = Time.time;
        SyncGlowWidth();
    }

    private void SyncGlowWidth()
    {
        if (glowRect == null || fillImage == null) return;

        float filledWidth = Mathf.Max(0f, fillImage.rectTransform.rect.width * fillImage.fillAmount);
        glowRect.sizeDelta = new Vector2(filledWidth + GlowHorizontalPadding, GlowHeight);
        glowRect.anchoredPosition = new Vector2(GlowAnchoredX, GlowAnchoredY);
    }

    private IEnumerator DisableGlowAfterDelay()
    {
        yield return new WaitForSeconds(glowLingerDuration);
        if (glowRect != null)
            glowRect.gameObject.SetActive(false);
        _glowCoroutine = null;
    }

    /// <summary>Shows the stat bar.</summary>
    public void Show() => gameObject.SetActive(true);

    /// <summary>Hides the stat bar.</summary>
    public void Hide() => gameObject.SetActive(false);
}
