using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single source of truth for every piece of guidebook content.
///
/// <see cref="GuidebookBuilder"/> reads this asset each time the guidebook opens and lays the
/// content out onto pooled sheets: rule pages first, then the scoring/disposition page, then one
/// section per <see cref="AnomalyCategory"/> that has at least one unlocked anomaly.
///
/// Anomaly entries never store a category or a verdict:
/// <list type="bullet">
///   <item>Category is derived from the anomaly's C# base class via
///   <see cref="AnomalyCategoryExtensions.FromAnomalyTypeName"/>.</item>
///   <item>Point cost is read live from <see cref="AnomalyController.GetAnomalyPointCost"/>.</item>
///   <item>Verdicts are score-based (Conversion Risk Protocol) and live in
///   <see cref="DispositionBands"/>, never on an individual anomaly.</item>
/// </list>
/// </summary>
[CreateAssetMenu(menuName = "Good Cop Bad Cop/Guidebook Database", fileName = "GuidebookDatabase")]
public class GuidebookDatabase : ScriptableObject
{
    // -------------------------------------------------------------------------
    // Inner types
    // -------------------------------------------------------------------------

    [Serializable]
    public class RulePage
    {
        [Tooltip("Small caps line above the title (e.g. 'PROCEDURE').")]
        public string Header = "PROCEDURE";

        public string Title;

        [TextArea(4, 12)]
        public string Body;

        [Tooltip("Optional illustration. When empty the body text fills the space.")]
        public Sprite Image;
    }

    [Serializable]
    public class DispositionBand
    {
        public int MinScore;
        public int MaxScore;
        public string Status;
        public string Action;
    }

    [Serializable]
    public class CategoryStyle
    {
        public AnomalyCategory Category;

        [Tooltip("Full section name shown on the section intro and anomaly page headers.")]
        public string DisplayName;

        [Tooltip("Short label printed on the physical tab.")]
        public string TabLabel;

        public Color TabColor = Color.white;

        [TextArea(2, 6)]
        public string Intro;

        [Tooltip("Tools used to inspect this category (e.g. 'UV flashlight, Geiger scanner').")]
        public string Tools;
    }

    [Serializable]
    public class AnomalyEntry
    {
        [Tooltip("Exact C# class name of the anomaly (e.g. 'BlueVeinsAnomaly'). Must match " +
                 "AnomalyUnlockManager / ChecklistItem type names.")]
        public string AnomalyTypeName;

        public string DisplayName;

        public Sprite Illustration;

        [TextArea(2, 6)]
        public string Description;

        [TextArea(2, 6)]
        public string HowToDetect;
    }

    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------

    [Header("Rules Section")]
    [SerializeField] private string _rulesTabLabel = "RULES";
    [SerializeField] private Color  _rulesTabColor = new Color(0.82f, 0.78f, 0.66f, 1f);
    [SerializeField] private RulePage[] _rulePages = Array.Empty<RulePage>();

    [Header("Scoring & Disposition Page")]
    [SerializeField] private string _scoringHeader = "CONVERSION RISK PROTOCOL";
    [SerializeField] private string _scoringTitle  = "FINAL DISPOSITION";
    [TextArea(2, 6)]
    [SerializeField] private string _scoringIntro;
    [SerializeField] private DispositionBand[] _dispositionBands = Array.Empty<DispositionBand>();
    [TextArea(1, 4)]
    [SerializeField] private string _dispositionWarning;

    [Header("Anomaly Sections (in book order)")]
    [SerializeField] private CategoryStyle[] _categories = Array.Empty<CategoryStyle>();

    [Header("Anomaly Entries (in-section order)")]
    [SerializeField] private AnomalyEntry[] _entries = Array.Empty<AnomalyEntry>();

    [Header("Filler")]
    [Tooltip("Header shown on blank pages inserted to keep each section starting on a spread.")]
    [SerializeField] private string _notesHeader = "FIELD NOTES";

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public string RulesTabLabel => _rulesTabLabel;
    public Color  RulesTabColor => _rulesTabColor;
    public IReadOnlyList<RulePage> RulePages => _rulePages;

    public string ScoringHeader => _scoringHeader;
    public string ScoringTitle  => _scoringTitle;
    public string ScoringIntro  => _scoringIntro;
    public IReadOnlyList<DispositionBand> DispositionBands => _dispositionBands;
    public string DispositionWarning => _dispositionWarning;

    public IReadOnlyList<CategoryStyle> Categories => _categories;
    public IReadOnlyList<AnomalyEntry>  Entries    => _entries;

    public string NotesHeader => _notesHeader;

    /// <summary>
    /// Resolves the category of an anomaly entry from its C# base class.
    /// Returns null if the type name does not resolve to a known category.
    /// </summary>
    public static AnomalyCategory? GetCategory(AnomalyEntry entry) =>
        entry == null ? null : AnomalyCategoryExtensions.FromAnomalyTypeName(entry.AnomalyTypeName);

#if UNITY_EDITOR
    private void OnValidate()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (AnomalyEntry entry in _entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.AnomalyTypeName)) continue;

            if (!seen.Add(entry.AnomalyTypeName))
                Debug.LogWarning($"[GuidebookDatabase] Duplicate entry for '{entry.AnomalyTypeName}'.", this);

            if (GetCategory(entry) == null)
                Debug.LogWarning($"[GuidebookDatabase] '{entry.AnomalyTypeName}' does not resolve to an anomaly " +
                                 "class with a known category. Check the spelling against the C# class name.", this);
        }
    }
#endif
}
