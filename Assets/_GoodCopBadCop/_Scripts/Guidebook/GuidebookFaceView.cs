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
    public Color  AccentColor;
    public int    PageNumber;   // 0 = hide footer
    public bool   IsNew;
    public bool   IsBlank;      // notes filler page: header only, lined body
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

    public void Apply(in GuidebookFaceContent content)
    {
        Color accent = content.AccentColor.a > 0f ? content.AccentColor : DefaultAccent;

        SetText(_header, content.Header);
        if (_header != null) _header.color = accent;
        if (_divider != null) _divider.color = new Color(accent.r, accent.g, accent.b, 0.85f);

        SetText(_title, content.IsBlank ? null : content.Title);

        float cursor = _contentTop;

        bool hasImage = !content.IsBlank && content.Image != null;
        bool hasBadge = !content.IsBlank && !string.IsNullOrEmpty(content.Badge);
        string body   = content.IsBlank ? BuildNotesLines() : content.Body;

        if (_image != null)
        {
            _image.gameObject.SetActive(hasImage);
            if (hasImage)
            {
                float imageHeight = ImageHeightFor(cursor, hasBadge, body);
                _image.sprite = content.Image;
                _image.preserveAspect = true;
                PlaceTop(_image.rectTransform, cursor, imageHeight);
                cursor -= imageHeight + _gap;
            }
        }

        if (_badge != null)
        {
            SetText(_badge, hasBadge ? content.Badge : null);
            if (hasBadge)
            {
                _badge.color = accent;
                PlaceTop(_badge.rectTransform, cursor, _badgeHeight);
                cursor -= _badgeHeight + _gap;
            }
        }

        if (_body != null)
        {
            SetText(_body, body);
            PlaceTop(_body.rectTransform, cursor, Mathf.Max(0f, cursor - _bodyBottom));
        }

        SetText(_footer, content.PageNumber > 0 ? $"- {content.PageNumber} -" : null);

        if (_newStamp != null) _newStamp.SetActive(content.IsNew && !content.IsBlank);
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
