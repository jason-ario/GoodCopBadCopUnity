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

    /// <summary>Forces an immediate width recalculation based on currently visible characters.</summary>
    public void UpdateWidth()
    {
        if (tmp == null || rectTransform == null)
            return;

        float measuredWidth = MeasureVisibleWidth();
        float targetWidth = measuredWidth + horizontalPadding * 2f;

        rectTransform.sizeDelta = new Vector2(targetWidth, rectTransform.sizeDelta.y);

        // Cache state to avoid redundant updates in LateUpdate.
        lastText = tmp.text;
        lastFontSize = tmp.fontSize;
    }

    /// <summary>
    /// Measures the preferred width of the currently visible text portion.
    /// When <see cref="TextMeshProUGUI.maxVisibleCharacters"/> is limiting the output,
    /// only the visible substring is measured so the box grows character by character.
    /// </summary>
    private float MeasureVisibleWidth()
    {
        var info = tmp.textInfo;
        int charCount = (info != null) ? info.characterCount : 0;
        int maxVisible = tmp.maxVisibleCharacters;

        if (charCount == 0 || maxVisible == 0)
            return 0f;

        // Fast path: all characters visible — measure the full text.
        if (maxVisible >= charCount)
            return tmp.GetPreferredValues(float.MaxValue, float.MaxValue).x;

        // Partial reveal: measure the substring up to and including the last visible character.
        // characterInfo[i].index gives the source-string position of the i-th visible character,
        // correctly accounting for rich-text tags in the source string.
        int lastStringIndex = info.characterInfo[maxVisible - 1].index;
        string visiblePortion = tmp.text.Substring(0, lastStringIndex + 1);
        return tmp.GetPreferredValues(visiblePortion, float.MaxValue, float.MaxValue).x;
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
