using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

    [Tooltip("Include inactive members when finding the shared size, so it doesn't change as rows toggle.")]
    public bool measureInactive;

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

    /// <summary>Adds a text with its design range (used for rows created at runtime). Duplicates are ignored.</summary>
    public void AddMember(TMP_Text text, float maxSize, float minSize)
    {
        if (text == null || members.Exists(m => m.text == text)) return;
        members.Add(new Member { text = text, maxSize = maxSize, minSize = minSize });
        _dirty = true;
    }

    private void OnRectTransformDimensionsChange() { _dirty = true; }

    private void OnValidate() { _dirty = true; }

    [ContextMenu("Collect children")]
    public void CollectChildren()
    {
        // Keep existing design ranges: a grouped text's fontSize is the shrunk shared size, not its max.
        var previous = new List<Member>(members);
        members.Clear();
        foreach (var t in GetComponentsInChildren<TMP_Text>(true))
        {
            var existing = previous.Find(m => m.text == t);
            if (existing != null) { members.Add(existing); continue; }
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
        _lastTexts.Clear();
        foreach (var m in members) _lastTexts.Add(m.text != null ? m.text.text : null);

        float shared = TryGetLayoutRow(out var row) ? FitRow(row) : FitEachInOwnRect();
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

    private bool IsMeasured(Member m) =>
        m.text != null && (m.text.isActiveAndEnabled || measureInactive) && !string.IsNullOrEmpty(m.text.text);

    /// <summary>
    /// True when this object has a HorizontalLayoutGroup that sizes its children and every measured member
    /// is a direct child. Those labels are sized by their own text, so their rects can't be used as the fit box.
    /// </summary>
    private bool TryGetLayoutRow(out HorizontalLayoutGroup row)
    {
        row = GetComponent<HorizontalLayoutGroup>();
        if (row == null || !row.enabled || !row.childControlWidth) return false;
        foreach (var m in members)
            if (IsMeasured(m) && m.text.transform.parent != transform) return false;
        return true;
    }

    /// <summary>Largest shared size where the whole row (members + other children + spacing) fits this rect.</summary>
    private float FitRow(HorizontalLayoutGroup row)
    {
        var rt = (RectTransform)transform;
        var box = rt.rect.size;
        if (box.x <= 0f || box.y <= 0f) { _dirty = true; return float.MaxValue; }

        float availW = box.x - row.padding.horizontal;
        float availH = box.y - row.padding.vertical;
        int laidOut = 0;
        float lo = float.MaxValue, hi = 0f;
        var measured = new List<Member>();

        foreach (Transform child in transform)
        {
            if (!child.gameObject.activeInHierarchy) continue;
            if (child.TryGetComponent<ILayoutIgnorer>(out var ig) && ig.ignoreLayout) continue;
            laidOut++;
            var member = members.Find(m => m.text != null && m.text.transform == child);
            if (member != null && IsMeasured(member)) continue;
            var crt = (RectTransform)child;
            float w = LayoutUtility.GetPreferredWidth(crt);
            if (row.childScaleWidth) w *= crt.localScale.x;
            availW -= w;
        }
        availW -= row.spacing * Mathf.Max(0, laidOut - 1);

        foreach (var m in members)
        {
            if (!IsMeasured(m)) continue;
            measured.Add(m);
            lo = Mathf.Min(lo, m.minSize);
            hi = Mathf.Max(hi, m.maxSize);
        }
        if (measured.Count == 0) return float.MaxValue;
        if (Fits(measured, hi, availW, availH)) return hi;

        for (int i = 0; i < 14 && hi - lo > 0.05f; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (Fits(measured, mid, availW, availH)) lo = mid; else hi = mid;
        }
        return Mathf.Floor(lo * 10f) / 10f;
    }

    private static bool Fits(List<Member> measured, float size, float availW, float availH)
    {
        float totalW = 0f, maxH = 0f;
        foreach (var m in measured)
        {
            var t = m.text;
            t.enableAutoSizing = false;
            t.fontSize = Mathf.Clamp(size, m.minSize, m.maxSize);
            var pref = t.GetPreferredValues(t.text, float.PositiveInfinity, float.PositiveInfinity);
            totalW += pref.x;
            maxH = Mathf.Max(maxH, pref.y);
        }
        return totalW <= availW && maxH <= availH;
    }

    /// <summary>Members with fixed rects: auto-size each in its own rect and take the smallest result.</summary>
    private float FitEachInOwnRect()
    {
        float shared = float.MaxValue;
        bool measuredAll = true;

        foreach (var m in members)
        {
            if (!IsMeasured(m)) continue;
            var t = m.text;
            var r = t.rectTransform.rect;
            if (r.width <= 0f || r.height <= 0f) { measuredAll = false; continue; }
            t.fontSizeMax = m.maxSize;
            t.fontSizeMin = m.minSize;
            // Start from max: TMP auto-size begins at the stored base size, which is the
            // previously applied (shrunk) shared size unless we reset it here.
            t.fontSize = m.maxSize;
            t.enableAutoSizing = true;
            t.ForceMeshUpdate(true);
            shared = Mathf.Min(shared, t.fontSize);
        }

        // A member had no layout size yet; try again next frame instead of locking in min size.
        if (!measuredAll) _dirty = true;
        return shared;
    }
}
