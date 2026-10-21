using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// View for one "{prefix} [icon] {text}" row. Lives on the root of the <c>Control Hint Row</c> prefab,
/// which <see cref="ControlHintsOverlay"/> instantiates at runtime. Restyle rows by editing the prefab.
/// </summary>
public class ControlHintRow : MonoBehaviour
{
    [Tooltip("Optional label before the icon (\"Hold\"). Hidden when the hint has no prefix.")]
    [SerializeField] private TMP_Text _prefixLabel;

    [Tooltip("Key / button icon.")]
    [SerializeField] private Image _icon;

    [Tooltip("Label after the icon.")]
    [SerializeField] private TMP_Text _label;

    /// <summary>Prefix label, registered with the overlay's shared TextSizeGroup.</summary>
    public TMP_Text PrefixLabel => _prefixLabel;

    /// <summary>Main label, registered with the overlay's shared TextSizeGroup.</summary>
    public TMP_Text Label => _label;

    public void Apply(string prefix, Sprite icon, string text)
    {
        bool hasPrefix = !string.IsNullOrEmpty(prefix);
        if (_prefixLabel != null)
        {
            // Hidden labels are cleared so they don't take part in the shared size.
            string value = hasPrefix ? prefix : string.Empty;
            if (_prefixLabel.text != value) _prefixLabel.text = value;
            SetActive(_prefixLabel.gameObject, hasPrefix);
        }

        if (_icon != null)
        {
            if (icon != null) _icon.sprite = icon;
            SetActive(_icon.gameObject, icon != null);
        }

        if (_label != null)
        {
            string value = text ?? string.Empty;
            if (_label.text != value) _label.text = value;
            SetActive(_label.gameObject, value.Length > 0);
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
