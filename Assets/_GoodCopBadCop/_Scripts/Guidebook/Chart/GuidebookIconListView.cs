using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One illustrated entry of a guidebook icon list: picture on the left, bold label + text on the right.</summary>
[Serializable]
public class GuidebookIconItem
{
    public Sprite Icon;
    public string Label;
    [TextArea(1, 3)]
    public string Text;
}

/// <summary>
/// Draws <see cref="GuidebookIconItem"/>s as stacked rows on a guidebook face (e.g. the chores on the
/// integrity page). Icons reuse the face illustration's lit material and text copies the body's font,
/// lit material and ink colour, so the list is lit like the page. Created at runtime by
/// <see cref="GuidebookFaceView"/> as a sibling of the body.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class GuidebookIconListView : MonoBehaviour
{
    private const float MaxRowHeight = 120f;
    private const float MinRowHeight = 40f;
    private const float RowGap       = 16f;
    private const float IconTextGap  = 14f;

    private readonly List<Image>           _icons = new List<Image>();
    private readonly List<TextMeshProUGUI> _texts = new List<TextMeshProUGUI>();

    private TMP_Text _style;
    private Material _imageMaterial;
    private Color    _ink;

    private RectTransform Rect => (RectTransform)transform;

    public void Configure(TMP_Text style, Material imageMaterial, Color ink)
    {
        _style         = style;
        _imageMaterial = imageMaterial;
        _ink           = ink;
    }

    public void Clear()
    {
        Hide(0);
        gameObject.SetActive(false);
    }

    /// <summary>Lays the rows out from the top of this rect, fitting <paramref name="availableHeight"/>. Returns the height used.</summary>
    public float Build(IReadOnlyList<GuidebookIconItem> items, float availableHeight)
    {
        gameObject.SetActive(true);
        int count = items != null ? items.Count : 0;
        float width = Rect.rect.width;

        float rowHeight = count > 0
            ? Mathf.Clamp((availableHeight - RowGap * (count - 1)) / count, MinRowHeight, MaxRowHeight)
            : 0f;
        float fontSize = _style != null ? Mathf.Max(10f, _style.fontSizeMax - 1f) : 17f;

        float y = 0f;
        for (int i = 0; i < count; i++)
        {
            GuidebookIconItem item = items[i];
            Image icon = IconAt(i);
            icon.sprite = item.Icon;
            icon.gameObject.SetActive(item.Icon != null);
            Place(icon.rectTransform, 0f, y, rowHeight, rowHeight, width);

            TextMeshProUGUI text = TextAt(i);
            text.text = string.IsNullOrEmpty(item.Label) ? item.Text : $"<b>{item.Label}</b>\n{item.Text}";
            text.fontSizeMax = fontSize;
            text.fontSizeMin = Mathf.Min(10f, fontSize);
            text.fontSize = fontSize;
            float textX = rowHeight + IconTextGap;
            Place(text.rectTransform, textX, y, width - textX, rowHeight, width);

            y -= rowHeight + RowGap;
        }
        Hide(count);

        float used = count > 0 ? -y - RowGap : 0f;
        Rect.sizeDelta = new Vector2(Rect.sizeDelta.x, used);
        return used;
    }

    private Image IconAt(int i)
    {
        while (_icons.Count <= i)
        {
            Image img = CreateChild("Icon").AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            _icons.Add(img);
        }
        Image icon = _icons[i];
        icon.material = _imageMaterial;
        return icon;
    }

    private TextMeshProUGUI TextAt(int i)
    {
        while (_texts.Count <= i)
        {
            TextMeshProUGUI t = CreateChild("Text").AddComponent<TextMeshProUGUI>();
            t.raycastTarget = false;
            t.richText = true;
            t.enableAutoSizing = true;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Truncate;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.margin = Vector4.zero;
            _texts.Add(t);
        }
        TextMeshProUGUI text = _texts[i];
        if (_style != null)
        {
            text.font = _style.font;
            text.fontSharedMaterial = _style.fontSharedMaterial;
            text.lineSpacing = _style.lineSpacing;
        }
        text.color = _ink;
        text.gameObject.SetActive(true);
        return text;
    }

    private void Hide(int from)
    {
        for (int i = from; i < _icons.Count; i++) _icons[i].gameObject.SetActive(false);
        for (int i = from; i < _texts.Count; i++) _texts[i].gameObject.SetActive(false);
    }

    /// <summary>Positions by top-left corner (x right, y down from 0) in list space.</summary>
    private static void Place(RectTransform rt, float x, float top, float w, float h, float listWidth)
    {
        rt.anchoredPosition = new Vector2(x - listWidth * 0.5f, top);
        rt.sizeDelta = new Vector2(Mathf.Max(0f, w), Mathf.Max(0f, h));
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
}
