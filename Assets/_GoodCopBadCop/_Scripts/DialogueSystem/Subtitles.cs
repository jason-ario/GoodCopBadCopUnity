using Febucci.TextAnimatorCore;
using Febucci.TextAnimatorForUnity;
using TMPro;
using UnityEngine;

public class Subtitles : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI subtitlesText;
    [SerializeField] private float maxWidth = 600f;

    public void SetText(string text, string name = null, Color nameColor = default)
    {
        
        if (name == null)
        {
            subtitlesText.text = text;
        }
        else
        {
            // Combine name and text with color tag for the name
            string colorTag = $"<color=#{ColorUtility.ToHtmlStringRGB(nameColor)}>";
            subtitlesText.text = $"{colorTag}{name}:</color> {text}";
        }
    
        // Set max width
        RectTransform rectTransform = subtitlesText.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(maxWidth, rectTransform.sizeDelta.y);

        Canvas.ForceUpdateCanvases();
    }
}
