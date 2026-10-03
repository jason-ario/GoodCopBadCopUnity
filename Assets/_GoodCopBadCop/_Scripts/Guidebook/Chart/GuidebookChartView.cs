using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A ruled table printed on a guidebook page. Row 0 is the header row (centred, bold).</summary>
public sealed class GuidebookTable
{
    /// <summary>Optional bold caption printed above the grid.</summary>
    public string Caption;

    /// <summary>Relative column widths (normalised at layout time).</summary>
    public float[] ColumnWidths;

    /// <summary>Per-column alignment for body rows. Header cells are always centred.</summary>
    public TextAlignmentOptions[] Alignments;

    public readonly List<string[]> Rows = new List<string[]>();
}

/// <summary>
/// Draws <see cref="GuidebookTable"/>s and an optional boxed warning ("callout") onto a guidebook
/// face, in the style of a printed checkpoint chart: ruled grid, heavier outer border, centred
/// header row. Created at runtime by <see cref="GuidebookFaceView"/> as a child of the face's Layout.
///
/// Cell text copies the face body's font, lit material and ink colour; rules reuse the divider's
/// lit material, so the chart is lit like the rest of the page. Elements are pooled and rebuilt
/// only when the book opens.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class GuidebookChartView : MonoBehaviour
{
    // Layout-space units (same as GuidebookFaceView).
    private const float OuterRule   = 2.5f;
    private const float InnerRule   = 1.25f;
    private const float CellPadding = 7f;
    private const float CaptionGap  = 4f;
    private const float TableGap    = 14f;
    private const float MinRowScale = 0.6f;
    // Rows may grow past their nominal height to use leftover page space.
    private const float MaxRowScale = 1.4f;

    private float _rowHeight      = 34f;
    private float _headerHeight   = 34f;
    private float _captionHeight  = 22f;
    private float _maxFontSize    = 17f;
    private float _minFontSize    = 10f;

    private readonly List<TextMeshProUGUI> _texts = new List<TextMeshProUGUI>();
    private readonly List<Image>           _rules = new List<Image>();
    private int _textCursor;
    private int _ruleCursor;

    private TMP_Text _style;
    private Material _ruleMaterial;
    private Color    _ink;

    private RectTransform Rect => (RectTransform)transform;

    /// <summary>Cell text style (font, material, colour) and rule material for all following builds.</summary>
    public void Configure(TMP_Text style, Material ruleMaterial, Color ink)
    {
        _style        = style;
        _ruleMaterial = ruleMaterial;
        _ink          = ink;
        if (style != null) _maxFontSize = Mathf.Max(_minFontSize, style.fontSizeMax - 1f);
    }

    public void Clear()
    {
        _textCursor = _ruleCursor = 0;
        HideUnused();
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Lays out the tables then the callout from the top of this rect (pivot top-centre),
    /// shrinking row heights if needed to fit <paramref name="availableHeight"/>.
    /// Returns the height used.
    /// </summary>
    public float Build(IReadOnlyList<GuidebookTable> tables, string callout, float availableHeight)
    {
        gameObject.SetActive(true);
        _textCursor = _ruleCursor = 0;

        float width = Rect.rect.width;
        int tableCount = tables != null ? tables.Count : 0;

        float calloutHeight = string.IsNullOrEmpty(callout) ? 0f : CalloutHeight(callout, width);

        // Fixed parts (captions, gaps, callout) never shrink; rows do.
        float fixedHeight = calloutHeight;
        float rowsHeight  = 0f;
        for (int i = 0; i < tableCount; i++)
        {
            GuidebookTable table = tables[i];
            if (!string.IsNullOrEmpty(table.Caption)) fixedHeight += _captionHeight + CaptionGap;
            if (table.Rows.Count > 0) rowsHeight += _headerHeight + (table.Rows.Count - 1) * _rowHeight;
            if (i > 0) fixedHeight += TableGap;
        }
        if (calloutHeight > 0f && tableCount > 0) fixedHeight += TableGap;

        float rowScale = rowsHeight > 0f
            ? Mathf.Clamp((availableHeight - fixedHeight) / rowsHeight, MinRowScale, MaxRowScale)
            : 1f;

        float y = 0f;
        for (int i = 0; i < tableCount; i++)
        {
            if (i > 0) y -= TableGap;
            y = LayoutTable(tables[i], y, width, rowScale);
        }

        if (calloutHeight > 0f)
        {
            if (tableCount > 0) y -= TableGap;
            y = LayoutCallout(callout, y, width, calloutHeight);
        }

        HideUnused();

        float used = -y;
        Vector2 size = Rect.sizeDelta;
        size.y = used;
        Rect.sizeDelta = size;
        return used;
    }

    // ── Tables ────────────────────────────────────────────────────────────────

    private float LayoutTable(GuidebookTable table, float top, float width, float rowScale)
    {
        float y = top;

        if (!string.IsNullOrEmpty(table.Caption))
        {
            PlaceText($"<b>{table.Caption}</b>", 0f, y, width, _captionHeight,
                      TextAlignmentOptions.BottomLeft, _maxFontSize);
            y -= _captionHeight + CaptionGap;
        }

        int rowCount = table.Rows.Count;
        if (rowCount == 0) return y;

        int columns = 0;
        foreach (string[] row in table.Rows) columns = Mathf.Max(columns, row.Length);

        float[] colX = ColumnEdges(table.ColumnWidths, columns, width);
        float headerH = _headerHeight * rowScale;
        float rowH    = _rowHeight * rowScale;
        float gridTop = y;
        float gridHeight = headerH + (rowCount - 1) * rowH;
        float fontSize = Mathf.Max(_minFontSize, _maxFontSize * Mathf.Lerp(0.85f, 1f, Mathf.InverseLerp(MinRowScale, 1f, rowScale)));

        // Cells
        float rowTop = gridTop;
        for (int r = 0; r < rowCount; r++)
        {
            bool header = r == 0;
            float h = header ? headerH : rowH;
            string[] row = table.Rows[r];

            for (int c = 0; c < columns; c++)
            {
                string value = c < row.Length ? row[c] : null;
                if (string.IsNullOrEmpty(value)) continue;

                TextAlignmentOptions align = header
                    ? TextAlignmentOptions.Center
                    : table.Alignments != null && c < table.Alignments.Length ? table.Alignments[c] : TextAlignmentOptions.MidlineLeft;

                float x0 = colX[c] + CellPadding;
                float cellWidth = colX[c + 1] - colX[c] - CellPadding * 2f;
                PlaceText(header ? $"<b>{value}</b>" : value, x0, rowTop, cellWidth, h, align, fontSize);
            }
            rowTop -= h;
        }

        // Horizontal rules: outer top/bottom heavy, under the header heavy, others light.
        rowTop = gridTop;
        for (int r = 0; r <= rowCount; r++)
        {
            bool outer = r == 0 || r == rowCount;
            bool underHeader = r == 1;
            float t = outer || underHeader ? OuterRule : InnerRule;
            PlaceRule(0f, rowTop + t * 0.5f, width, t);
            if (r < rowCount) rowTop -= r == 0 ? headerH : rowH;
        }

        // Vertical rules
        for (int c = 0; c <= columns; c++)
        {
            bool outer = c == 0 || c == columns;
            float t = outer ? OuterRule : InnerRule;
            PlaceRule(colX[c] - t * 0.5f, gridTop, t, gridHeight);
        }

        return gridTop - gridHeight;
    }

    private static float[] ColumnEdges(float[] widths, int columns, float totalWidth)
    {
        var edges = new float[columns + 1];
        float sum = 0f;
        for (int c = 0; c < columns; c++) sum += ColumnWeight(widths, c);

        float x = 0f;
        for (int c = 0; c < columns; c++)
        {
            edges[c] = x;
            x += totalWidth * ColumnWeight(widths, c) / sum;
        }
        edges[columns] = totalWidth;
        return edges;
    }

    private static float ColumnWeight(float[] widths, int c) =>
        widths != null && c < widths.Length && widths[c] > 0f ? widths[c] : 1f;

    // ── Callout ───────────────────────────────────────────────────────────────

    private const float CalloutIconWidth = 44f;

    private float CalloutHeight(string text, float width)
    {
        TextMeshProUGUI probe = NextText();
        probe.fontSize = _maxFontSize;
        probe.enableAutoSizing = false;
        float textWidth = width - CalloutIconWidth - CellPadding * 3f;
        float h = probe.GetPreferredValues(text, textWidth, 10000f).y;
        _textCursor--; // probe only
        return Mathf.Max(CalloutIconWidth + CellPadding, h + CellPadding * 2.5f);
    }

    private float LayoutCallout(string text, float top, float width, float height)
    {
        // Box
        PlaceRule(0f, top, width, OuterRule);
        PlaceRule(0f, top - height + OuterRule, width, OuterRule);
        PlaceRule(0f, top, OuterRule, height);
        PlaceRule(width - OuterRule, top, OuterRule, height);

        // Warning mark: a ringed "!" on the left.
        float iconSize = Mathf.Min(CalloutIconWidth - CellPadding, height - CellPadding * 2f);
        float iconX = CellPadding + (CalloutIconWidth - CellPadding - iconSize) * 0.5f + 2f;
        float iconTop = top - (height - iconSize) * 0.5f;
        PlaceRule(iconX, iconTop, iconSize, InnerRule * 1.5f);
        PlaceRule(iconX, iconTop - iconSize + InnerRule * 1.5f, iconSize, InnerRule * 1.5f);
        PlaceRule(iconX, iconTop, InnerRule * 1.5f, iconSize);
        PlaceRule(iconX + iconSize - InnerRule * 1.5f, iconTop, InnerRule * 1.5f, iconSize);
        PlaceText("<b>!</b>", iconX, iconTop, iconSize, iconSize, TextAlignmentOptions.Center, iconSize * 0.8f, autoSize: false);

        float textX = CalloutIconWidth + CellPadding * 1.5f;
        PlaceText(text, textX, top - CellPadding, width - textX - CellPadding, height - CellPadding * 2f,
                  TextAlignmentOptions.MidlineLeft, _maxFontSize);

        return top - height;
    }

    // ── Elements ──────────────────────────────────────────────────────────────

    private void PlaceText(string value, float x, float top, float width, float height,
                           TextAlignmentOptions align, float maxSize, bool autoSize = true)
    {
        TextMeshProUGUI text = NextText();
        text.text = value;
        text.alignment = align;
        text.enableAutoSizing = autoSize;
        text.fontSizeMax = maxSize;
        text.fontSizeMin = Mathf.Min(_minFontSize, maxSize);
        text.fontSize = maxSize;
        Place(text.rectTransform, x, top, width, height);
    }

    private void PlaceRule(float x, float top, float width, float height)
    {
        Image rule = NextRule();
        Place(rule.rectTransform, x, top, width, height);
    }

    /// <summary>Positions an element by its top-left corner in chart space (x right, y down from 0).</summary>
    private void Place(RectTransform rt, float x, float top, float width, float height)
    {
        float halfWidth = Rect.rect.width * 0.5f;
        rt.anchoredPosition = new Vector2(x - halfWidth, top);
        rt.sizeDelta = new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, height));
    }

    private TextMeshProUGUI NextText()
    {
        TextMeshProUGUI text;
        if (_textCursor < _texts.Count)
        {
            text = _texts[_textCursor];
        }
        else
        {
            text = CreateChild("Chart Text").AddComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.richText = true;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Truncate;
            text.margin = Vector4.zero;
            _texts.Add(text);
        }

        if (_style != null)
        {
            text.font = _style.font;
            text.fontSharedMaterial = _style.fontSharedMaterial;
            text.lineSpacing = 0f;
        }
        text.color = _ink;
        text.gameObject.SetActive(true);
        _textCursor++;
        return text;
    }

    private Image NextRule()
    {
        Image rule;
        if (_ruleCursor < _rules.Count)
        {
            rule = _rules[_ruleCursor];
        }
        else
        {
            rule = CreateChild("Chart Rule").AddComponent<Image>();
            rule.raycastTarget = false;
            _rules.Add(rule);
        }

        rule.material = _ruleMaterial;
        rule.color = new Color(_ink.r, _ink.g, _ink.b, 0.9f);
        rule.gameObject.SetActive(true);
        _ruleCursor++;
        return rule;
    }

    private GameObject CreateChild(string childName)
    {
        var go = new GameObject(childName, typeof(RectTransform));
        go.layer = gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        return go;
    }

    private void HideUnused()
    {
        for (int i = _textCursor; i < _texts.Count; i++) _texts[i].gameObject.SetActive(false);
        for (int i = _ruleCursor; i < _rules.Count; i++) _rules[i].gameObject.SetActive(false);
    }
}
