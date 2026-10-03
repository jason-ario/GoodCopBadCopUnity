using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Lays the <see cref="GuidebookDatabase"/> out onto pooled <see cref="GuidebookSheet"/>s each time
/// the guidebook opens, showing only anomalies that are currently unlocked
/// (<see cref="AnomalyUnlockManager.IsAnomalyUnlocked"/>).
///
/// Book order:
/// <list type="number">
///   <item>Rule pages (How to play, Pass, Quarantine, Kill, …).</item>
///   <item>Scoring &amp; disposition page (Conversion Risk Protocol table).</item>
///   <item>Checkpoint Integrity pages (HUD panel + payout effect, then illustrated chores).</item>
///   <item>Survival pages (HUD health bar, then HUD Geiger counter).</item>
///   <item>One section per category with ≥1 unlocked anomaly: intro page, then one page per anomaly.</item>
/// </list>
///
/// Every section intro is placed on a sheet's BACK face, so jumping to a section shows the intro on
/// the left and its first anomaly on the right. Blank "field notes" faces pad the gaps.
///
/// Rebuilding happens in <see cref="OnEnable"/> (book opening) — never while the player is reading.
/// The reading position is kept between opens unless the set of unlocked anomalies changed.
/// </summary>
[DefaultExecutionOrder(-10)]
public class GuidebookBuilder : MonoBehaviour
{
    [Header("Content")]
    [SerializeField] private GuidebookDatabase _database;

    [Header("Wiring")]
    [SerializeField] private GuidebookPageController _pageController;
    [SerializeField] private GuidebookSheet          _sheetPrefab;
    [Tooltip("Parent for spawned sheets. Must be the transform the page controller's stack origins are relative to.")]
    [SerializeField] private Transform               _sheetParent;
    [SerializeField] private GuidebookSectionTab     _tabPrefab;

    [Header("Tab Placement (sheet-local)")]
    [SerializeField] private Vector3 _tabFirstSlot  = new Vector3(-0.29f, 0f, -1.247f);
    [SerializeField] private Vector3 _tabSlotStep   = new Vector3(-0.318f, 0f, 0f);
    [SerializeField] private int     _tabSlotCount  = 6;
    [SerializeField] private Vector3 _tabLocalEuler = new Vector3(0f, 180f, 0f);

    [Header("Styling")]
    [Tooltip("How far tab colours are darkened to produce readable header/badge text colours.")]
    [Range(0f, 1f)]
    [SerializeField] private float _accentDarken = 0.55f;
    [SerializeField] private Color _rulesAccent = new Color(0.45f, 0.08f, 0.06f, 1f);

    /// <summary>
    /// Anomaly type names whose page the local player has already had open in front of them.
    /// Session-scoped and shared by all guidebook instances. Drives the "NEW" stamp.
    /// </summary>
    private static readonly HashSet<string> s_seenAnomalies = new HashSet<string>();

    private readonly List<GuidebookSheet>      _sheetPool = new List<GuidebookSheet>();
    private readonly List<GuidebookSheet>      _activeSheets = new List<GuidebookSheet>();
    private readonly List<GuidebookSectionTab> _tabPool = new List<GuidebookSectionTab>();
    private readonly List<string>              _shownAnomalies = new List<string>();
    private readonly List<GuidebookFaceContent> _faces = new List<GuidebookFaceContent>();
    private readonly List<(int faceIndex, string label, Color color)> _tabs =
        new List<(int, string, Color)>();
    private readonly List<List<int>> _imageGroups = new List<List<int>>();

    private string _builtSignature;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_pageController == null) _pageController = GetComponent<GuidebookPageController>();
        if (_sheetParent == null)    _sheetParent    = transform;
    }

    private void OnEnable()
    {
        if (_database == null || _pageController == null || _sheetPrefab == null)
        {
            Debug.LogWarning($"[GuidebookBuilder] '{name}' is missing its database, page controller or sheet prefab.", this);
            return;
        }

        string signature = ComputeUnlockSignature();
        bool unlocksChanged = signature != _builtSignature;
        _builtSignature = signature;

        int leftCount = unlocksChanged ? 0 : _pageController.LeftCount;
        Build();
        _pageController.SetSheets(_activeSheets, leftCount);
    }

    private void OnDisable()
    {
        // Only the local player's first-person copy marks pages as read.
        if (_pageController != null && _pageController.InputEnabled)
            foreach (string typeName in _shownAnomalies)
                s_seenAnomalies.Add(typeName);
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    private void Build()
    {
        _faces.Clear();
        _tabs.Clear();
        _shownAnomalies.Clear();
        _imageGroups.Clear();

        AddRules();
        AddScoring();
        AddIntegrity();
        AddSurvival();
        AddSections();

        if (_faces.Count % 2 == 1) _faces.Add(NotesFace());

        for (int i = 0; i < _faces.Count; i++)
        {
            GuidebookFaceContent f = _faces[i];
            if (!f.IsBlank)
            {
                f.PageNumber = i + 1;
                f.Body       = FormatBody(f.Body);
            }
            _faces[i] = f;
        }

        EqualizeImageHeights();
        LayoutSheets();
        LayoutTabs();
    }

    /// <summary>
    /// Adds authored pages (rules / integrity). Pages with a single illustration are recorded as one
    /// group so <see cref="EqualizeImageHeights"/> can give them all the same image size.
    /// </summary>
    private void AddAuthoredPages(IReadOnlyList<GuidebookDatabase.RulePage> pages, Color accent,
                                  System.Func<string, string> bodyFilter = null)
    {
        var group = new List<int>();
        foreach (GuidebookDatabase.RulePage page in pages)
        {
            bool hasRow   = page.ImageRow != null && page.ImageRow.Length > 0;
            bool hasPanel = page.ShowIntegrityPanel && _database.IntegrityPanel != null;
            bool hasHud   = !hasPanel && page.HudArt != GuidebookHudArt.None;
            if (page.Image != null && !hasRow && !hasPanel && !hasHud) group.Add(_faces.Count);

            _faces.Add(new GuidebookFaceContent
            {
                Header      = page.Header,
                Title       = page.Title,
                Body        = bodyFilter != null ? bodyFilter(page.Body) : page.Body,
                Image       = page.Image,
                ImageRow    = hasRow ? page.ImageRow : null,
                IntegrityPanel = hasPanel ? _database.IntegrityPanel : null,
                HudArt      = hasHud ? page.HudArt : GuidebookHudArt.None,
                HudExample  = hasHud ? _database.SurvivalExample : null,
                IconList    = page.IconList != null && page.IconList.Length > 0 ? page.IconList : null,
                AccentColor = accent,
            });
        }
        if (group.Count > 1) _imageGroups.Add(group);
    }

    /// <summary>
    /// Each group's pages use the smallest automatic image height among them, so the illustrations
    /// are the same size regardless of how much text each page has. Must run after <see cref="FormatBody"/>.
    /// </summary>
    private void EqualizeImageHeights()
    {
        if (_imageGroups.Count == 0) return;
        EnsureSheetPool(1);
        GuidebookFaceView probe = _sheetPool[0].Front;
        if (probe == null) return;

        foreach (List<int> group in _imageGroups)
        {
            float height = float.MaxValue;
            foreach (int index in group)
                height = Mathf.Min(height, probe.MeasureImageHeight(_faces[index]));

            foreach (int index in group)
            {
                GuidebookFaceContent f = _faces[index];
                f.ImageHeight  = height;
                _faces[index]  = f;
            }
        }
    }

    private void AddIntegrity()
    {
        IReadOnlyList<GuidebookDatabase.RulePage> pages = _database.IntegrityPages;
        if (pages.Count == 0) return;

        // Open the section as a spread, like the anomaly sections.
        if (_faces.Count % 2 == 0) _faces.Add(NotesFace());
        _tabs.Add((_faces.Count, _database.IntegrityTabLabel, _database.IntegrityTabColor));
        Color  accent       = Accent(_database.IntegrityTabColor);
        string maxDeduction = MaxIntegrityDeductionPercent().ToString();

        AddAuthoredPages(pages, accent,
            body => body?.Replace(GuidebookDatabase.IntegrityMaxDeductionToken, maxDeduction));
    }

    /// <summary>Survival section (health bar page, Geiger counter page), opened as a spread with its own tab.</summary>
    private void AddSurvival()
    {
        IReadOnlyList<GuidebookDatabase.RulePage> pages = _database.SurvivalPages;
        if (pages == null || pages.Count == 0) return;

        if (_faces.Count % 2 == 0) _faces.Add(NotesFace());
        _tabs.Add((_faces.Count, _database.SurvivalTabLabel, _database.SurvivalTabColor));
        AddAuthoredPages(pages, Accent(_database.SurvivalTabColor));
    }

    /// <summary>Largest share of a payout the integrity multiplier can take away, in whole percent.</summary>
    private static int MaxIntegrityDeductionPercent()
    {
        CheckpointIntegrityService service = CheckpointIntegrityService.Instance;
        if (service == null || service.MaxScore <= 0f) return 50;
        return Mathf.RoundToInt((1f - service.MinScore / service.MaxScore) * 100f);
    }

    private void AddRules()
    {
        IReadOnlyList<GuidebookDatabase.RulePage> rules = _database.RulePages;
        if (rules.Count == 0) return;

        // Like every other section, open Rules as a spread when something precedes it.
        if (_faces.Count > 0 && _faces.Count % 2 == 0) _faces.Add(NotesFace());
        _tabs.Add((_faces.Count, _database.RulesTabLabel, _database.RulesTabColor));

        AddAuthoredPages(rules, _rulesAccent);
    }

    private void AddScoring()
    {
        IReadOnlyList<GuidebookDatabase.DispositionBand> bands = _database.DispositionBands;
        if (bands.Count == 0) return;

        // Disposition chart: SCORE | STATUS | ACTION.
        var disposition = new GuidebookTable
        {
            ColumnWidths = new[] { 0.9f, 1.25f, 1.85f },
            Alignments   = new[] { TMPro.TextAlignmentOptions.Center,
                                   TMPro.TextAlignmentOptions.MidlineLeft,
                                   TMPro.TextAlignmentOptions.MidlineLeft },
        };
        disposition.Rows.Add(new[] { "SCORE", "STATUS", "ACTION" });
        foreach (GuidebookDatabase.DispositionBand band in bands)
        {
            string range = band.MinScore == band.MaxScore ? band.MinScore.ToString() : $"{band.MinScore}-{band.MaxScore}";
            disposition.Rows.Add(new[] { range, Upper(band.Status), Upper(band.Action) });
        }

        _faces.Add(new GuidebookFaceContent
        {
            Header      = _database.ScoringHeader,
            Title       = _database.ScoringTitle,
            Body        = _database.ScoringIntro,
            Tables      = new[] { disposition },
            Callout     = string.IsNullOrEmpty(_database.DispositionWarning)
                              ? null
                              : "<b>WARNING:</b> " + _database.DispositionWarning,
            AccentColor = _rulesAccent,
        });
    }

    private static string Upper(string s) => string.IsNullOrEmpty(s) ? s : s.ToUpperInvariant();

    private void AddSections()
    {
        int sectionNumber = 0;
        var sectionEntries = new List<GuidebookDatabase.AnomalyEntry>();

        foreach (GuidebookDatabase.CategoryStyle style in _database.Categories)
        {
            sectionEntries.Clear();
            foreach (GuidebookDatabase.AnomalyEntry entry in _database.Entries)
                if (GuidebookDatabase.GetCategory(entry) == style.Category && IsUnlocked(entry.AnomalyTypeName))
                    sectionEntries.Add(entry);

            if (sectionEntries.Count == 0) continue;

            // Intro must sit on a back face (odd index) so the section opens as a spread.
            if (_faces.Count % 2 == 0) _faces.Add(NotesFace());

            sectionNumber++;
            int   cost   = AnomalyController.GetAnomalyPointCost(style.Category);
            Color accent = Accent(style.TabColor);
            string costText = cost == 1 ? "+1 POINT" : $"+{cost} POINTS";

            int introIndex = _faces.Count;
            _tabs.Add((introIndex, style.TabLabel, style.TabColor));
            _faces.Add(default); // filled below once anomaly page numbers are known

            var contents = new StringBuilder();
            foreach (GuidebookDatabase.AnomalyEntry entry in sectionEntries)
            {
                contents.Append(ListMarker).Append(entry.DisplayName).Append(PosRight).Append(_faces.Count + 1).Append("</indent>\n");

                string body = entry.Description;
                if (!string.IsNullOrEmpty(entry.HowToDetect))
                    body += "\n\n<b>HOW TO DETECT</b>\n" + entry.HowToDetect;

                _faces.Add(new GuidebookFaceContent
                {
                    Header      = style.DisplayName.ToUpperInvariant(),
                    Title       = entry.DisplayName.ToUpperInvariant(),
                    Image       = entry.Illustration,
                    Badge       = $"{costText} TO RISK SCORE",
                    Body        = body,
                    AccentColor = accent,
                    IsNew       = !s_seenAnomalies.Contains(entry.AnomalyTypeName),
                });
                _shownAnomalies.Add(entry.AnomalyTypeName);
            }

            var intro = new StringBuilder();
            if (!string.IsNullOrEmpty(style.Intro)) intro.Append(style.Intro).Append("\n\n");
            if (!string.IsNullOrEmpty(style.Tools)) intro.Append("<b>TOOLS</b>\n").Append(style.Tools).Append("\n\n");
            intro.Append("<b>IN THIS SECTION</b>").Append(PosRight).Append("<b>PAGE</b>\n").Append(contents);

            _faces[introIndex] = new GuidebookFaceContent
            {
                Header      = $"SECTION {sectionNumber}",
                Title       = style.DisplayName.ToUpperInvariant(),
                Badge       = $"{costText} PER CONFIRMED ANOMALY",
                Body        = intro.ToString(),
                AccentColor = accent,
            };
        }
    }

    private GuidebookFaceContent NotesFace() => new GuidebookFaceContent
    {
        Header      = _database.NotesHeader,
        AccentColor = _rulesAccent,
        IsBlank     = true,
    };

    // ── Layout ────────────────────────────────────────────────────────────────

    private void EnsureSheetPool(int count)
    {
        while (_sheetPool.Count < count)
        {
            GuidebookSheet sheet = Instantiate(_sheetPrefab, _sheetParent);
            sheet.name = $"Sheet {_sheetPool.Count:00}";
            _sheetPool.Add(sheet);
        }
    }

    private void LayoutSheets()
    {
        int sheetCount = _faces.Count / 2;
        EnsureSheetPool(sheetCount);

        _activeSheets.Clear();
        for (int i = 0; i < _sheetPool.Count; i++)
        {
            GuidebookSheet sheet = _sheetPool[i];
            bool used = i < sheetCount;
            sheet.gameObject.SetActive(used);
            if (!used) continue;

            sheet.Front.Apply(_faces[i * 2]);
            sheet.Back.Apply(_faces[i * 2 + 1]);
            _activeSheets.Add(sheet);
        }
    }

    private void LayoutTabs()
    {
        if (_tabPrefab == null) return;

        while (_tabPool.Count < _tabs.Count)
            _tabPool.Add(Instantiate(_tabPrefab, _sheetParent));

        int slots = Mathf.Max(1, _tabSlotCount);
        // More tabs than slots: tighten the spacing so they all fit in the authored span
        // instead of wrapping onto (and overlapping) the first slots.
        Vector3 step = _tabSlotStep;
        if (_tabs.Count > slots && slots > 1)
        {
            step  = _tabSlotStep * (slots - 1) / (_tabs.Count - 1);
            slots = _tabs.Count;
        }

        for (int i = 0; i < _tabPool.Count; i++)
        {
            GuidebookSectionTab tab = _tabPool[i];
            bool used = i < _tabs.Count;
            tab.gameObject.SetActive(used);
            if (!used) continue;

            (int faceIndex, string label, Color color) = _tabs[i];
            int sheetIndex = faceIndex / 2;
            int target     = faceIndex % 2 == 1 ? sheetIndex + 1 : sheetIndex;

            Transform t = tab.transform;
            t.SetParent(_activeSheets[sheetIndex].TabAnchor, false);
            t.localPosition = _tabFirstSlot + step * (i % slots);
            t.localRotation = Quaternion.Euler(_tabLocalEuler);
            tab.name = $"Tab - {label}";
            tab.Bind(_pageController, target, label, color);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Rich-text column stop for right-aligned page numbers in section contents.
    private const string PosRight   = "<pos=86%>";
    // Dash bullet with a hanging indent; the line must close with </indent>.
    private const string ListMarker = "-<indent=1.1em>";

    private static readonly Regex s_numberedItem = new Regex(@"^(\d+\.)\s+(.*)$");

    // Gap between paragraphs / sections (a blank line in the source text): a full empty line.
    private const string ParagraphBreak = "\n\n";
    // Gap between consecutive list items: a short empty line, so items read as separate entries.
    private const string ListItemBreak  = "\n<size=50%>\n</size>";

    /// <summary>
    /// Final typography pass for every page body:
    /// "1. text" and "- text" lines get a hanging indent so wrapped lines align with the item
    /// text instead of running back under the number. Blank lines become full paragraph gaps and
    /// consecutive list items get a smaller gap between them.
    /// </summary>
    private static string FormatBody(string body)
    {
        if (string.IsNullOrEmpty(body)) return body;

        string[] lines = body.Replace("\r", "").Trim('\n').Split('\n');
        var isListItem = new bool[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimStart();
            Match m = s_numberedItem.Match(line);
            if (m.Success)
            {
                lines[i] = $"{m.Groups[1].Value}<indent=1.6em>{m.Groups[2].Value}</indent>";
                isListItem[i] = true;
            }
            else if (line.StartsWith("- "))
            {
                lines[i] = ListMarker + line.Substring(2) + "</indent>";
                isListItem[i] = true;
            }
            else
            {
                lines[i] = line;
                isListItem[i] = line.StartsWith(ListMarker);
            }
        }

        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            bool blank = lines[i].Length == 0;
            if (blank)
            {
                // Collapse runs of blank lines into one paragraph gap.
                while (i + 1 < lines.Length && lines[i + 1].Length == 0) i++;
                if (i + 1 < lines.Length) sb.Append(ParagraphBreak).Append(lines[++i]);
                continue;
            }

            if (sb.Length > 0)
                sb.Append(isListItem[i] && i > 0 && isListItem[i - 1] ? ListItemBreak : "\n");
            sb.Append(lines[i]);
        }
        return sb.ToString();
    }
    private Color Accent(Color tabColor)
    {
        Color c = Color.Lerp(tabColor, Color.black, _accentDarken);
        c.a = 1f;
        return c;
    }

    private static bool IsUnlocked(string typeName) =>
        !string.IsNullOrEmpty(typeName)
        && (AnomalyUnlockManager.Instance == null || AnomalyUnlockManager.Instance.IsAnomalyUnlocked(typeName));

    private string ComputeUnlockSignature()
    {
        var sb = new StringBuilder();
        foreach (GuidebookDatabase.AnomalyEntry entry in _database.Entries)
            if (entry != null && IsUnlocked(entry.AnomalyTypeName))
                sb.Append(entry.AnomalyTypeName).Append('|');
        return sb.ToString();
    }
}
