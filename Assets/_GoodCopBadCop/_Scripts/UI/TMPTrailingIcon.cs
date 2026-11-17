using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps an icon a fixed gap after the rendered end of a TMP label, sliding along the label's baseline.
/// The icon never moves further along than its authored position, so long text keeps the authored layout.
/// The icon must share the label's parent.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class TMPTrailingIcon : MonoBehaviour
{
    [SerializeField] private RectTransform _icon;
    [Tooltip("Gap between the last glyph and the icon's visible edge, in parent-space units.")]
    [SerializeField] private float _gap = 3f;

    private TMP_Text _text;
    private Vector3 _authoredIconPos;
    private bool _captured;

    private void Awake()
    {
        _text = GetComponent<TMP_Text>();
        Capture();
    }

    private void OnEnable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        Place();
    }

    private void OnDisable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
    }

    private void OnTextChanged(Object changed)
    {
        if (changed == _text) Place();
    }

    private void Capture()
    {
        if (_captured || _icon == null) return;
        _authoredIconPos = _icon.localPosition;
        _captured = true;
    }

    private void Place()
    {
        if (_text == null || _icon == null || _icon.parent != transform.parent) return;
        Capture();
        if (string.IsNullOrEmpty(_text.text) || _text.textInfo == null || _text.textInfo.characterCount == 0)
        {
            _icon.localPosition = _authoredIconPos;
            return;
        }

        var label = (RectTransform)transform;
        Vector3 right = label.localRotation * Vector3.right;
        Vector3 up = label.localRotation * Vector3.up;
        Vector3 origin = label.localPosition;

        Bounds b = _text.textBounds;
        Vector3 endLocal = new Vector3(b.max.x, b.center.y, 0f);
        Vector3 endParent = label.localRotation * Vector3.Scale(endLocal, label.localScale) + origin;

        float halfWidth = VisibleWidth(_icon) * 0.5f * _icon.localScale.x;
        float along = Vector3.Dot(endParent - origin, right) + _gap + halfWidth;
        float authoredAlong = Vector3.Dot(_authoredIconPos - origin, right);
        float perp = Vector3.Dot(_authoredIconPos - origin, up);

        Vector3 pos = origin + right * Mathf.Min(along, authoredAlong) + up * perp;
        pos.z = _authoredIconPos.z;
        _icon.localPosition = pos;
    }

    private static float VisibleWidth(RectTransform icon)
    {
        Rect r = icon.rect;
        var img = icon.GetComponent<Image>();
        if (img == null || img.sprite == null || !img.preserveAspect) return r.width;
        float aspect = img.sprite.rect.width / img.sprite.rect.height;
        return Mathf.Min(r.width, r.height * aspect);
    }
}
