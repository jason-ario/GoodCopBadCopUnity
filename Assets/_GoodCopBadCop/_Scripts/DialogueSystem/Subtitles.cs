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
            tmp.font = LocalizationSettings.Localize(font);
    }

    /// <summary>
    /// Swaps the prefab's authored subtitle font for the current language's font. Uses the font's
    /// assigned role, falling back to the "Dialogue" role when the font isn't assigned to any.
    /// </summary>
    private void ApplyLanguageFont()
    {
        if (textReveal == null) return;
        var tmp = textReveal.GetComponent<TextMeshProUGUI>();
        if (tmp == null || tmp.font == null) return;

        var settings = LocalizationSettings.Instance;
        string role = settings != null ? settings.EffectiveRoleOf(tmp.font) : null;
        if (role == null && settings != null && settings.HasRole(DialogueFontRole)) role = DialogueFontRole;
        var localized = LocalizationSettings.Localize(tmp.font, role);
        if (localized != null && localized != tmp.font) tmp.font = localized;
    }

    private const string DialogueFontRole = "Dialogue";

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

        if (Application.isPlaying) ApplyLanguageFont();
    }

    // -------------------------------------------------------------------------
    // Trailer Console hide flag
    // -------------------------------------------------------------------------

    /// <summary>True while the Trailer Console hides all subtitle instances.</summary>
    public static bool TrailerHidden { get; private set; }

    /// <summary>Raised when <see cref="TrailerHidden"/> changes.</summary>
    public static event System.Action<bool> TrailerHiddenChanged;

    private CanvasGroup _trailerCanvasGroup;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetTrailerStatics()
    {
        TrailerHidden = false;
        TrailerHiddenChanged = null;
    }

    /// <summary>
    /// Hides/shows every subtitle (current and future instances) for trailer capture. Each subtitle
    /// is a fresh prefab instance, so new instances read <see cref="TrailerHidden"/> on enable.
    /// </summary>
    public static void SetTrailerHidden(bool hidden)
    {
        if (TrailerHidden == hidden) return;
        TrailerHidden = hidden;
        TrailerHiddenChanged?.Invoke(hidden);
    }

    private void OnEnable()
    {
        TrailerHiddenChanged += ApplyTrailerHidden;
        ApplyTrailerHidden(TrailerHidden);
    }

    private void OnDisable()
    {
        TrailerHiddenChanged -= ApplyTrailerHidden;
    }

    private void ApplyTrailerHidden(bool hidden)
    {
        if (!Application.isPlaying) return;

        if (_trailerCanvasGroup == null)
        {
            // Only add a group when actually hiding; an existing one is reused either way.
            _trailerCanvasGroup = GetComponent<CanvasGroup>();
            if (_trailerCanvasGroup == null)
            {
                if (!hidden) return;
                _trailerCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        // OverhearSubtitleGate owns this group's alpha every frame (and honors TrailerHidden).
        if (!hidden && TryGetComponent(out OverhearSubtitleGate _)) return;

        _trailerCanvasGroup.alpha = hidden ? 0f : 1f;
    }

    /// <summary>
    /// Sets the subtitle text and starts the typewriter reveal. <paramref name="name"/> is kept for
    /// call-site compatibility; the speaker name is displayed by <see cref="ConversationNameTag"/>.
    /// </summary>
    public void SetText(string text, string name = null, Color nameColor = default)
    {
        // Localize the incoming source string before keyword markup, wrapping and color are applied.
        originalText = Loc.TranslateSource(text);
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

    /// <summary>
    /// Inserts line breaks so no line exceeds <paramref name="maxChars"/> half-width units. The bar's
    /// ContentSizeFitter sizes to the text's preferred width, so TMP never wraps on its own: these
    /// manual breaks are the only wrapping. Full-width characters (CJK, kana, Hangul, full-width
    /// punctuation) count as 2 units. Lines break at spaces, and also between Chinese/Japanese
    /// characters (which have no spaces), following basic kinsoku rules: no line starts with closing
    /// punctuation or small kana, and none ends with opening brackets. Korean keeps breaking at spaces.
    /// Rich-text tags (keyword emphasis) have no width and are never split.
    /// </summary>
    private static string WrapText(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || maxChars <= 0) return text;

        var sb = new StringBuilder(text.Length + 8);
        int lineWidth = 0;
        int breakAt = -1;           // index in sb where a break may go
        bool breakReplaces = false; // true when sb[breakAt] is a space to turn into the newline
        int widthAfterBreak = 0;    // visible width written after breakAt
        char prev = '\0';           // previous visible character on this line

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '<')
            {
                int close = text.IndexOf('>', i);
                if (close > i)
                {
                    sb.Append(text, i, close - i + 1);
                    i = close;
                    continue;
                }
            }

            if (c == '\n')
            {
                sb.Append(c);
                lineWidth = 0;
                breakAt = -1;
                prev = '\0';
                continue;
            }

            if (c == ' ')
            {
                breakAt = sb.Length;
                breakReplaces = true;
                widthAfterBreak = 0;
                sb.Append(c);
                lineWidth++;
                prev = c;
                continue;
            }

            if (prev != '\0' && prev != ' ' && (BreaksAnywhere(prev) || BreaksAnywhere(c)) &&
                NoLineStart.IndexOf(c) < 0 && NoLineEnd.IndexOf(prev) < 0)
            {
                breakAt = sb.Length;
                breakReplaces = false;
                widthAfterBreak = 0;
            }

            int w = IsFullWidth(c) ? 2 : 1;
            if (lineWidth + w > maxChars && breakAt >= 0)
            {
                if (breakReplaces) sb[breakAt] = '\n';
                else sb.Insert(breakAt, '\n');
                lineWidth = widthAfterBreak;
                breakAt = -1;
            }

            sb.Append(c);
            lineWidth += w;
            widthAfterBreak += w;
            prev = c;
        }

        return sb.ToString();
    }

    /// <summary>Closing punctuation, prolonged sound mark and small kana: never start a line (kinsoku).</summary>
    private const string NoLineStart =
        "、。，．・：；？！‼⁇⁈⁉ー〜～゛゜ヽヾゝゞ々〻）」』】〕〉》〙〗］｝｠’”…‥" +
        "ぁぃぅぇぉっゃゅょゎゕゖァィゥェォッャュョヮヵヶ" +
        ")]}!?,.:;%";

    /// <summary>Opening brackets and quotes: never end a line.</summary>
    private const string NoLineEnd = "（「『【〔〈《〘〖［｛｟‘“([{";

    /// <summary>Visually double-width characters: CJK, kana, Hangul and full-width forms.</summary>
    private static bool IsFullWidth(char c) =>
        (c >= '\u1100' && c <= '\u115F') ||
        (c >= '\u2E80' && c <= '\uA4CF') ||
        (c >= '\uAC00' && c <= '\uD7A3') ||
        (c >= '\uF900' && c <= '\uFAFF') ||
        (c >= '\uFE30' && c <= '\uFE4F') ||
        (c >= '\uFF00' && c <= '\uFF60') ||
        (c >= '\uFFE0' && c <= '\uFFE6');

    /// <summary>Scripts written without spaces (Chinese, Japanese) may break between any two characters. Hangul breaks at spaces.</summary>
    private static bool BreaksAnywhere(char c) =>
        IsFullWidth(c) && !(c >= '\u1100' && c <= '\u115F') && !(c >= '\uAC00' && c <= '\uD7A3') &&
        !(c >= '\u3130' && c <= '\u318F');

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
