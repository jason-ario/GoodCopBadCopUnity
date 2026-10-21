using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Lays out "LABEL: value" rows for localized documents (works with world-space TextMeshPro and UGUI).
/// Each label keeps its authored left edge and grows to the right on one line; the value's left edge is
/// placed right after the label's measured text, and its width fills toward the label rect's right edge
/// (the column edge), optionally capped by <see cref="Row.maxValueWidth"/>. Re-runs whenever a label's or
/// value's text, font, or font size changes (e.g. language switch via LocalizedText).
/// </summary>
[ExecuteAlways]
public class TMPLabelValueRowLayout : MonoBehaviour
{
    [Serializable]
    public class Row
    {
        public TMP_Text label;
        public TMP_Text value;

        [Tooltip("Max value width in value-parent units. 0 = fill to the column edge (label rect's right edge).")]
        public float maxValueWidth;

        [Tooltip("Authored label font size. Captured automatically; the label is shrunk from this if it exceeds Max Label Fraction.")]
        public float labelBaseFontSize;

        [NonSerialized] public string lastLabelText;
        [NonSerialized] public string lastValueText;
        [NonSerialized] public float lastLabelSize = -1f;
        [NonSerialized] public TMP_FontAsset lastLabelFont;
    }

    [Tooltip("Gap between the end of the label text and the start of the value.")]
    [SerializeField] private float spacing = 0.4f;

    [Tooltip("Smallest width the value is allowed to shrink to.")]
    [SerializeField] private float minValueWidth = 4f;

    [Tooltip("Labels wider than this fraction of the column are shrunk so the value always keeps room.")]
    [Range(0.2f, 1f)]
    [SerializeField] private float maxLabelFraction = 0.6f;

    [SerializeField] private Row[] rows = Array.Empty<Row>();

    private void OnEnable() => RefreshAll(true);

    private void OnValidate() => RefreshAll(true);

    private void LateUpdate() => RefreshAll(false);

    /// <summary>Forces every row to be re-measured and re-positioned.</summary>
    public void Refresh() => RefreshAll(true);

    private void RefreshAll(bool force)
    {
        if (rows == null) return;
        foreach (var row in rows)
        {
            if (row == null || row.label == null || row.value == null) continue;
            if (force || IsDirty(row)) Layout(row);
        }
    }

    private static bool IsDirty(Row row) =>
        row.label.text != row.lastLabelText
        || row.value.text != row.lastValueText
        || row.label.font != row.lastLabelFont
        || !Mathf.Approximately(row.label.fontSize, row.lastLabelSize);

    private void Layout(Row row)
    {
        var labelRt = row.label.rectTransform;
        var valueRt = row.value.rectTransform;
        var valueParent = valueRt.parent;
        if (valueParent == null) return;

        if (row.labelBaseFontSize <= 0f) row.labelBaseFontSize = row.label.fontSize;

        // Label column in the value's parent space (label rect is the authored column; it isn't resized).
        Rect labelRect = labelRt.rect;
        float columnLeft = valueParent.InverseTransformPoint(labelRt.TransformPoint(new Vector3(labelRect.xMin, 0f, 0f))).x;
        float columnRight = valueParent.InverseTransformPoint(labelRt.TransformPoint(new Vector3(labelRect.xMax, 0f, 0f))).x;
        float columnWidth = Mathf.Max(0.0001f, columnRight - columnLeft);
        float labelToParentScale = columnWidth / Mathf.Max(0.0001f, labelRect.width);

        // Measure at base size; shrink the label only if it would crowd out the value.
        row.label.textWrappingMode = TextWrappingModes.NoWrap;
        if (!row.label.enableAutoSizing && !Mathf.Approximately(row.label.fontSize, row.labelBaseFontSize))
            row.label.fontSize = row.labelBaseFontSize;

        float labelWidth = row.label.GetPreferredValues(row.label.text, float.PositiveInfinity, float.PositiveInfinity).x * labelToParentScale;
        float maxLabelWidth = columnWidth * maxLabelFraction;
        if (labelWidth > maxLabelWidth && !row.label.enableAutoSizing && labelWidth > 0f)
        {
            row.label.fontSize = row.labelBaseFontSize * (maxLabelWidth / labelWidth);
            labelWidth = maxLabelWidth;
        }

        // Value: left edge right after the label text, fill toward the column edge.
        float valueLeft = columnLeft + labelWidth + spacing;
        float available = columnRight - valueLeft;
        float width = row.maxValueWidth > 0f ? Mathf.Min(row.maxValueWidth, available) : available;
        width = Mathf.Max(minValueWidth, width);

        valueRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        Vector3 lp = valueRt.localPosition;
        float pivotOffset = valueRt.pivot.x * valueRt.rect.width * valueRt.localScale.x;
        lp.x = valueLeft + pivotOffset;
        valueRt.localPosition = lp;

        row.lastLabelText = row.label.text;
        row.lastValueText = row.value.text;
        row.lastLabelFont = row.label.font;
        row.lastLabelSize = row.label.fontSize;
    }
}
