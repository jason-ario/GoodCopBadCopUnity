using TMPro;
using UnityEngine;

/// <summary>
/// Automatically resizes the RectTransform width to match the TextMeshProUGUI
/// preferred text width. Padding is added on each horizontal side.
/// Hooks into TMP's text-changed event for zero-overhead updates.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(TextMeshProUGUI))]
public class TMPWidthFitter : MonoBehaviour
{
    [Tooltip("Extra pixels added to each horizontal side of the measured text width.")]
    [SerializeField] private float horizontalPadding = 0f;

    private TextMeshProUGUI tmp;
    private RectTransform rectTransform;

    private string lastText = null;
    private float lastFontSize = -1f;

    private void Awake()
    {
        CacheComponents();
    }

    private void OnEnable()
    {
        CacheComponents();
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        UpdateWidth();
    }

    private void OnDisable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
    }

    private void OnValidate()
    {
        CacheComponents();
        UpdateWidth();
    }

    // Catches font size changes (e.g. auto-sizing) which don't fire TEXT_CHANGED_EVENT.
    private void LateUpdate()
    {
        if (tmp == null)
            return;

        bool textChanged = tmp.text != lastText;
        bool sizeChanged = !Mathf.Approximately(tmp.fontSize, lastFontSize);

        if (textChanged || sizeChanged)
            UpdateWidth();
    }

    /// <summary>Sets the horizontal padding and immediately refreshes the width.</summary>
    public void SetHorizontalPadding(float padding)
    {
        horizontalPadding = padding;
        UpdateWidth();
    }

    /// <summary>Forces an immediate width recalculation.</summary>
    public void UpdateWidth()
    {
        if (tmp == null || rectTransform == null)
            return;

        // Measure unconstrained preferred width (no wrapping limit).
        float preferredWidth = tmp.GetPreferredValues(float.MaxValue, float.MaxValue).x;
        float targetWidth = preferredWidth + horizontalPadding * 2f;

        rectTransform.sizeDelta = new Vector2(targetWidth, rectTransform.sizeDelta.y);

        // Cache state to avoid redundant updates in LateUpdate.
        lastText = tmp.text;
        lastFontSize = tmp.fontSize;
    }

    private void OnTextChanged(Object obj)
    {
        if (obj == tmp)
            UpdateWidth();
    }

    private void CacheComponents()
    {
        if (tmp == null)
            tmp = GetComponent<TextMeshProUGUI>();

        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();
    }
}
