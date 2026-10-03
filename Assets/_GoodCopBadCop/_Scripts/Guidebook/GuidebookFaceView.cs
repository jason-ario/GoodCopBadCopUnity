using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Everything one side of a guidebook sheet needs to render itself.</summary>
public struct GuidebookFaceContent
{
    public string Header;
    public string Title;
    public string Badge;
    public string Body;
    public Sprite Image;
    /// <summary>Optional row of illustrations drawn side by side in the image slot (replaces <see cref="Image"/>).</summary>
    public Sprite[] ImageRow;
    /// <summary>Fixed image slot height; 0 = automatic (whatever space the body doesn't need).</summary>
    public float  ImageHeight;
    /// <summary>Optional printed copy of the HUD integrity panel, drawn in the image slot (replaces images).</summary>
    public GuidebookIntegrityPanel IntegrityPanel;
    /// <summary>Optional printed copy of a HUD element (health bar / Geiger counter), drawn in the image slot (replaces images).</summary>
    public GuidebookHudArt HudArt;
    /// <summary>Readings shown by <see cref="HudArt"/>.</summary>
    public GuidebookSurvivalExample HudExample;
    /// <summary>Optional illustrated rows (icon + label + text) printed below the body.</summary>
    public GuidebookIconItem[] IconList;
    public Color  AccentColor;
    public int    PageNumber;   // 0 = hide footer
    public bool   IsNew;
    public bool   IsBlank;      // notes filler page: header only, lined body

    /// <summary>Ruled charts printed below the body (the body then only takes the height it needs).</summary>
    public GuidebookTable[] Tables;
    /// <summary>Optional boxed warning printed after the charts.</summary>
    public string Callout;

    public bool HasChart => (Tables != null && Tables.Length > 0) || !string.IsNullOrEmpty(Callout);
}

/// <summary>
/// One side (front or back) of a <see cref="GuidebookSheet"/>. Lives on the face's Canvas.
///
/// Elements are stacked top-down from <see cref="_contentTop"/>: optional image, optional badge,
/// then the body, which stretches down to <see cref="_bodyBottom"/>. This lets one layout serve
/// rule pages, the scoring table, section intros, anomaly pages and blank notes pages.
/// All coordinates are in the face's "Layout" RectTransform space (anchored top-centre).
/// </summary>
public class GuidebookFaceView : MonoBehaviour
{
    [Header("Elements")]
    [SerializeField] private TMP_Text   _header;
    [SerializeField] private Graphic    _divider;
    [SerializeField] private TMP_Text   _title;
    [SerializeField] private Image      _image;
    [SerializeField] private TMP_Text   _badge;
    [SerializeField] private TMP_Text   _body;
    [SerializeField] private TMP_Text   _footer;
    [SerializeField] private GameObject _newStamp;

    [Header("Vertical Layout (Layout-space units, top = 0)")]
    [SerializeField] private float _contentTop     = -100f;
    [Tooltip("The image takes whatever space the body text doesn't need, clamped to this range.")]
    [SerializeField] private float _imageMinHeight = 200f;
    [SerializeField] private float _imageMaxHeight = 320f;
    [SerializeField] private float _badgeHeight    = 28f;
    [SerializeField] private float _gap            = 12f;
    [SerializeField] private float _bodyBottom     = -570f;

    [Header("Notes Page")]
    [SerializeField] private int    _notesLineCount = 14;
    [SerializeField] private Color  _notesLineColor = new Color(0.35f, 0.38f, 0.5f, 0.45f);

    private static readonly Color DefaultAccent = new Color(0.45f, 0.08f, 0.06f, 1f);

    [Header("Integrity Panel")]
    [Tooltip("Width of the printed HUD integrity panel as a fraction of the image slot width.")]
    [SerializeField, Range(0.3f, 1f)] private float _panelWidthFraction = 0.85f;

    [Header("HUD Art (health bar / Geiger counter)")]
    [Tooltip("Largest width of a printed HUD element as a fraction of the image slot width.")]
    [SerializeField, Range(0.3f, 1f)] private float _hudArtWidthFraction = 0.85f;
    [Tooltip("Largest height of a printed HUD element (Layout units); tall art like the Geiger counter is scaled to fit.")]
    [SerializeField, Min(40f)] private float _hudArtMaxHeight = 230f;

    [Header("Spreading Sparse Pages")]
    [Tooltip("Text-only pages: largest extra paragraph spacing (TMP units, 100 = 1 em) used to fill the page.")]
    [SerializeField, Min(0f)] private float _maxParagraphSpread = 90f;
    [Tooltip("Chart / list pages: largest extra gap inserted between blocks to use leftover space.")]
    [SerializeField, Min(0f)] private float _maxBlockSpread = 60f;

    private readonly System.Collections.Generic.List<System.Collections.Generic.List<RectTransform>> _blocks =
        new System.Collections.Generic.List<System.Collections.Generic.List<RectTransform>>();
    private float _authoredParagraphSpacing = float.NaN;

    private void AddBlock(RectTransform rt)
    {
        _blocks.Add(new System.Collections.Generic.List<RectTransform> { rt });
    }

    private void AddRowBlock()
    {
        var block = new System.Collections.Generic.List<RectTransform>();
        foreach (Image img in _rowImages)
            if (img != null && img.gameObject.activeSelf) block.Add(img.rectTransform);
        _blocks.Add(block);
    }

    /// <summary>
    /// Moves blocks down so the leftover space is shared evenly between the gaps between blocks
    /// and the space at the bottom (each gap grows by at most <see cref="_maxBlockSpread"/>).
    /// </summary>
    private void SpreadBlocks(float leftover)
    {
        int count = _blocks.Count;
        if (count < 2 || leftover <= 0f) return;

        float extra = Mathf.Min(leftover / count, _maxBlockSpread);
        for (int i = 1; i < count; i++)
            foreach (RectTransform rt in _blocks[i])
            {
                if (rt == null) continue;
                Vector2 pos = rt.anchoredPosition;
                pos.y -= extra * i;
                rt.anchoredPosition = pos;
            }
    }

    private void ResetBodySpacing()
    {
        if (float.IsNaN(_authoredParagraphSpacing)) _authoredParagraphSpacing = _body.paragraphSpacing;
        _body.paragraphSpacing = _authoredParagraphSpacing;
    }

    /// <summary>
    /// Raises the body's paragraph spacing (space after each line break) until the text nearly
    /// fills <paramref name="height"/>, so short text-only pages read as a full page.
    /// </summary>
    private void SpreadBodyToFill(string body, float height)
    {
        if (string.IsNullOrEmpty(body) || height <= 0f || body.IndexOf('\n') < 0) return;

        float width  = _body.rectTransform.rect.width;
        float target = height * 0.94f;
        if (_body.GetPreferredValues(body, width, 10000f).y >= target) return;

        float lo = _authoredParagraphSpacing, hi = _authoredParagraphSpacing + _maxParagraphSpread;
        _body.paragraphSpacing = hi;
        if (_body.GetPreferredValues(body, width, 10000f).y <= target) return; // capped

        for (int i = 0; i < 8; i++)
        {
            float mid = (lo + hi) * 0.5f;
            _body.paragraphSpacing = mid;
            if (_body.GetPreferredValues(body, width, 10000f).y <= target) lo = mid;
            else hi = mid;
        }
        _body.paragraphSpacing = lo;
    }

    private GuidebookChartView _chart;
    private GuidebookIntegrityPanelView _panel;
    private GuidebookIconListView _iconList;
    private GuidebookHudArtView _hudArt;

    /// <summary>Runtime sibling of the image, centred in the image slot.</summary>
    private GuidebookHudArtView GetOrCreateHudArt()
    {
        if (_hudArt != null || _image == null) return _hudArt;

        RectTransform src = _image.rectTransform;
        var go = new GameObject("HUD Art", typeof(RectTransform));
        go.layer = _image.gameObject.layer;

        var rt = (RectTransform)go.transform;
        rt.SetParent(src.parent, false);
        rt.SetSiblingIndex(src.GetSiblingIndex() + 1);
        rt.anchorMin = src.anchorMin;
        rt.anchorMax = src.anchorMax;
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(src.anchoredPosition.x, 0f);

        _hudArt = go.AddComponent<GuidebookHudArtView>();
        _hudArt.Configure(_body, _image.material);
        return _hudArt;
    }

    /// <summary>Runtime sibling of the body, sized and anchored like it.</summary>
    private GuidebookIconListView GetOrCreateIconList()
    {
        if (_iconList != null || _body == null || _image == null) return _iconList;

        RectTransform bodyRect = _body.rectTransform;
        var go = new GameObject("Icon List", typeof(RectTransform));
        go.layer = _body.gameObject.layer;

        var rt = (RectTransform)go.transform;
        rt.SetParent(bodyRect.parent, false);
        rt.SetSiblingIndex(bodyRect.GetSiblingIndex() + 1);
        rt.anchorMin = bodyRect.anchorMin;
        rt.anchorMax = bodyRect.anchorMax;
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(bodyRect.anchoredPosition.x, 0f);
        rt.sizeDelta = new Vector2(bodyRect.rect.width, 0f);

        _iconList = go.AddComponent<GuidebookIconListView>();
        _iconList.Configure(_body, _image.material, _body.color);
        return _iconList;
    }

    /// <summary>Runtime sibling of the image, centred in the image slot.</summary>
    private GuidebookIntegrityPanelView GetOrCreatePanel()
    {
        if (_panel != null || _image == null) return _panel;

        RectTransform src = _image.rectTransform;
        var go = new GameObject("Integrity Panel", typeof(RectTransform));
        go.layer = _image.gameObject.layer;

        var rt = (RectTransform)go.transform;
        rt.SetParent(src.parent, false);
        rt.SetSiblingIndex(src.GetSiblingIndex() + 1);
        rt.anchorMin = src.anchorMin;
        rt.anchorMax = src.anchorMax;
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(src.anchoredPosition.x, 0f);

        _panel = go.AddComponent<GuidebookIntegrityPanelView>();
        _panel.Configure(_body, _image.material);
        return _panel;
    }

    /// <summary>
    /// The chart lives in a runtime child of the Layout, sized and anchored like the body, so the
    /// sheet prefab needs no extra authoring.
    /// </summary>
    private GuidebookChartView GetOrCreateChart()
    {
        if (_chart != null || _body == null) return _chart;

        RectTransform bodyRect = _body.rectTransform;
        var go = new GameObject("Chart", typeof(RectTransform));
        go.layer = _body.gameObject.layer;

        var rt = (RectTransform)go.transform;
        rt.SetParent(bodyRect.parent, false);
        rt.SetSiblingIndex(bodyRect.GetSiblingIndex() + 1);
        rt.anchorMin = bodyRect.anchorMin;
        rt.anchorMax = bodyRect.anchorMax;
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(bodyRect.anchoredPosition.x, 0f);
        rt.sizeDelta = new Vector2(bodyRect.rect.width, 0f);

        _chart = go.AddComponent<GuidebookChartView>();
        Material ruleMaterial = _divider != null ? _divider.material : null;
        _chart.Configure(_body, ruleMaterial, _body.color);
        return _chart;
    }

    public void Apply(in GuidebookFaceContent content)
    {
        Color accent = content.AccentColor.a > 0f ? content.AccentColor : DefaultAccent;

        SetText(_header, content.Header);
        if (_header != null) _header.color = accent;
        if (_divider != null) _divider.color = new Color(accent.r, accent.g, accent.b, 0.85f);

        SetText(_title, content.IsBlank ? null : content.Title);

        float cursor = _contentTop;

        bool hasPanel = !content.IsBlank && content.IntegrityPanel != null && _image != null;
        bool hasHud   = !content.IsBlank && !hasPanel && content.HudArt != GuidebookHudArt.None && _image != null;
        bool hasRow   = !content.IsBlank && !hasPanel && !hasHud && HasAny(content.ImageRow);
        bool hasImage = !content.IsBlank && !hasPanel && !hasHud && (content.Image != null || hasRow);
        bool hasBadge = !content.IsBlank && !string.IsNullOrEmpty(content.Badge);
        bool hasChart = !content.IsBlank && content.HasChart;
        bool hasList  = !content.IsBlank && content.IconList != null && content.IconList.Length > 0;
        bool fitBody  = hasChart || hasList;
        string body   = content.IsBlank ? BuildNotesLines() : content.Body;

        // Every element placed below is recorded as a block so leftover page space can be
        // spread between them afterwards (instead of everything bunching up at the top).
        _blocks.Clear();
        float blockBottom = cursor;

        GuidebookIntegrityPanelView panel = hasPanel ? GetOrCreatePanel() : _panel;
        if (panel != null)
        {
            if (hasPanel)
            {
                float width = _image.rectTransform.sizeDelta.x * _panelWidthFraction;
                var panelRect = (RectTransform)panel.transform;
                PlaceTop(panelRect, cursor, 0f);
                float used = panel.Build(content.IntegrityPanel, width);
                if (used > 0f)
                {
                    AddBlock(panelRect);
                    blockBottom = cursor - used;
                    cursor = blockBottom - _gap * 1.5f;
                }
            }
            else
            {
                panel.Clear();
            }
        }

        GuidebookHudArtView hudArt = hasHud ? GetOrCreateHudArt() : _hudArt;
        if (hudArt != null)
        {
            if (hasHud)
            {
                float width = _image.rectTransform.sizeDelta.x * _hudArtWidthFraction;
                var hudRect = (RectTransform)hudArt.transform;
                PlaceTop(hudRect, cursor, 0f);
                float used = hudArt.Build(content.HudArt, content.HudExample, width, _hudArtMaxHeight);
                if (used > 0f)
                {
                    AddBlock(hudRect);
                    blockBottom = cursor - used;
                    cursor = blockBottom - _gap * 1.5f;
                }
            }
            else
            {
                hudArt.Clear();
            }
        }

        if (_image != null)
        {
            _image.gameObject.SetActive(hasImage && !hasRow);
            if (!hasRow) HideImageRow();

            if (hasImage)
            {
                float imageHeight = content.ImageHeight > 0f
                    ? content.ImageHeight
                    : ImageHeightFor(cursor, hasBadge, body);

                if (hasRow)
                {
                    imageHeight = LayoutImageRow(content.ImageRow, cursor, imageHeight);
                    AddRowBlock();
                }
                else
                {
                    _image.sprite = content.Image;
                    _image.preserveAspect = true;
                    PlaceTop(_image.rectTransform, cursor, imageHeight);
                    AddBlock(_image.rectTransform);
                }
                blockBottom = cursor - imageHeight;
                cursor = blockBottom - _gap;
            }
        }

        if (_badge != null)
        {
            SetText(_badge, hasBadge ? content.Badge : null);
            if (hasBadge)
            {
                _badge.color = accent;
                PlaceTop(_badge.rectTransform, cursor, _badgeHeight);
                AddBlock(_badge.rectTransform);
                blockBottom = cursor - _badgeHeight;
                cursor = blockBottom - _gap;
            }
        }

        if (_body != null)
        {
            SetText(_body, body);
            ResetBodySpacing();
            float bodyHeight = Mathf.Max(0f, cursor - _bodyBottom);
            if (fitBody)
            {
                bodyHeight = string.IsNullOrEmpty(body)
                    ? 0f
                    : Mathf.Min(bodyHeight, _body.GetPreferredValues(body, _body.rectTransform.rect.width, 10000f).y + 4f);
            }
            else if (!content.IsBlank && !hasImage)
            {
                // Text-only pages: open the paragraphs up so the text fills the page.
                SpreadBodyToFill(body, bodyHeight);
            }
            PlaceTop(_body.rectTransform, cursor, bodyHeight);
            if (fitBody && bodyHeight > 0f)
            {
                AddBlock(_body.rectTransform);
                blockBottom = cursor - bodyHeight;
                cursor = blockBottom - _gap * 1.5f;
            }
        }

        GuidebookIconListView list = hasList ? GetOrCreateIconList() : _iconList;
        if (list != null)
        {
            if (hasList)
            {
                var listRect = (RectTransform)list.transform;
                PlaceTop(listRect, cursor, 0f);
                float used = list.Build(content.IconList, Mathf.Max(0f, cursor - _bodyBottom));
                AddBlock(listRect);
                blockBottom = cursor - used;
                cursor = blockBottom - _gap * 1.5f;
            }
            else
            {
                list.Clear();
            }
        }

        GuidebookChartView chart = hasChart ? GetOrCreateChart() : _chart;
        if (chart != null)
        {
            if (hasChart)
            {
                var chartRect = (RectTransform)chart.transform;
                PlaceTop(chartRect, cursor, 0f);
                float used = chart.Build(content.Tables, content.Callout, Mathf.Max(0f, cursor - _bodyBottom));
                AddBlock(chartRect);
                blockBottom = cursor - used;
            }
            else
            {
                chart.Clear();
            }
        }

        if (fitBody) SpreadBlocks(blockBottom - _bodyBottom);

        SetText(_footer, content.PageNumber > 0 ? $"- {content.PageNumber} -" : null);

        if (_newStamp != null) _newStamp.SetActive(content.IsNew && !content.IsBlank);
    }

    /// <summary>
    /// Image slot height this face would pick automatically for <paramref name="content"/>.
    /// Lets the builder give a group of pages one shared (smallest) image height.
    /// </summary>
    public float MeasureImageHeight(in GuidebookFaceContent content)
    {
        string body = content.IsBlank ? null : content.Body;
        return ImageHeightFor(_contentTop, !string.IsNullOrEmpty(content.Badge), body);
    }

    private static bool HasAny(Sprite[] sprites)
    {
        if (sprites == null) return false;
        foreach (Sprite s in sprites) if (s != null) return true;
        return false;
    }

    private readonly System.Collections.Generic.List<Image> _rowImages = new System.Collections.Generic.List<Image>();

    /// <summary>
    /// Lays the sprites out side by side across the image slot's width (equal cells, <see cref="_gap"/>
    /// apart) using runtime copies of <see cref="_image"/>, so they share its lit material.
    /// Returns the row height actually used (no taller than the sprites need at that cell width).
    /// </summary>
    private float LayoutImageRow(Sprite[] sprites, float top, float maxHeight)
    {
        RectTransform src = _image.rectTransform;
        float width = src.sizeDelta.x;
        int   count = sprites.Length;
        float cellWidth = (width - _gap * (count - 1)) / count;

        float needed = 0f;
        foreach (Sprite s in sprites)
            if (s != null && s.rect.width > 0f)
                needed = Mathf.Max(needed, cellWidth * s.rect.height / s.rect.width);
        float height = Mathf.Min(maxHeight, needed > 0f ? needed : maxHeight);

        while (_rowImages.Count < count)
        {
            Image copy = Instantiate(_image, src.parent);
            copy.name = $"Image Row {_rowImages.Count}";
            copy.transform.SetSiblingIndex(src.GetSiblingIndex() + 1 + _rowImages.Count);
            _rowImages.Add(copy);
        }

        float left = src.anchoredPosition.x - width * 0.5f + cellWidth * 0.5f;
        for (int i = 0; i < _rowImages.Count; i++)
        {
            Image img = _rowImages[i];
            bool used = i < count && sprites[i] != null;
            img.gameObject.SetActive(used);
            if (!used) continue;

            img.sprite = sprites[i];
            img.preserveAspect = true;
            RectTransform rt = img.rectTransform;
            rt.anchoredPosition = new Vector2(left + i * (cellWidth + _gap), top);
            rt.sizeDelta = new Vector2(cellWidth, height);
        }
        return height;
    }

    private void HideImageRow()
    {
        foreach (Image img in _rowImages)
            if (img != null) img.gameObject.SetActive(false);
    }

    /// <summary>
    /// Gives the image all vertical space the body needs at its largest font size isn't using,
    /// so short anomaly pages get big illustrations and text-heavy pages keep readable text.
    /// </summary>
    private float ImageHeightFor(float top, bool hasBadge, string body)
    {
        float available = top - _bodyBottom - _gap;
        if (hasBadge) available -= _badgeHeight + _gap;

        float bodyNeeded = 0f;
        if (_body != null && !string.IsNullOrEmpty(body))
            bodyNeeded = _body.GetPreferredValues(body, _body.rectTransform.rect.width, 10000f).y;

        return Mathf.Clamp(available - bodyNeeded, _imageMinHeight, _imageMaxHeight);
    }

    private string BuildNotesLines()
    {
        string hex = ColorUtility.ToHtmlStringRGBA(_notesLineColor);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _notesLineCount; i++)
            sb.Append("<color=#").Append(hex).Append(">______________________________</color>\n");
        return sb.ToString();
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text == null) return;
        bool show = !string.IsNullOrEmpty(value);
        text.gameObject.SetActive(show);
        if (show) text.text = value;
    }

    private static void PlaceTop(RectTransform rt, float top, float height)
    {
        Vector2 pos = rt.anchoredPosition;
        pos.y = top;
        rt.anchoredPosition = pos;

        Vector2 size = rt.sizeDelta;
        size.y = height;
        rt.sizeDelta = size;
    }
}
