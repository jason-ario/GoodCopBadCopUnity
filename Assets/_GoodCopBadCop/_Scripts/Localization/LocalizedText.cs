using TMPro;
using UnityEngine;

/// <summary>Draws a string field as a dropdown of the font roles defined in LocalizationSettings.</summary>
public class FontRoleFieldAttribute : PropertyAttribute { }

/// <summary>
/// Drives a <see cref="TMP_Text"/> from a localization key. Refreshes on enable and whenever the language changes,
/// and applies the per-language font and size scale from <see cref="LocalizationSettings"/>.
/// Subtitles and code-driven text call <see cref="Loc.Get(string)"/> directly instead.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public class LocalizedText : MonoBehaviour
{
    [Tooltip("Localization key, for example ui.mainmenu.play.text")]
    public string key;

    [Tooltip("Apply the per-language font / size scale from LocalizationSettings.")]
    public bool applyLanguageFont = true;

    [FontRoleField]
    [Tooltip("Font role override (for example UI or Dialogue). Auto = use the role the label's own font is assigned to " +
             "in the Localization window (Fonts > Languages > Font roles).")]
    public string fontRole = "";

    private TMP_Text _text;
    private TMP_FontAsset _originalFont;
    private float _originalSize, _originalMax, _originalMin;
    private bool _captured;

    public TMP_Text Text
    {
        get
        {
            if (_text == null) _text = GetComponent<TMP_Text>();
            return _text;
        }
    }

    private void OnEnable()
    {
        Loc.LanguageChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        Loc.LanguageChanged -= Refresh;
    }

    public void SetKey(string newKey)
    {
        key = newKey;
        Refresh();
    }

    public void Refresh()
    {
        var t = Text;
        if (t == null || string.IsNullOrEmpty(key)) return;

        if (!_captured)
        {
            _originalFont = t.font;
            _originalSize = t.fontSize;
            _originalMax = t.fontSizeMax;
            _originalMin = t.fontSizeMin;
            _captured = true;
        }

        // Missing keys leave the authored text untouched.
        if (Loc.TryGet(key, out string value))
            t.text = value;

        if (applyLanguageFont) ApplyFont(t);
    }

    private void ApplyFont(TMP_Text t)
    {
        var settings = LocalizationSettings.Instance;
        var lang = settings != null ? settings.Find(Loc.CurrentLanguage) : null;

        TMP_FontAsset font = _originalFont;
        float scale = 1f;
        if (lang != null)
        {
            font = settings.ResolveFont(lang, _originalFont, fontRole);
            if (lang.sizeScale > 0f) scale = lang.sizeScale;

            if (Application.isPlaying && font != null && lang.fallbackFonts != null)
                foreach (var fb in lang.fallbackFonts)
                    if (fb != null && fb != font && !font.fallbackFontAssetTable.Contains(fb))
                        font.fallbackFontAssetTable.Add(fb);
        }

        if (font != null && t.font != font) t.font = font;
        t.fontSize = _originalSize * scale;
        if (t.enableAutoSizing)
        {
            t.fontSizeMax = _originalMax * scale;
            t.fontSizeMin = _originalMin * scale;
        }
    }
}
