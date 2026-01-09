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

        if (string.IsNullOrEmpty(lastDisplayName))
        {
            subtitlesText.text = wrappedText;
        }
        else
        {
            string colorTag = $"<color=#{ColorUtility.ToHtmlStringRGB(lastDisplayColor)}>";
            subtitlesText.text = $"{colorTag}{lastDisplayName}:</color> {wrappedText}";
        }

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
            // Try to extract the text part if there are tags
            string currentText = subtitlesText.text;
            
            // Basic check to see if we have a colon (meaning a name tag is likely present)
            if (currentText.Contains(":</color> "))
            {
                int index = currentText.IndexOf(":</color> ") + 10;
                originalText = currentText.Substring(index);
            }
            else
            {
                originalText = currentText;
            }
        }

        UpdateVisuals();
    }
}
