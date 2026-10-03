using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Art and example values for the printed copy of the HUD's Checkpoint Integrity panel
/// (<see cref="CheckpointIntegrityBar"/> + maintenance rows) shown on the guidebook's integrity page.
/// </summary>
[Serializable]
public class GuidebookIntegrityPanel
{
    [Tooltip("Panel frame (the HUD uses 'HUD/Slice/Tasks panel'). Drawn 9-sliced.")]
    public Sprite Background;
    public Sprite FenceIcon;
    public Sprite TrashIcon;
    public Sprite GraffitiIcon;

    [Header("Example State")]
    [Range(0, 100)] public int Percent = 70;
    [Min(0)] public int FenceCount    = 1;
    [Min(0)] public int TrashCount    = 2;
    [Min(0)] public int GraffitiCount = 1;
}

/// <summary>
/// Redraws the HUD's Checkpoint Integrity panel on a guidebook face: frame, title, percentage,
/// ten integrity squares and the fence / trash / graffiti counters. Laid out in the HUD's own
/// 300 x 136 units and scaled to the requested width. Images reuse the face illustration's lit
/// material and text reuses the body font's lit material, so the panel is lit like the page.
/// Created at runtime by <see cref="GuidebookFaceView"/>.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class GuidebookIntegrityPanelView : MonoBehaviour
{
    // HUD reference layout (CheckpointIntegrityPanel in the Player HUD).
    private const float RefWidth  = 300f;
    private const float RefHeight = 136f;
    private const int   Squares   = 10;

    private static readonly Color TitleColor    = new Color(0.906f, 0.78f, 0.58f, 1f);
    private static readonly Color BarBackColor  = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color GoodColor     = new Color(0.75f, 0.82f, 0.25f, 1f);
    private static readonly Color WarningColor  = new Color(1f, 0.85f, 0.1f, 1f);
    private static readonly Color CriticalColor = new Color(0.9f, 0.12f, 0.1f, 1f);
    private static readonly Color EmptyGood     = new Color(0.18f, 0.19f, 0.08f, 1f);
    private static readonly Color EmptyWarning  = new Color(0.24f, 0.19f, 0.04f, 1f);
    private static readonly Color EmptyCritical = new Color(0.26f, 0.05f, 0.04f, 1f);
    private static readonly Color PanelWarning  = new Color(1f, 0.88f, 0.6f, 1f);
    private static readonly Color PanelCritical = new Color(0.85f, 0.38f, 0.34f, 1f);

    private readonly List<Image>           _images = new List<Image>();
    private readonly List<TextMeshProUGUI> _texts  = new List<TextMeshProUGUI>();
    private int _imageCursor;
    private int _textCursor;

    private TMP_Text _style;
    private Material _imageMaterial;
    private float    _scale = 1f;
    private float    _width;

    private RectTransform Rect => (RectTransform)transform;

    public void Configure(TMP_Text style, Material imageMaterial)
    {
        _style         = style;
        _imageMaterial = imageMaterial;
    }

    /// <summary>Height the panel takes at <paramref name="width"/>.</summary>
    public static float HeightFor(float width) => width * RefHeight / RefWidth;

    public void Clear()
    {
        _imageCursor = _textCursor = 0;
        HideUnused();
        gameObject.SetActive(false);
    }

    public void Build(GuidebookIntegrityPanel panel, float width)
    {
        gameObject.SetActive(true);
        _imageCursor = _textCursor = 0;
        _width = width;
        _scale = width / RefWidth;
        Rect.sizeDelta = new Vector2(width, HeightFor(width));

        float n = Mathf.Clamp01(panel.Percent / 100f);
        Color fill  = n >= 0.9f ? GoodColor : n >= 0.6f ? WarningColor : CriticalColor;
        Color empty = n >= 0.9f ? EmptyGood : n >= 0.6f ? EmptyWarning : EmptyCritical;
        Color tint  = n >= 0.9f ? Color.white : n >= 0.6f ? PanelWarning : PanelCritical;

        // Frame
        Image frame = PlaceImage(panel.Background, 0f, 0f, RefWidth, RefHeight, tint);
        frame.type = panel.Background != null && panel.Background.border.sqrMagnitude > 0f
            ? Image.Type.Sliced : Image.Type.Simple;
        frame.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.01f, _scale);

        // Title + score bar
        PlaceText("CHECKPOINT INTEGRITY", 12f, 10f, RefWidth - 24f, 26f, 21f, TitleColor, TextAlignmentOptions.Center);
        PlaceText($"{panel.Percent}%", 14f, 40f, 58f, 34f, 22f, n >= 0.9f ? Color.white : fill, TextAlignmentOptions.MidlineLeft);

        const float barX = 76f, barY = 47f, barW = 208f, barH = 20f, sq = 12f, pad = 5f;
        PlaceImage(null, barX, barY, barW, barH, BarBackColor);
        int filled = Mathf.RoundToInt(n * Squares);
        float step = (barW - pad * 2f - sq) / (Squares - 1);
        for (int i = 0; i < Squares; i++)
            PlaceImage(null, barX + pad + i * step, barY + (barH - sq) * 0.5f, sq, sq, i < filled ? fill : empty);

        // Maintenance counters (HUD order: fence, trash, graffiti)
        const float rowY = 86f, icon = 26f, countW = 22f, groupGap = 34f;
        float groupW = icon + 6f + countW;
        float x = (RefWidth - (groupW * 3f + groupGap * 2f)) * 0.5f;
        PlaceCounter(panel.FenceIcon,    panel.FenceCount,    x, rowY, icon, countW); x += groupW + groupGap;
        PlaceCounter(panel.TrashIcon,    panel.TrashCount,    x, rowY, icon, countW); x += groupW + groupGap;
        PlaceCounter(panel.GraffitiIcon, panel.GraffitiCount, x, rowY, icon, countW);

        HideUnused();
    }

    private void PlaceCounter(Sprite sprite, int count, float x, float y, float icon, float countW)
    {
        Image img = PlaceImage(sprite, x, y, icon, icon, TitleColor);
        img.preserveAspect = true;
        PlaceText(count.ToString(), x + icon + 6f, y, countW, icon, 18f, TitleColor, TextAlignmentOptions.MidlineLeft);
    }

    // ── Elements (positions in HUD reference units, top-left origin) ─────────

    private Image PlaceImage(Sprite sprite, float x, float y, float w, float h, Color color)
    {
        Image img;
        if (_imageCursor < _images.Count)
        {
            img = _images[_imageCursor];
        }
        else
        {
            img = CreateChild("Panel Image").AddComponent<Image>();
            img.raycastTarget = false;
            _images.Add(img);
        }
        _imageCursor++;

        img.material       = _imageMaterial;
        img.sprite         = sprite;
        img.type           = Image.Type.Simple;
        img.preserveAspect = false;
        img.color          = color;
        img.gameObject.SetActive(true);
        Place(img.rectTransform, x, y, w, h);
        return img;
    }

    private void PlaceText(string value, float x, float y, float w, float h, float size, Color color,
                           TextAlignmentOptions align)
    {
        TextMeshProUGUI text;
        if (_textCursor < _texts.Count)
        {
            text = _texts[_textCursor];
        }
        else
        {
            text = CreateChild("Panel Text").AddComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.richText = true;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.margin = Vector4.zero;
            _texts.Add(text);
        }
        _textCursor++;

        if (_style != null)
        {
            text.font = _style.font;
            text.fontSharedMaterial = _style.fontSharedMaterial;
        }
        text.text = $"<b>{value}</b>";
        text.alignment = align;
        text.color = color;
        text.enableAutoSizing = true;
        text.fontSizeMax = size * _scale;
        text.fontSizeMin = size * _scale * 0.5f;
        text.fontSize = size * _scale;
        text.gameObject.SetActive(true);
        Place(text.rectTransform, x, y, w, h);
    }

    private void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchoredPosition = new Vector2(x * _scale - _width * 0.5f, -y * _scale);
        rt.sizeDelta = new Vector2(w * _scale, h * _scale);
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
        for (int i = _imageCursor; i < _images.Count; i++) _images[i].gameObject.SetActive(false);
        for (int i = _textCursor; i < _texts.Count; i++) _texts[i].gameObject.SetActive(false);
    }
}
