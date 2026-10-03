using TMPro;
using UnityEngine;
using System.Collections;
using System.Text;

public class Subtitles : MonoBehaviour
{
    [SerializeField] private TMPTextReveal textReveal;
    [SerializeField] private int maxCharactersPerLine = 50;

    [Header("Continue Chevron")]
    [Tooltip("Small downward chevron at the bottom of the black bar, shown once the line is fully revealed.")]
    [SerializeField] private CanvasGroup continueChevron;
    [SerializeField] private float chevronFadeInDuration = 0.2f;
    [Tooltip("Seconds per full pulse cycle.")]
    [SerializeField] private float chevronPulsePeriod = 1.4f;
    [SerializeField, Range(0f, 1f)] private float chevronPulseMinAlpha = 0.35f;

    [Header("Speaker Name Tag Template")]
    [Tooltip("Visual template for the conversation name tag. Always hidden on the subtitle itself; " +
             "ConversationNameTag clones it once and owns the on-screen tag for the whole conversation.")]
    [SerializeField] private CanvasGroup nameTag;

    /// <summary>Template used by <see cref="ConversationNameTag"/> to build its standalone tag.</summary>
    public CanvasGroup NameTagTemplate => nameTag;

    [Header("Wobble Effect")]
    [Tooltip("TMPWobbleText component on the subtitle TMP object. Assign in the prefab.")]
    [SerializeField] private TMPWobbleText _wobbleText;

    private string originalText;
    private Color lastDisplayColor;

    public bool IsPromptActive { get; private set; }

    // -------------------------------------------------------------------------
    // Wobble API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies the given wobble <paramref name="profile"/> to the subtitle text.
    /// Pass <c>null</c> to stop any active wobble.
    /// </summary>
    public void SetWobble(TMPWobbleProfile profile)
    {
        if (_wobbleText == null) return;

        if (profile != null)
        {
            _wobbleText.SetProfile(profile, restartSeeds: true);
            _wobbleText.StartWobble();
        }
        else
        {
            _wobbleText.StopWobble();
        }
    }

    /// <summary>
    /// Applies the given font <paramref name="font"/> override to the subtitle text.
    /// Pass <c>null</c> to leave the subtitle prefab's default font untouched — since each
    /// subtitle is a fresh prefab instance, no explicit reset is needed between lines.
    /// </summary>
    public void SetFontOverride(TMP_FontAsset font)
    {
        if (font == null || textReveal == null) return;

        var tmp = textReveal.GetComponent<TextMeshProUGUI>();
        if (tmp != null)
            tmp.font = font;
    }

    /// <summary>
    /// Arms (or disarms) advance-input gating. When armed, <see cref="IsPromptActive"/> becomes
    /// true once the typewriter finishes and the continue chevron fades in.
    /// </summary>
    public void ShowContinuePrompt(bool show)
    {
        if (show)
            StartCoroutine(ActivatePromptAfterTypewriter());
        else
        {
            IsPromptActive = false;
            if (continueChevron != null) continueChevron.alpha = 0f;
        }
    }

    private IEnumerator ActivatePromptAfterTypewriter()
    {
        IsPromptActive = false;
        if (continueChevron != null) continueChevron.alpha = 0f;

        if (textReveal != null)
            yield return new WaitUntil(() => !textReveal.IsRevealing);

        // Input gating depends only on IsPromptActive; the chevron is purely cosmetic.
        IsPromptActive = true;

        if (continueChevron == null) yield break;

        // Fade in, then pulse until the player advances.
        float t = 0f;
        while (IsPromptActive)
        {
            t += Time.unscaledDeltaTime;
            float fadeIn = chevronFadeInDuration > 0f ? Mathf.Clamp01(t / chevronFadeInDuration) : 1f;
            float wave = 0.5f + 0.5f * Mathf.Cos(t * Mathf.PI * 2f / chevronPulsePeriod); // starts at 1
            continueChevron.alpha = fadeIn * Mathf.Lerp(chevronPulseMinAlpha, 1f, wave);
            yield return null;
        }
    }

    private void Awake()
    {
        if (continueChevron != null)
            continueChevron.alpha = 0f;

        // The speaker name is shown by the standalone ConversationNameTag, never on the subtitle bar.
        if (nameTag != null)
            nameTag.gameObject.SetActive(false);
    }

    /// <summary>
    /// Sets the subtitle text and starts the typewriter reveal. <paramref name="name"/> is kept for
    /// call-site compatibility; the speaker name is displayed by <see cref="ConversationNameTag"/>.
    /// </summary>
    public void SetText(string text, string name = null, Color nameColor = default)
    {
        originalText = text;
        lastDisplayColor = nameColor;

        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (textReveal == null || string.IsNullOrEmpty(originalText)) return;

        // [[keyword]] markup -> bold/yellow/link-tagged rich text; TMPWobbleText animates the link range.
        string wrappedText = WrapText(DialogueKeywordMarkup.Apply(originalText), maxCharactersPerLine);
        string colorHex = ColorUtility.ToHtmlStringRGB(lastDisplayColor);
        string formattedText = $"<color=#{colorHex}>{wrappedText}</color>";

        var tmp = textReveal.GetComponent<TextMeshProUGUI>();
        if (tmp != null)
            tmp.textWrappingMode = TextWrappingModes.Normal;

        // Use typewriter reveal in play mode; instant set in editor to keep OnValidate previews fast.
        if (Application.isPlaying)
            textReveal.RevealText(formattedText);
        else
            textReveal.SetTextInstant(formattedText);
    }

    private string WrapText(string text, int maxChars)
    {
        // Measure rendered length only, so rich-text tags (keyword emphasis) don't cause early wraps.
        if (string.IsNullOrEmpty(text) || DialogueKeywordMarkup.VisibleLength(text) <= maxChars)
            return text;

        StringBuilder sb = new StringBuilder();
        string[] words = text.Split(' ');
        int currentLineLength = 0;

        foreach (string word in words)
        {
            int wordLength = DialogueKeywordMarkup.VisibleLength(word);
            if (currentLineLength + wordLength + 1 > maxChars)
            {
                sb.Append('\n');
                currentLineLength = 0;
            }
            else if (currentLineLength > 0)
            {
                sb.Append(' ');
                currentLineLength++;
            }

            sb.Append(word);
            currentLineLength += wordLength;
        }

        return sb.ToString();
    }

    void OnValidate()
    {
        if (!Application.isPlaying && textReveal != null)
        {
            var tmp = textReveal.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                string currentText = tmp.text;
                if (currentText.StartsWith("<color=#") && currentText.EndsWith("</color>"))
                {
                    int start = currentText.IndexOf('>') + 1;
                    int end = currentText.LastIndexOf("</color>");
                    originalText = currentText.Substring(start, end - start);
                }
                else
                {
                    originalText = currentText;
                }
            }
        }

        UpdateVisuals();
    }
}
