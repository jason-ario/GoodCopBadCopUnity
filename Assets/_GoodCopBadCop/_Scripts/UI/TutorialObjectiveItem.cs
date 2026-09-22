using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Represents a single row in the tutorial objective list.
/// Owns the label for one task; completion is shown by procedurally drawing a strikethrough bar
/// across the label's text (one per wrapped line) that grows left-to-right, then holds in place
/// until the row is removed. Drawn with real UI Images rather than TMPro's built-in &lt;s&gt; tag,
/// since that tag's rendering depends on the font asset's underline glyph data and wasn't
/// reliably visible with this project's fonts.
/// Instantiated and managed by <see cref="TutorialObjectiveList"/>.
/// </summary>
public class TutorialObjectiveItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;

    [Header("Strikethrough Animation")]
    [Tooltip("Seconds for the strikethrough bar(s) to grow across the text when this objective " +
             "is marked complete. Set to 0 to apply it instantly.")]
    [SerializeField] private float strikethroughDuration = 0.25f;

    [Tooltip("Eases the strikethrough's left-to-right growth over strikethroughDuration.")]
    [SerializeField] private AnimationCurve strikethroughEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Thickness, in the label's local UI units, of the drawn strikethrough bar(s).")]
    [SerializeField] private float strikethroughThickness = 2.5f;

    [Tooltip("Color of the drawn strikethrough bar(s).")]
    [SerializeField] private Color strikethroughColor = new Color(0.86f, 0.86f, 0.83f, 1f);

    [Header("Audio")]
    [Tooltip("Played once via SFXController when this objective is fully completed (MarkComplete).")]
    [SerializeField] private AudioClip completeSound;
    [Tooltip("Volume multiplier for completeSound (before global SFX volume scaling).")]
    [SerializeField] private float completeSoundVolume = 1f;

    [Tooltip("Played whenever this objective's in-progress text changes (e.g. a counter like \"1/10\" ticking up).")]
    [SerializeField] private AudioClip progressSound;
    [Tooltip("Volume multiplier for progressSound (before global SFX volume scaling).")]
    [SerializeField] private float progressSoundVolume = 0.6f;

    [Header("Checkbox (optional)")]
    [Tooltip("Shown while this objective is incomplete. Optional — leave unassigned to skip the checkbox visual.")]
    [SerializeField] private GameObject checkboxUnchecked;
    [Tooltip("Shown once this objective is marked complete. Optional — leave unassigned to skip the checkbox visual.")]
    [SerializeField] private GameObject checkboxChecked;

    private bool _isComplete;
    private Coroutine _strikethroughRoutine;

    // Pooled strikethrough bar RectTransforms — one per wrapped text line, created on demand
    // the first time this row is completed (most rows never need more than one or two).
    private readonly List<RectTransform> _strikethroughBars = new();

    /// <summary>True once <see cref="MarkComplete"/> has been called on this row.</summary>
    public bool IsComplete => _isComplete;

    /// <summary>Prefix shown before every objective's text, e.g. "- Talk to Vlad".</summary>
    private const string BulletPrefix = "- ";

    /// <summary>Sets the display text for this objective.</summary>
    public void SetText(string text)
    {
        if (label != null)
            label.text = BulletPrefix + text;
    }

    /// <summary>
    /// Marks the objective complete: draws a strikethrough bar across each wrapped line of the
    /// label, growing left-to-right over <see cref="strikethroughDuration"/> seconds (instantly
    /// if the row is inactive/disabled and can't run a coroutine, or if the duration is 0), then
    /// swaps the checkbox visual and plays <see cref="completeSound"/>.
    /// </summary>
    public void MarkComplete()
    {
        if (_isComplete) return;
        _isComplete = true;

        if (label != null)
        {
            if (strikethroughDuration > 0f && isActiveAndEnabled)
                _strikethroughRoutine = StartCoroutine(AnimateStrikethrough());
            else
                DrawStrikethroughBars(1f);
        }

        if (checkboxUnchecked != null) checkboxUnchecked.SetActive(false);
        if (checkboxChecked != null) checkboxChecked.SetActive(true);

        SFXController.Instance?.Play(completeSound, completeSoundVolume);
    }

    /// <summary>
    /// Animates every strikethrough bar's width from 0 to its full line width in lockstep, so
    /// each wrapped line's bar visibly grows left-to-right instead of snapping on all at once.
    /// </summary>
    private IEnumerator AnimateStrikethrough()
    {
        DrawStrikethroughBars(0f);

        float elapsed = 0f;
        while (elapsed < strikethroughDuration)
        {
            elapsed += Time.deltaTime;
            float t = strikethroughEase.Evaluate(Mathf.Clamp01(elapsed / strikethroughDuration));
            DrawStrikethroughBars(t);
            yield return null;
        }

        DrawStrikethroughBars(1f);
        _strikethroughRoutine = null;
    }

    /// <summary>
    /// Reads the label's current wrapped-line layout and sizes/positions one pooled bar per
    /// line, each at <paramref name="progress"/> (0-1) of that line's full width.
    /// </summary>
    private void DrawStrikethroughBars(float progress)
    {
        label.ForceMeshUpdate();
        TMP_TextInfo info = label.textInfo;
        int lineCount = Mathf.Max(1, info.lineCount);

        EnsureBarPool(lineCount);

        for (int i = 0; i < lineCount; i++)
        {
            TMP_LineInfo lineInfo = info.lineInfo[i];
            float width = lineInfo.lineExtents.max.x - lineInfo.lineExtents.min.x;
            RectTransform bar = _strikethroughBars[i];

            if (width <= 0f)
            {
                bar.gameObject.SetActive(false);
                continue;
            }

            float y = (lineInfo.ascender + lineInfo.descender) * 0.5f;
            bar.gameObject.SetActive(true);
            bar.anchoredPosition = new Vector2(lineInfo.lineExtents.min.x, y);
            bar.sizeDelta = new Vector2(width * Mathf.Clamp01(progress), strikethroughThickness);
        }

        for (int i = lineCount; i < _strikethroughBars.Count; i++)
            _strikethroughBars[i].gameObject.SetActive(false);
    }

    /// <summary>Grows the pooled bar list to at least <paramref name="count"/>, creating new bars as needed.</summary>
    private void EnsureBarPool(int count)
    {
        while (_strikethroughBars.Count < count)
        {
            var barObject = new GameObject("Strikethrough Bar", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)barObject.transform;
            rect.SetParent(label.rectTransform, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);

            var image = barObject.GetComponent<Image>();
            image.color = strikethroughColor;
            image.raycastTarget = false;

            _strikethroughBars.Add(rect);
        }
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

        string prefixed = BulletPrefix + text;
        bool changed = label.text != prefixed;
        label.text = prefixed;

        if (changed && !_isComplete)
            SFXController.Instance?.Play(progressSound, progressSoundVolume);
    }
}
