using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Wall calendar (month / day / year). Each label is re-fitted whenever its font, text or size changes, so language
/// fonts with different proportions (wide CJK digits, tall Georgian caps, condensed Oswald) render at the same visual
/// size as the authored SovietStyle font:
/// <list type="bullet">
/// <item>Height: the size is scaled so the language font's digit height matches the authored font's.</item>
/// <item>Width: the label may be at most <see cref="widthTolerance"/> times as wide as the authored font would render
/// the same text (when the authored font has the glyphs), and never wider than its box.</item>
/// </list>
/// Runs after <see cref="LocalizationTextRuntime"/> (order 1000), which swaps Day/Year to the language font in LateUpdate.
/// </summary>
[DefaultExecutionOrder(1100)]
public class Calendar : MonoBehaviour
{
    [SerializeField] TextMeshPro monthText;
    [SerializeField] TextMeshPro dayText;
    [SerializeField] TextMeshPro yearText;

    [Header("Language fit")]
    [Tooltip("How much wider than the authored font a label may render before it is shrunk.")]
    [SerializeField, Min(1f)] float widthTolerance = 1.25f;
    [Tooltip("Smallest size a label may shrink to, as a fraction of its authored size.")]
    [SerializeField, Range(0.2f, 1f)] float minSizeFraction = 0.35f;

    sealed class LabelFit
    {
        public TextMeshPro Text;
        public TMP_FontAsset ReferenceFont;
        public float BaseSize;
        public float RefDigitHeight;
        public TMP_FontAsset LastFont;
        public string LastText;
        public float LastSize = -1f;
    }

    LabelFit[] _fits = Array.Empty<LabelFit>();

    private void Awake()
    {
        _fits = new[] { CreateFit(monthText), CreateFit(dayText), CreateFit(yearText) };
    }

    private void Start()
    {
        ShiftManager.Instance.OnShiftReady += OnShiftReady;
        ShiftManager.Instance.OnShiftStart += OnShiftStart;
        Loc.LanguageChanged += OnLanguageChanged;
    }

    private void OnDestroy()
    {
        Loc.LanguageChanged -= OnLanguageChanged;
    }

    private void LateUpdate()
    {
        foreach (var fit in _fits)
            if (fit != null && fit.Text != null && NeedsFit(fit))
                ApplyFit(fit);
    }

    /// <summary>The month heading is localized, so refresh it when the language changes.</summary>
    void OnLanguageChanged()
    {
        if (this != null && ShiftManager.Instance != null)
            OnShiftReady();
    }

    void OnShiftReady()
    {
        SetDate(ShiftManager.Instance.currentMonth, ShiftManager.Instance.currentDay, ShiftManager.Instance.currentYear);
    }

    void OnShiftStart()
    {
        SetDate(ShiftManager.Instance.currentMonth, ShiftManager.Instance.currentDay, ShiftManager.Instance.currentYear);
    }

    public void SetDate(string month, string day, string year)
    {
        monthText.text = month.ToString();
        dayText.text = day.ToString();
        yearText.text = year.ToString();
    }

    // ── Language fit ─────────────────────────────────────────────────────────────

    LabelFit CreateFit(TextMeshPro t)
    {
        if (t == null) return null;

        // LocalizedText may already have swapped the font in its OnEnable; its OriginalFont is the authored one.
        var localized = t.GetComponent<LocalizedText>();
        var reference = localized != null && localized.OriginalFont != null ? localized.OriginalFont : t.font;

        // TMP auto-size would fight the fit; never wrap, so a wide font can't split "20" over two lines.
        t.enableAutoSizing = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;

        return new LabelFit
        {
            Text = t,
            ReferenceFont = reference,
            BaseSize = t.fontSize,
            RefDigitHeight = DigitHeight(reference)
        };
    }

    static bool NeedsFit(LabelFit fit)
    {
        var t = fit.Text;
        return t.font != fit.LastFont || t.text != fit.LastText || !Mathf.Approximately(t.fontSize, fit.LastSize);
    }

    void ApplyFit(LabelFit fit)
    {
        var t = fit.Text;
        string text = t.text ?? "";

        // Match the authored font's digit height.
        float size = fit.BaseSize;
        float digitHeight = DigitHeight(t.font);
        if (fit.RefDigitHeight > 0f && digitHeight > 0f) size *= fit.RefDigitHeight / digitHeight;

        // No wider than the authored font would render the same text (times the tolerance).
        float refEm = EmWidth(fit.ReferenceFont, text);
        float curEm = EmWidth(t.font, text);
        if (refEm > 0f && curEm > 0f)
            size = Mathf.Min(size, widthTolerance * fit.BaseSize * refEm / curEm);

        // Never wider than the label's box (preferred width covers fallback glyphs too).
        t.fontSize = size;
        float maxWidth = t.rectTransform.rect.width - t.margin.x - t.margin.z;
        float width = t.GetPreferredValues(text, float.PositiveInfinity, float.PositiveInfinity).x;
        if (maxWidth > 0f && width > maxWidth) size *= maxWidth / width;

        t.fontSize = Mathf.Max(size, fit.BaseSize * minSizeFraction);

        fit.LastFont = t.font;
        fit.LastText = t.text;
        fit.LastSize = t.fontSize;
    }

    /// <summary>Height of the '0' glyph per unit of font size (cap line when the font has no '0').</summary>
    static float DigitHeight(TMP_FontAsset font)
    {
        if (font == null) return 0f;
        var face = font.faceInfo;
        if (face.pointSize <= 0f) return 0f;

        float height = 0f;
        if (font.HasCharacter('0', false, true) &&
            font.characterLookupTable.TryGetValue('0', out var character) && character.glyph != null)
            height = character.glyph.metrics.height;
        if (height <= 0f) height = face.capLine;

        return height / face.pointSize * face.scale;
    }

    /// <summary>Advance width of <paramref name="text"/> per unit of font size, or -1 when the font lacks a glyph.</summary>
    static float EmWidth(TMP_FontAsset font, string text)
    {
        if (font == null || string.IsNullOrEmpty(text)) return -1f;
        var face = font.faceInfo;
        if (face.pointSize <= 0f) return -1f;

        float advance = 0f;
        foreach (char c in text)
        {
            if (char.IsControl(c)) continue;
            if (!font.HasCharacter(c, false, true) ||
                !font.characterLookupTable.TryGetValue(c, out var character) || character.glyph == null)
                return -1f;
            advance += character.glyph.metrics.horizontalAdvance * character.scale;
        }
        return advance / face.pointSize * face.scale;
    }
}
