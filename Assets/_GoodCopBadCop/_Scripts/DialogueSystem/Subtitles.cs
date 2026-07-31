using TMPro;
using UnityEngine;
using System.Collections;
using System.Text;

public class Subtitles : MonoBehaviour
{
    [SerializeField] private TMPTextReveal textReveal;
    [SerializeField] private int maxCharactersPerLine = 50;
    [SerializeField] private CanvasGroup continuePrompt;

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

        IsPromptActive = true;
        continuePrompt.alpha = 1;
    }

    /// <summary>Sets the subtitle text and starts the typewriter reveal.</summary>
    public void SetText(string text, string name = null, Color nameColor = default)
    {
        originalText = text;
        lastDisplayName = name;
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
            tmp.enableWordWrapping = true;

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
