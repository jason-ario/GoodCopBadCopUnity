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
    [Tooltip("Pixel amplitude of the per-character sine-wave displacement.")]
    [SerializeField] private float wobbleAmount = 10f;

    [Tooltip("Speed multiplier of the wobble sine wave.")]
    [SerializeField] private float wobbleSpeed = 3f;

    private string originalText;
    private string lastDisplayName;
    private Color lastDisplayColor;

    private bool _wobbling;
    private Coroutine _wobbleCoroutine;

    public bool IsPromptActive { get; private set; }

    // -------------------------------------------------------------------------
    // Wobble API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts or stops the per-character vertex-wobble animation on the subtitle text.
    /// Safe to call before or after <see cref="SetText"/>.
    /// </summary>
    public void SetWobble(bool wobble)
    {
        _wobbling = wobble;

        if (wobble && _wobbleCoroutine == null)
            _wobbleCoroutine = StartCoroutine(WobbleCoroutine());
        else if (!wobble && _wobbleCoroutine != null)
        {
            StopCoroutine(_wobbleCoroutine);
            _wobbleCoroutine = null;
        }
    }

    private void OnDisable()
    {
        // Stop wobble so TMP mesh is not left in a dirty state after the GO is destroyed.
        if (_wobbleCoroutine != null)
        {
            StopCoroutine(_wobbleCoroutine);
            _wobbleCoroutine = null;
        }
        _wobbling = false;
    }

    /// <summary>
    /// Each frame, displaces each visible character along a sine wave to create a shaky,
    /// angry wobble effect. Uses TMP's mesh-modification API so it works with rich text
    /// and the typewriter reveal simultaneously.
    /// </summary>
    private IEnumerator WobbleCoroutine()
    {
        var tmp = textReveal != null ? textReveal.GetComponent<TextMeshProUGUI>() : null;
        if (tmp == null) yield break;

        while (_wobbling)
        {
            tmp.ForceMeshUpdate();
            var textInfo = tmp.textInfo;

            for (int i = 0; i < textInfo.characterCount; i++)
            {
                var charInfo = textInfo.characterInfo[i];
                if (!charInfo.isVisible) continue;

                var verts = textInfo.meshInfo[charInfo.materialReferenceIndex].vertices;

                // Offset all four corners of the character quad independently so the
                // letter itself appears to twist as well as translate.
                for (int j = 0; j < 4; j++)
                {
                    int idx = charInfo.vertexIndex + j;
                    float phase = Time.time * wobbleSpeed + i * 1.3f + j * 0.5f;
                    verts[idx] += new Vector3(
                        Mathf.Sin(phase)            * wobbleAmount,
                        Mathf.Cos(phase + i * 0.9f) * wobbleAmount,
                        0f);
                }
            }

            for (int i = 0; i < textInfo.meshInfo.Length; i++)
            {
                var mesh = textInfo.meshInfo[i].mesh;
                mesh.vertices = textInfo.meshInfo[i].vertices;
                tmp.UpdateGeometry(mesh, i);
            }

            yield return null;
        }
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
