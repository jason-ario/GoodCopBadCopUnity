using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// View for one "[before] [icon] [after]" row, filled from a single localized <see cref="IconPrompt"/> template
/// ("Hold {0} to rotate"). Lives on the root of the <c>Control Hint Row</c> prefab, which
/// <see cref="ControlHintsOverlay"/> instantiates at runtime. Restyle rows by editing the prefab.
/// </summary>
public class ControlHintRow : MonoBehaviour, IIconPromptDriver
{
    [Tooltip("Label before the icon (text left of {0}). Hidden when the template starts with the icon.")]
    [SerializeField] private TMP_Text _prefixLabel;

    [Tooltip("Key / button icon.")]
    [SerializeField] private Image _icon;

    [Tooltip("Label after the icon (text right of {0}).")]
    [SerializeField] private TMP_Text _label;

    private IconPromptLabels _labels;

    /// <summary>Prefix label, registered with the overlay's shared TextSizeGroup.</summary>
    public TMP_Text PrefixLabel => _prefixLabel;

    /// <summary>Main label, registered with the overlay's shared TextSizeGroup.</summary>
    public TMP_Text Label => _label;

    public System.Collections.Generic.IEnumerable<TMP_Text> DrivenLabels => new[] { _prefixLabel, _label };

    /// <summary>Fills the row from a localized template ("{0}" = icon). Hidden labels are cleared so they don't take part in the shared size.</summary>
    public void Apply(string localizedTemplate, Sprite icon)
    {
        _labels ??= new IconPromptLabels(_label, _icon, _prefixLabel);
        _labels.Apply(localizedTemplate);

        if (_icon != null)
        {
            if (icon != null) _icon.sprite = icon;
            SetActive(_icon.gameObject, icon != null);
        }
    }

    /// <summary>Clears both labels for a pooled row that is being hidden.</summary>
    public void Clear()
    {
        if (_prefixLabel != null) _prefixLabel.text = string.Empty;
        if (_label != null) _label.text = string.Empty;
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go.activeSelf != active) go.SetActive(active);
    }
}
