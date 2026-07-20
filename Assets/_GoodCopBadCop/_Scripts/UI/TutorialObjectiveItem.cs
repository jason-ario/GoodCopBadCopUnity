using TMPro;
using UnityEngine;

/// <summary>
/// Represents a single row in the tutorial objective list.
/// Owns the label for one task; completion is shown via TMPro strikethrough markup.
/// Instantiated and managed by <see cref="TutorialObjectiveList"/>.
/// </summary>
public class TutorialObjectiveItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;

    /// <summary>Sets the display text for this objective.</summary>
    public void SetText(string text)
    {
        if (label != null)
            label.text = text;
    }

    /// <summary>Applies a TMPro strikethrough to the label text to signal task completion.</summary>
    public void MarkComplete()
    {
        if (label != null)
            label.text = $"<s>{label.text}</s>";
    }
}
