using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single source of truth for every piece of guidebook content.
///
/// <see cref="GuidebookBuilder"/> reads this asset each time the guidebook opens and lays the
/// content out onto pooled sheets: rule pages first, then the scoring/disposition page, then the Checkpoint Integrity pages,
/// then the Survival pages (health, radiation), then one
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

        [Tooltip("Optional row of illustrations shown side by side. Replaces Image when set.")]
        public Sprite[] ImageRow;

        [Tooltip("Draws the printed HUD Checkpoint Integrity panel (see the database's Integrity Panel) " +
                 "in the image slot instead of Image / Image Row.")]
        public bool ShowIntegrityPanel;

        [Tooltip("Draws a printed copy of a HUD element (health bar / Geiger counter, see the database's " +
                 "Survival Example) in the image slot instead of Image / Image Row.")]
        public GuidebookHudArt HudArt;

        [Tooltip("Optional illustrated rows (icon, bold label, text) printed below the body.")]
        public GuidebookIconItem[] IconList;
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

    [Header("Checkpoint Integrity Section (after Rules & Scoring)")]
    [SerializeField] private string _integrityTabLabel = "INTEGRITY";
    [SerializeField] private Color  _integrityTabColor = new Color(0.55f, 0.66f, 0.42f, 1f);
    [Tooltip("Pages of the Checkpoint Integrity section. " + IntegrityMaxDeductionToken +
             " in a body is replaced with the live maximum payout deduction (e.g. 50).")]
    [SerializeField] private RulePage[] _integrityPages = DefaultIntegrityPages();
    [Tooltip("Example state of the HUD panel copy on pages with Show Integrity Panel (the art is copied from the scene's HUD).")]
    [SerializeField] private GuidebookIntegrityPanel _integrityPanel = new GuidebookIntegrityPanel();

    [Header("Survival Section (after Checkpoint Integrity)")]
    [SerializeField] private string _survivalTabLabel = "SURVIVAL";
    [SerializeField] private Color  _survivalTabColor = new Color(0.78f, 0.45f, 0.38f, 1f);
    [Tooltip("Pages of the Survival section (health, radiation).")]
    [SerializeField] private RulePage[] _survivalPages = DefaultSurvivalPages();
    [Tooltip("Example readings of the HUD copies on pages with Hud Art (the art is copied from the scene's HUD).")]
    [SerializeField] private GuidebookSurvivalExample _survivalExample = new GuidebookSurvivalExample();

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

    /// <summary>Placeholder in integrity page bodies for the live maximum payout deduction percentage.</summary>
    public const string IntegrityMaxDeductionToken = "{MAX_DEDUCTION}";

    public string IntegrityTabLabel => _integrityTabLabel;
    public Color  IntegrityTabColor => _integrityTabColor;
    public IReadOnlyList<RulePage> IntegrityPages => _integrityPages;
    public GuidebookIntegrityPanel IntegrityPanel => _integrityPanel;

    public string SurvivalTabLabel => _survivalTabLabel;
    public Color  SurvivalTabColor => _survivalTabColor;
    public IReadOnlyList<RulePage> SurvivalPages => _survivalPages;
    public GuidebookSurvivalExample SurvivalExample => _survivalExample;

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
    /// Starting content for the Checkpoint Integrity section. Only used while the asset has no
    /// serialized <c>_integrityPages</c> yet; after that the asset's (Inspector-edited) copy wins.
    /// </summary>
    private static RulePage[] DefaultIntegrityPages() => new[]
    {
        new RulePage
        {
            Header = "CHECKPOINT INTEGRITY",
            Title  = "KEEP THE YARD CLEAN",
            Body   = IntegrityBody,
            ShowIntegrityPanel = true,
        },
        new RulePage
        {
            Header = "CHECKPOINT INTEGRITY",
            Title  = "CHORES",
            Body   = IntegrityChoresBody,
            IconList = new[]
            {
                new GuidebookIconItem { Label = "TRASH & GORE",     Text = "Bag it, then throw the bag in the dumpster." },
                new GuidebookIconItem { Label = "GRAFFITI & BLOOD", Text = "Scrub it away with the mop." },
                new GuidebookIconItem { Label = "FENCES",           Text = "Repair broken fences with the hammer." },
            },
        },
    };

    public const string IntegrityChoresBody = "Clear every mess to keep integrity at 100%.";

    public const string IntegrityBody =
        "Your pay is multiplied by the checkpoint's integrity, shown on this panel.\n\n" +
        "Below 100%, the difference is deducted from your earnings, up to " + IntegrityMaxDeductionToken + "%.\n\n" +
        "The counters show what's left to clean. Your compass marks every mess.";

    /// <summary>
    /// Starting content for the Survival section. Only used while the asset has no serialized
    /// <c>_survivalPages</c> yet; after that the asset's (Inspector-edited) copy wins.
    /// </summary>
    private static RulePage[] DefaultSurvivalPages() => new[]
    {
        new RulePage
        {
            Header = "SURVIVAL",
            Title  = "STAY ALIVE",
            Body   = SurvivalHealthBody,
            HudArt = GuidebookHudArt.HealthBar,
        },
        new RulePage
        {
            Header = "SURVIVAL",
            Title  = "RADIATION",
            Body   = SurvivalRadiationBody,
            HudArt = GuidebookHudArt.GeigerCounter,
        },
    };

    public const string SurvivalHealthBody =
        "This bar shows your health. Injuries and radiation wear it down.\n\n" +
        "If your health reaches 0%, you die.\n\n" +
        "Eat food or smoke a cigarette to restore health. Your health resets at the start of the next day.";

    public const string SurvivalRadiationBody =
        "The Geiger counter shows your radiation. The needle reacts to how fast you are being exposed; " +
        "the meter and reading show how much you have absorbed.\n\n" +
        "When your radiation is high, your health declines. The higher it climbs, the faster you lose health.\n\n" +
        "Take radiation pills to bring your radiation down. Your radiation resets at the start of the next day.";

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
