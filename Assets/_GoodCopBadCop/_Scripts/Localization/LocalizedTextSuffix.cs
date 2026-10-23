using TMPro;
using UnityEngine;

/// <summary>
/// Appends a fixed, non-localized suffix (e.g. a TMP sprite tag such as " &lt;sprite=0&gt;") to a TMP text AFTER
/// render-time localization. It takes the place of <see cref="LocalizationTextRuntime"/> as the text's
/// <see cref="ITextPreprocessor"/>, runs the same <see cref="Loc.LocalizeRendered"/> pass on the assigned text, then
/// adds the suffix. The suffix is therefore never part of the translation lookup, and the assigned <c>text</c> is
/// never rewritten.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(TMP_Text))]
public sealed class LocalizedTextSuffix : MonoBehaviour, ITextPreprocessor
{
    [Tooltip("Appended to the text after localization. Not translated.")]
    [SerializeField] private string suffix = " <sprite=0>";

    private TMP_Text _text;

    public string Suffix
    {
        get => suffix;
        set { suffix = value; MarkDirty(); }
    }

    private void Awake() => Claim();

    private void OnEnable()
    {
        // Edit mode: re-claim after domain reloads so the suffix also previews in the Scene view.
        Claim();
        Loc.LanguageChanged += MarkDirty;
    }

    private void OnDisable()
    {
        Loc.LanguageChanged -= MarkDirty;
        if (_text != null && ReferenceEquals(_text.textPreprocessor, this))
        {
            _text.textPreprocessor = null;
            MarkDirty();
        }
    }

#if UNITY_EDITOR
    private void OnValidate() => MarkDirty();
#endif

    /// <summary>Claims the preprocessor before LocalizationTextRuntime's LateUpdate scan assigns its own.</summary>
    private void Claim()
    {
        if (_text == null) _text = GetComponent<TMP_Text>();
        if (_text == null) return;
        _text.textPreprocessor = this;
        MarkDirty();
    }

    public string PreprocessText(string text)
    {
        // Edit mode shows the authored English; localization only applies in Play mode.
        string localized = Application.isPlaying ? Loc.LocalizeRendered(text) : text;
        return string.IsNullOrEmpty(suffix) ? localized : localized + suffix;
    }

    private void MarkDirty()
    {
        if (_text != null) _text.havePropertiesChanged = true;
    }
}
