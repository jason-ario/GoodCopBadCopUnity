using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Keeps a set of auto-sizing TMP labels at one shared font size: the largest size at which every active label
/// still fits. Re-fits when the text, font, rect width or language changes, so localized menus (for example the
/// main menu buttons in Russian) don't end up with each label auto-sized differently.
/// </summary>
[DisallowMultipleComponent]
public sealed class TMPAutoSizeGroup : MonoBehaviour
{
    [Tooltip("Labels to keep at one size. If empty, every TMP label under this object with auto-size on is used.")]
    [SerializeField] private List<TMP_Text> _labels = new List<TMP_Text>();

    private readonly Dictionary<TMP_Text, float> _baseMax = new Dictionary<TMP_Text, float>();
    private readonly StringBuilder _sig = new StringBuilder();
    private readonly Dictionary<TMP_Text, float> _applied = new Dictionary<TMP_Text, float>();
    private string _lastSignature;

    private void OnEnable()
    {
        if (_labels.Count == 0)
            foreach (var t in GetComponentsInChildren<TMP_Text>(true))
                if (t.enableAutoSizing) _labels.Add(t);
        _lastSignature = null;
        Loc.LanguageChanged += MarkDirty;
    }

    private void OnDisable()
    {
        Loc.LanguageChanged -= MarkDirty;
    }

    public void MarkDirty() => _lastSignature = null;

    private void LateUpdate()
    {
        string signature = BuildSignature();
        if (signature == _lastSignature) return;
        _lastSignature = signature;
        Fit();
    }

    private string BuildSignature()
    {
        _sig.Clear().Append(Loc.CurrentLanguage);
        foreach (var t in _labels)
        {
            if (t == null) continue;
            _sig.Append('|').Append(t.isActiveAndEnabled ? '1' : '0')
                .Append(t.text).Append(t.font != null ? t.font.name : "")
                .Append(Mathf.RoundToInt(t.rectTransform.rect.width));
        }
        return _sig.ToString();
    }

    private void Fit()
    {
        float shared = float.MaxValue;
        foreach (var t in _labels)
        {
            if (t == null || !t.isActiveAndEnabled || !t.enableAutoSizing) continue;

            // Something else (LocalizedText on a language change) reset the max: take it as the new authored max.
            if (!_baseMax.TryGetValue(t, out float baseMax) ||
                !_applied.TryGetValue(t, out float applied) || !Mathf.Approximately(t.fontSizeMax, applied))
            {
                baseMax = t.fontSizeMax;
                _baseMax[t] = baseMax;
            }

            t.fontSizeMax = baseMax;
            t.ForceMeshUpdate();
            shared = Mathf.Min(shared, t.fontSize);
        }

        if (shared == float.MaxValue) return;

        foreach (var t in _labels)
        {
            if (t == null || !t.isActiveAndEnabled || !t.enableAutoSizing) continue;
            t.fontSizeMax = Mathf.Max(shared, t.fontSizeMin);
            _applied[t] = t.fontSizeMax;
            t.ForceMeshUpdate();
        }
    }
}
