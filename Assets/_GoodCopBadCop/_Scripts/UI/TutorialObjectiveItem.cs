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

    [Header("Audio")]
    [Tooltip("Played once via SFXController when this objective is fully completed (MarkComplete).")]
    [SerializeField] private AudioClip completeSound;
    [Tooltip("Volume multiplier for completeSound (before global SFX volume scaling).")]
    [SerializeField] private float completeSoundVolume = 1f;

    [Tooltip("Played whenever this objective's in-progress text changes (e.g. a counter like \"1/10\" ticking up).")]
    [SerializeField] private AudioClip progressSound;
    [Tooltip("Volume multiplier for progressSound (before global SFX volume scaling).")]
    [SerializeField] private float progressSoundVolume = 0.6f;

    private bool _isComplete;

    /// <summary>Sets the display text for this objective.</summary>
    public void SetText(string text)
    {
        if (label != null)
            label.text = text;
    }

    /// <summary>Applies a TMPro strikethrough to the label text to signal task completion.</summary>
    public void MarkComplete()
    {
        if (_isComplete) return;
        _isComplete = true;

        if (label != null)
            label.text = $"<s>{label.text}</s>";

        SFXController.Instance?.Play(completeSound, completeSoundVolume);
    }

    /// <summary>
    /// Replaces the display text in place (e.g. to refresh a progress count like "0/3").
    /// Plays <see cref="progressSound"/> whenever the text actually changes, so a "1/10 -> 2/10"
    /// style counter gets a mini success cue on every increment without spamming rows whose
    /// text is unchanged. Does not affect completion state — call before <see cref="MarkComplete"/>.
    /// </summary>
    public void UpdateText(string text)
    {
        if (label == null) return;

        bool changed = label.text != text;
        label.text = text;

        if (changed && !_isComplete)
            SFXController.Instance?.Play(progressSound, progressSoundVolume);
    }
}
