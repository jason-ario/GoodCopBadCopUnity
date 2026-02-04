using Febucci.TextAnimatorCore;
using Febucci.TextAnimatorForUnity;
using TMPro;
using UnityEngine;
using System.Text;

public class Subtitles : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI subtitlesText;
    [SerializeField] private int maxCharactersPerLine = 50;

    private string originalText; // Stores the raw text without manual breaks
    private string lastDisplayName;
    private Color lastDisplayColor;

    public void SetText(string text, string name = null, Color nameColor = default)
    {
        // Store these so OnValidate can refresh the look
        originalText = text;
        lastDisplayName = name;
        lastDisplayColor = nameColor;

        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (subtitlesText == null || string.IsNullOrEmpty(originalText)) return;

        string wrappedText = WrapText(originalText, maxCharactersPerLine);
        string colorHex = ColorUtility.ToHtmlStringRGB(lastDisplayColor);

        // Apply color to the entire wrapped text body and remove the name prefix
        subtitlesText.text = $"<color=#{colorHex}>{wrappedText}</color>";

        subtitlesText.enableWordWrapping = true;
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
            // +1 for the space
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
        // If the game is running, we use the stored original text.
        // If we are just editing in the inspector, we grab what's currently in the text box.
        if (!Application.isPlaying && subtitlesText != null)
        {
            string currentText = subtitlesText.text;
            
            // Extract content between color tags if present
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

        UpdateVisuals();
    }
}
