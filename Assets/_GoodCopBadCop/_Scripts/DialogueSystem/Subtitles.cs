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
    [Tooltip("Idle alpha pulse range (min..1) while the prompt is waiting for input.")]
    [SerializeField, Range(0f, 1f)] private float promptPulseMinAlpha = 0.45f;
    [SerializeField] private float promptPulseSpeed = 3f;
    [Tooltip("Vertical idle bob distance in canvas units.")]
    [SerializeField] private float promptBobAmount = 3f;

    [Header("Speaker Name Label")]
    [Tooltip("Root of the name tag pinned to the subtitle's top-left corner. Hidden when there is no speaker name.")]
    [SerializeField] private CanvasGroup nameTag;
    [SerializeField] private TMP_Text nameLabel;
    [Tooltip("Show only the first word of the speaker name (e.g. 'Vlad' from 'Vlad Petrov').")]
    [SerializeField] private bool firstNameOnly = true;
    [Tooltip("Label color used when the speaker color is white/unset.")]
    [SerializeField] private Color defaultNameColor = new Color(1f, 0.8f, 0.35f, 1f);
    [SerializeField] private float nameTagFadeInDuration = 0.2f;

    [Header("Wobble Effect")]
    [Tooltip("TMPWobbleText component on the subtitle TMP object. Assign in the prefab.")]
    [SerializeField] private TMPWobbleText _wobbleText;

    private string originalText;
    private string lastDisplayName;
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
    private Coroutine _nameTagFade;

    private void Awake()
    {
        if (continuePrompt != null)
        {
            _promptRect = continuePrompt.transform as RectTransform;
            if (_promptRect != null) _promptBasePos = _promptRect.anchoredPosition;
        }
    }

    private void Update()
    {
        if (!IsPromptActive || continuePrompt == null) return;

        _promptActiveTime += Time.unscaledDeltaTime;

        float fadeIn = promptFadeInDuration > 0f ? Mathf.Clamp01(_promptActiveTime / promptFadeInDuration) : 1f;
        float wave = (Mathf.Cos(_promptActiveTime * promptPulseSpeed) + 1f) * 0.5f; // starts at 1
        continuePrompt.alpha = fadeIn * Mathf.Lerp(promptPulseMinAlpha, 1f, wave);

        if (_promptRect != null)
            _promptRect.anchoredPosition = _promptBasePos + Vector2.up * (Mathf.Sin(_promptActiveTime * promptPulseSpeed) * promptBobAmount);
    }

    private void UpdateNameLabel()
    {
        if (nameTag == null || nameLabel == null) return;

        string display = FormatName(lastDisplayName);
        bool hasName = !string.IsNullOrEmpty(display);
        nameTag.gameObject.SetActive(hasName);
        if (!hasName) return;

        nameLabel.text = display;
        bool useSpeakerColor = lastDisplayColor.a > 0f && lastDisplayColor != Color.white;
        nameLabel.color = useSpeakerColor ? lastDisplayColor : defaultNameColor;

        if (Application.isPlaying && nameTagFadeInDuration > 0f && isActiveAndEnabled)
        {
            if (_nameTagFade != null) StopCoroutine(_nameTagFade);
            _nameTagFade = StartCoroutine(FadeInNameTag());
        }
        else
        {
            nameTag.alpha = 1f;
        }
    }

    private IEnumerator FadeInNameTag()
    {
        float t = 0f;
        nameTag.alpha = 0f;
        while (t < nameTagFadeInDuration)
        {
            t += Time.unscaledDeltaTime;
            nameTag.alpha = Mathf.Clamp01(t / nameTagFadeInDuration);
            yield return null;
        }
        nameTag.alpha = 1f;
        _nameTagFade = null;
    }

    private string FormatName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        string trimmed = rawName.Trim();
        if (!firstNameOnly) return trimmed;
        int space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed.Substring(0, space) : trimmed;
    }

    /// <summary>Sets the subtitle text and starts the typewriter reveal.</summary>
    public void SetText(string text, string name = null, Color nameColor = default)
    {
        originalText = text;
        lastDisplayName = name;
        lastDisplayColor = nameColor;

        UpdateVisuals();
        UpdateNameLabel();
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
