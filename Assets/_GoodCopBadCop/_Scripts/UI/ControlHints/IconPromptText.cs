using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Static "sentence with an input icon" label ("Hold {0} to close", "{0} to jump") for authored UI that has no script of its own.
/// The localized template is split at "{0}" through <see cref="IconPromptLabels"/>: text before the icon goes to
/// <see cref="_prefixLabel"/> (cloned on demand when the row has a HorizontalLayoutGroup), text after it to this label.
/// Without an icon in a shared layout row the whole sentence stays in this label, so free-positioned labels next to an icon
/// column still read correctly in every language. Keep the label's LocalizedText for the language font, with an empty key.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class IconPromptText : MonoBehaviour, IIconPromptDriver
{
    [Tooltip("Prompt sentence; {0} is the key / button icon, e.g. \"Hold {0} to close\".")]
    [SerializeField, LocalizeField] private string _template = "{0}";

    [Tooltip("Icon in the same HorizontalLayoutGroup row. Leave empty for labels that are positioned on their own.")]
    [SerializeField] private RectTransform _icon;

    [Tooltip("Optional existing label left of the icon. Cloned from this label when a translation needs one and it is empty.")]
    [SerializeField] private TMP_Text _prefixLabel;

    private IconPromptLabels _labels;

    public IEnumerable<TMP_Text> DrivenLabels
    {
        get
        {
            yield return GetComponent<TMP_Text>();
            if (_prefixLabel != null) yield return _prefixLabel;
        }
    }

    private void OnEnable()
    {
        Loc.LanguageChanged += Apply;
        Apply();
    }

    private void OnDisable()
    {
        Loc.LanguageChanged -= Apply;
    }

    private void Apply()
    {
        _labels ??= new IconPromptLabels(GetComponent<TMP_Text>(), _icon, _prefixLabel);
        _labels.Apply(IconPrompt.Localize(_template));
    }
}
