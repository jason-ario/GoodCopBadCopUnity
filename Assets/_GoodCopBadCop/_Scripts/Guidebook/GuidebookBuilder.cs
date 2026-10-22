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
///   <item>Checkpoint Integrity page (HUD panel + payout effect).</item>
///   <item>Survival pages, if any are authored (none by default).</item>
///   <item>One section per category with ≥1 unlocked anomaly: one page per anomaly (no intro page).</item>
/// </list>
///
/// Every section's first page is placed on a sheet's BACK face, so jumping to a section opens it as
/// a spread. Blank "field notes" faces pad the gaps.
///
/// Rebuilding happens in <see cref="OnEnable"/> (book opening) — never while the player is reading.
/// The reading position is kept between opens unless the set of unlocked anomalies changed.
///
/// On the local player's first-person copy, tabs of sections with unseen entries show a bobbing
/// "!". Once the open spread settles inside a section, its entries are marked seen
/// (<see cref="GuidebookSeenState"/>, saved per slot) and the tab's "!" goes away.
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

    private readonly List<GuidebookSheet>      _sheetPool = new List<GuidebookSheet>();
    private readonly List<GuidebookSheet>      _activeSheets = new List<GuidebookSheet>();
    private readonly List<GuidebookSectionTab> _tabPool = new List<GuidebookSectionTab>();
    private readonly List<GuidebookFaceContent> _faces = new List<GuidebookFaceContent>();
    // Anomalies is null for sections without anomaly entries (Rules, Integrity, Survival).
    private readonly List<(int faceIndex, string label, Color color, List<string> anomalies)> _tabs =
        new List<(int, string, Color, List<string>)>();
    private readonly List<List<int>> _imageGroups = new List<List<int>>();

    private string _builtSignature;

    // Seen tracking — only on the local player's first-person copy.
    private bool _tracksSeen;
    private int  _lastCheckedLeftCount = -1;

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

        // GuidebookController enables input before activating the local copy.
        _tracksSeen = _pageController.InputEnabled;
        _lastCheckedLeftCount = -1;

        int leftCount = unlocksChanged ? 0 : _pageController.LeftCount;
        Build();
        _pageController.SetSheets(_activeSheets, leftCount);
        RefreshTabBadges();
    }

    private void Update()
    {
        if (!_tracksSeen || _pageController == null || _pageController.IsBusy) return;

        int leftCount = _pageController.LeftCount;
        if (leftCount == _lastCheckedLeftCount) return;
        _lastCheckedLeftCount = leftCount;

        MarkSectionSeen(leftCount);
    }

    // ── Seen tracking ─────────────────────────────────────────────────────────

    /// <summary>
    /// The open spread has settled on <paramref name="leftCount"/>: every entry of the section it
    /// belongs to counts as seen. Pages keep their NEW stamp until the book is next opened.
    /// </summary>
    private void MarkSectionSeen(int leftCount)
    {
        int section = -1;
        for (int i = 0; i < _tabs.Count; i++)
            if (TabTarget(_tabs[i].faceIndex) <= leftCount) section = i;

        if (section < 0) return;
        List<string> anomalies = _tabs[section].anomalies;
        if (anomalies == null || !SectionHasNew(anomalies)) return;

        GuidebookSeenState.MarkSeen(anomalies);
        RefreshTabBadges();
    }

    private static bool SectionHasNew(List<string> anomalies)
    {
        if (anomalies == null) return false;
        foreach (string typeName in anomalies)
            if (!GuidebookSeenState.IsSeen(typeName)) return true;
        return false;
    }

    private void RefreshTabBadges()
    {
        for (int i = 0; i < _tabPool.Count; i++)
        {
            bool isNew = _tracksSeen && i < _tabs.Count && SectionHasNew(_tabs[i].anomalies);
            _tabPool[i].SetNew(isNew);
        }
    }

    /// <summary>Left-stack count that opens the section whose first face is <paramref name="faceIndex"/>.</summary>
    private static int TabTarget(int faceIndex) =>
        faceIndex % 2 == 1 ? faceIndex / 2 + 1 : faceIndex / 2;

    // ── Build ─────────────────────────────────────────────────────────────────

    private void Build()
    {
        _faces.Clear();
        _tabs.Clear();
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
                Header      = L(page.Header),
                Title       = L(page.Title),
                Body        = bodyFilter != null ? bodyFilter(L(page.Body)) : L(page.Body),
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
        _tabs.Add((_faces.Count, L(_database.IntegrityTabLabel), _database.IntegrityTabColor, null));
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
        _tabs.Add((_faces.Count, L(_database.SurvivalTabLabel), _database.SurvivalTabColor, null));
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
        _tabs.Add((_faces.Count, L(_database.RulesTabLabel), _database.RulesTabColor, null));

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
        disposition.Rows.Add(new[] { Loc.T("SCORE"), Loc.T("STATUS"), Loc.T("ACTION") });
        foreach (GuidebookDatabase.DispositionBand band in bands)
        {
            string range = band.MinScore == band.MaxScore ? band.MinScore.ToString() : $"{band.MinScore}-{band.MaxScore}";
            disposition.Rows.Add(new[] { range, Upper(L(band.Status)), Upper(L(band.Action)) });
        }

        _faces.Add(new GuidebookFaceContent
        {
            Header      = L(_database.ScoringHeader),
            Title       = L(_database.ScoringTitle),
            Body        = L(_database.ScoringIntro),
            Tables      = new[] { disposition },
            Callout     = string.IsNullOrEmpty(_database.DispositionWarning)
                              ? null
                              : "<b>" + Loc.T("WARNING:") + "</b> " + L(_database.DispositionWarning),
            AccentColor = _rulesAccent,
        });
    }

    private static string Upper(string s) => string.IsNullOrEmpty(s) ? s : s.ToUpperInvariant();

    /// <summary>Database text is authored in English and looked up by source (GuidebookDatabase rows in Guidebook.csv).</summary>
    private static string L(string english) => Loc.TranslateSource(english);

    private void AddSections()
    {
        var sectionEntries = new List<GuidebookDatabase.AnomalyEntry>();

        foreach (GuidebookDatabase.CategoryStyle style in _database.Categories)
        {
            sectionEntries.Clear();
            foreach (GuidebookDatabase.AnomalyEntry entry in _database.Entries)
                if (GuidebookDatabase.GetCategory(entry) == style.Category && IsUnlocked(entry.AnomalyTypeName))
                    sectionEntries.Add(entry);

            if (sectionEntries.Count == 0) continue;

            // First anomaly page sits on a back face (odd index) so the section opens as a spread.
            if (_faces.Count % 2 == 0) _faces.Add(NotesFace());

            int   cost   = AnomalyController.GetAnomalyPointCost(style.Category);
            Color accent = Accent(style.TabColor);
            string costText = cost == 1 ? Loc.T("+1 POINT") : Loc.T("+{0} POINTS", cost);

            var sectionAnomalies = new List<string>(sectionEntries.Count);
            _tabs.Add((_faces.Count, L(style.TabLabel), style.TabColor, sectionAnomalies));

            foreach (GuidebookDatabase.AnomalyEntry entry in sectionEntries)
            {
                string body = L(entry.Description);
                if (!string.IsNullOrEmpty(entry.HowToDetect))
                    body += "\n\n<b>" + Loc.T("HOW TO DETECT") + "</b>\n" + L(entry.HowToDetect);

                _faces.Add(new GuidebookFaceContent
                {
                    Header      = L(style.DisplayName).ToUpperInvariant(),
                    Title       = L(entry.DisplayName).ToUpperInvariant(),
                    Image       = entry.Illustration,
                    Badge       = Loc.T("{0} TO RISK SCORE", costText),
                    Body        = body,
                    AccentColor = accent,
                    IsNew       = !GuidebookSeenState.IsSeen(entry.AnomalyTypeName),
                });
                sectionAnomalies.Add(entry.AnomalyTypeName);
            }
        }
    }

    private GuidebookFaceContent NotesFace() => new GuidebookFaceContent
    {
        Header      = L(_database.NotesHeader),
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

            (int faceIndex, string label, Color color, _) = _tabs[i];
            int sheetIndex = faceIndex / 2;
            int target     = TabTarget(faceIndex);

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

    private static bool IsUnlocked(string typeName) => GuidebookSeenState.IsUnlocked(typeName);

    private string ComputeUnlockSignature()
    {
        var sb = new StringBuilder();
        foreach (GuidebookDatabase.AnomalyEntry entry in _database.Entries)
            if (entry != null && IsUnlocked(entry.AnomalyTypeName))
                sb.Append(entry.AnomalyTypeName).Append('|');
        return sb.ToString();
    }
}
