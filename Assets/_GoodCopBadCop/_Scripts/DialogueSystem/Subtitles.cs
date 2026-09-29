using TMPro;
using UnityEngine;
using System.Collections;
using System.Text;

public class Subtitles : MonoBehaviour
{
    [SerializeField] private TMPTextReveal textReveal;
    [SerializeField] private int maxCharactersPerLine = 50;
    [SerializeField] private CanvasGroup continuePrompt;

    [Header("Continue Prompt Animation")]
    [Tooltip("Seconds for the continue prompt to fade in once the typewriter finishes.")]
    [SerializeField] private float promptFadeInDuration = 0.25f;
    [Tooltip("Speed of the idle vertical bob.")]
    [SerializeField] private float promptPulseSpeed = 3f;
    [Tooltip("Vertical idle bob distance in canvas units.")]
    [SerializeField] private float promptBobAmount = 3f;

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

    /// <summary>Shows or hides the continue prompt, waiting for the typewriter to finish before showing it.</summary>
    public void ShowContinuePrompt(bool show)
    {
        if (continuePrompt == null) return;

        if (show)
            StartCoroutine(ShowPromptAfterTypewriter());
        else
        {
            IsPromptActive = false;
            continuePrompt.alpha = 0;
            if (_promptRect != null) _promptRect.anchoredPosition = _promptBasePos;
        }
    }

    private IEnumerator ShowPromptAfterTypewriter()
    {
        IsPromptActive = false;
        continuePrompt.alpha = 0;

        if (textReveal != null)
        {
            yield return new WaitUntil(() => !textReveal.IsRevealing);
        }

        // Input gating depends only on IsPromptActive; the fade/pulse is purely cosmetic.
        IsPromptActive = true;
        _promptActiveTime = 0f;
    }

    private RectTransform _promptRect;
    private Vector2 _promptBasePos;
    private float _promptActiveTime;

    private void Awake()
    {
        if (continuePrompt != null)
        {
            _promptRect = continuePrompt.transform as RectTransform;
            if (_promptRect != null) _promptBasePos = _promptRect.anchoredPosition;
        }

        // The speaker name is shown by the standalone ConversationNameTag, never on the subtitle bar.
        if (nameTag != null)
            nameTag.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!IsPromptActive || continuePrompt == null) return;

        _promptActiveTime += Time.unscaledDeltaTime;

        float fadeIn = promptFadeInDuration > 0f ? Mathf.Clamp01(_promptActiveTime / promptFadeInDuration) : 1f;
        continuePrompt.alpha = fadeIn;

        if (_promptRect != null)
            _promptRect.anchoredPosition = _promptBasePos + Vector2.up * (Mathf.Sin(_promptActiveTime * promptPulseSpeed) * promptBobAmount);
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

        string wrappedText = WrapText(originalText, maxCharactersPerLine);
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
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        StringBuilder sb = new StringBuilder();
        string[] words = text.Split(' ');
        int currentLineLength = 0;

        foreach (string word in words)
        {
            if (currentLineLength + word.Length + 1 > maxChars)
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
            currentLineLength += word.Length;
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
