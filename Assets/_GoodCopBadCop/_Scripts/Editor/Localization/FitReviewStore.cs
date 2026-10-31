using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Per-key pseudo-fit review decisions, saved to Localization/FitReview.json. A decision is tied to the English source hash,
/// so editing the English sends the key back to "to review".
/// </summary>
[Serializable]
public class FitReviewStore
{
    public const string Approved = "approved"; // fits by eye, no character limit
    public const string Limit = "limit";       // too small: max_chars = limit
    public const string FilePath = LocalizationPaths.Root + "/FitReview.json";

    [Serializable]
    public class Decision
    {
        public string key;
        public string decision;
        public int limit;
        public string sourceHash;
        public string date;
    }

    public List<Decision> items = new List<Decision>();

    [NonSerialized] private Dictionary<string, Decision> _byKey;

    public static FitReviewStore Load()
    {
        string abs = LocalizationPaths.Abs(FilePath);
        FitReviewStore s = null;
        if (File.Exists(abs))
        {
            try { s = JsonUtility.FromJson<FitReviewStore>(File.ReadAllText(abs)); }
            catch (Exception e) { Debug.LogWarning("FitReview.json could not be read: " + e.Message); }
        }
        if (s == null) s = new FitReviewStore();
        s.Index();
        return s;
    }

    public void Save()
    {
        LocalizationPaths.EnsureFolders();
        items.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
        File.WriteAllText(LocalizationPaths.Abs(FilePath), JsonUtility.ToJson(this, true));
        AssetDatabase.ImportAsset(FilePath);
    }

    private void Index()
    {
        _byKey = new Dictionary<string, Decision>();
        foreach (var d in items) if (d != null && !string.IsNullOrEmpty(d.key)) _byKey[d.key] = d;
    }

    /// <summary>The decision for a key, or null if there is none or the English changed since it was made.</summary>
    public Decision Get(string key, string source)
    {
        if (_byKey == null) Index();
        if (key == null || !_byKey.TryGetValue(key, out var d)) return null;
        return d.sourceHash == LocalizationTable.Hash(source) ? d : null;
    }

    public void Set(string key, string source, string decision, int limit)
    {
        if (_byKey == null) Index();
        if (!_byKey.TryGetValue(key, out var d))
        {
            d = new Decision { key = key };
            items.Add(d);
            _byKey[key] = d;
        }
        d.decision = decision;
        d.limit = decision == Limit ? Mathf.Max(1, limit) : 0;
        d.sourceHash = LocalizationTable.Hash(source);
        d.date = DateTime.Now.ToString("yyyy-MM-dd");
    }

    public void Clear(string key)
    {
        if (_byKey == null) Index();
        if (_byKey.TryGetValue(key, out var d)) { items.Remove(d); _byKey.Remove(key); }
    }
}
