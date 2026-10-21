using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Keeps related labels (a row of buttons, dialogue choices, tabs) at one shared font size:
/// each member auto-sizes within its own range, then all of them use the smallest result.
/// Members keep their design range in this component; their own auto-size is switched off while grouped.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class TextSizeGroup : MonoBehaviour
{
    [System.Serializable]
    public class Member
    {
        public TMP_Text text;
        public float maxSize = 36f;
        public float minSize = 24f;
    }

    [Tooltip("Labels sharing one size. Leave empty and press 'Collect children' to fill from child TMP texts.")]
    public List<Member> members = new List<Member>();

    private readonly List<string> _lastTexts = new List<string>();
    private Vector2 _lastSize;
    private bool _dirty = true;

    private void OnEnable()
    {
        _dirty = true;
        Loc.LanguageChanged += MarkDirty;
    }

    private void OnDisable()
    {
        Loc.LanguageChanged -= MarkDirty;
    }

    public void MarkDirty() { _dirty = true; }

    private void OnRectTransformDimensionsChange() { _dirty = true; }

    private void OnValidate() { _dirty = true; }

    [ContextMenu("Collect children")]
    public void CollectChildren()
    {
        members.Clear();
        foreach (var t in GetComponentsInChildren<TMP_Text>(true))
        {
            float max = t.enableAutoSizing ? t.fontSizeMax : t.fontSize;
            float min = t.enableAutoSizing ? t.fontSizeMin : Mathf.Round(max * 0.7f);
            members.Add(new Member { text = t, maxSize = max, minSize = min });
        }
        _dirty = true;
    }

    private void LateUpdate()
    {
        if (!_dirty && !Changed()) return;
        _dirty = false;
        Refit();
    }

    private bool Changed()
    {
        if (_lastTexts.Count != members.Count) return true;
        for (int i = 0; i < members.Count; i++)
        {
            var t = members[i].text;
            if (t != null && !ReferenceEquals(t.text, _lastTexts[i]) && t.text != _lastTexts[i]) return true;
        }
        var rt = transform as RectTransform;
        return rt != null && rt.rect.size != _lastSize;
    }

    /// <summary>Finds the largest size every active member can use and applies it to all of them.</summary>
    public void Refit()
    {
        float shared = float.MaxValue;
        _lastTexts.Clear();

        foreach (var m in members)
        {
            _lastTexts.Add(m.text != null ? m.text.text : null);
            if (m.text == null || !m.text.isActiveAndEnabled || string.IsNullOrEmpty(m.text.text)) continue;
            var t = m.text;
            t.fontSizeMax = m.maxSize;
            t.fontSizeMin = m.minSize;
            t.enableAutoSizing = true;
            t.ForceMeshUpdate(true);
            shared = Mathf.Min(shared, t.fontSize);
        }

        if (shared == float.MaxValue) return;

        foreach (var m in members)
        {
            if (m.text == null) continue;
            m.text.enableAutoSizing = false;
            m.text.fontSize = Mathf.Clamp(shared, m.minSize, m.maxSize);
        }

        var rt = transform as RectTransform;
        if (rt != null) _lastSize = rt.rect.size;
    }
}
