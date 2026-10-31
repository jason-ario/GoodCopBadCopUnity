using TMPro;
using UnityEngine;

/// <summary>
/// A single row on the Task Page paper.
/// Call <see cref="Bind"/> to populate the row from a threat.
/// Rows always show their ThreatDescription (e.g. "2/5") inline after the name — a task's row
/// is destroyed by <see cref="TaskPage"/> the moment it's no longer active, so no completed
/// state or strikethrough is ever rendered here.
/// Subscribes to TaskRegistry.OnTaskStateChanged so the description stays live.
///
/// Sizing: long labels first shrink from <see cref="_maxFontSize"/> toward <see cref="_minFontSize"/>
/// to stay on one line; once at the minimum they wrap, and the parent Vertical Layout Group
/// (Control Child Size → Height) grows the row downward. TMP auto-size must stay OFF on the label.
/// </summary>
public class TaskPageRow : MonoBehaviour
{
    [Tooltip("TextMeshProUGUI label that displays the task name and progress (inside a world-space Canvas).")]
    [SerializeField] private TextMeshProUGUI _label;

    [Header("Shrink Then Wrap")]
    [Tooltip("Font size used when the label fits on one line.")]
    [SerializeField] private float _maxFontSize = 9.4f;

    [Tooltip("Smallest font size the label shrinks to before it starts wrapping onto more lines.")]
    [SerializeField] private float _minFontSize = 7.5f;

    private const float FontStep = 0.1f;

    private ISystemicThreat _threat;
    private string _fittedText;
    private TMP_FontAsset _fittedFont;
    private float _fittedWidth = -1f;

    private void Awake()
    {
        if (_label == null)
            _label = GetComponent<TextMeshProUGUI>();
        if (_label != null)
            _label.enableAutoSizing = false;
    }

    private void OnEnable()
    {
        TaskRegistry.OnTaskStateChanged += Refresh;
    }

    private void OnDisable()
    {
        TaskRegistry.OnTaskStateChanged -= Refresh;
    }

    /// <summary>
    /// Re-fits when the text, font (e.g. LocalizedText language font swap) or row width changes.
    /// </summary>
    private void LateUpdate()
    {
        if (_label == null) return;
        if (_label.text != _fittedText || _label.font != _fittedFont ||
            !Mathf.Approximately(_label.rectTransform.rect.width, _fittedWidth))
        {
            FitFontSize();
        }
    }

    /// <summary>Populates the row with the given threat.</summary>
    public void Bind(ISystemicThreat threat)
    {
        _threat = threat;
        Refresh();
    }

    private void Refresh()
    {
        if (_label == null || _threat == null) return;

        string name = Loc.TranslateSource(_threat.ThreatName);
        string description = Loc.TranslateSource(_threat.ThreatDescription);
        _label.text = Loc.Verbatim(string.IsNullOrWhiteSpace(description)
            ? $"- {name}"
            : $"- {name} ({description})");

        FitFontSize();
    }

    /// <summary>
    /// Picks the largest font size in [min, max] at which the label fits on a single line.
    /// If it doesn't fit even at min, min is used and TMP word-wrapping takes over.
    /// </summary>
    private void FitFontSize()
    {
        _fittedText = _label.text;
        _fittedFont = _label.font;
        _fittedWidth = _label.rectTransform.rect.width;

        float maxSize = Mathf.Max(_maxFontSize, _minFontSize);
        float minSize = Mathf.Min(_maxFontSize, _minFontSize);
        Vector4 margin = _label.margin;
        float available = _fittedWidth - margin.x - margin.z;

        float size = maxSize;
        if (available > 0f && !string.IsNullOrEmpty(_fittedText))
        {
            _label.fontSize = maxSize;
            float singleLineWidth = _label.GetPreferredValues(_fittedText, float.PositiveInfinity, float.PositiveInfinity).x;

            if (singleLineWidth > available)
            {
                // Width scales ~linearly with font size: estimate, then step down to correct for rounding/kerning.
                size = Mathf.Clamp(maxSize * available / singleLineWidth, minSize, maxSize);
                _label.fontSize = size;
                while (size > minSize &&
                       _label.GetPreferredValues(_fittedText, float.PositiveInfinity, float.PositiveInfinity).x > available)
                {
                    size = Mathf.Max(minSize, size - FontStep);
                    _label.fontSize = size;
                }
            }
        }

        if (!Mathf.Approximately(_label.fontSize, size))
            _label.fontSize = size;
    }
}
