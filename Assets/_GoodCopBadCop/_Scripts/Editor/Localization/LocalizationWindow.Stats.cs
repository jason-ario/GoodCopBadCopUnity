using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public partial class LocalizationWindow
{
    private static readonly Regex WordRx = new Regex(@"[\p{L}\p{N}][\p{L}\p{N}'\u2019\-]*", RegexOptions.Compiled);

    // Word counts keyed by source text. Identical lines are common in dialogue, and the cache survives refreshes.
    private static readonly Dictionary<string, int> WordCountCache = new Dictionary<string, int>();
    private const int WordCountCacheLimit = 200000;

    private class StatRow
    {
        public string name;
        public int strings, words, chars, uniqueWords;
        public int delta; // words that are new or changed since the last freeze
        public readonly HashSet<string> seen = new HashSet<string>();

        public void Add(string source, int w, int c, bool frozen)
        {
            strings++;
            words += w;
            chars += c;
            if (seen.Add(source)) uniqueWords += w;
            if (!frozen) delta += w;
        }
    }

    private class LangProgress
    {
        public string lang;
        public int strings, words;
    }

    /// <summary>Everything the Stats tab shows, computed in one pass and reused until the data changes.</summary>
    private class StatsSnapshot
    {
        public StatRow all, dialogue;
        public List<StatRow> byGroup, bySheet;
        public List<LangProgress> langs;
        public List<(string key, int words)> longest;
        public int entryCount;
        public List<ScanItem> scanRef;
        public int pending, pendingWords;
        public double millis;
    }

    private StatsSnapshot _stats;
    private Vector2 _statsScroll;
    private float _ratePerWord = 0.08f;
    private string _statsCurrency = "USD";
    private bool _showAllSheets;
    private const int SheetRowsCollapsed = 25;

    /// <summary>Translator-style word count: words in localizable text only. Tags, placeholders, escapes and bare numerals are excluded.</summary>
    public static int CountWords(string source)
    {
        if (string.IsNullOrEmpty(source)) return 0;
        if (WordCountCache.TryGetValue(source, out int cached)) return cached;
        int n = 0;
        foreach (var seg in PseudoTokenizer.Tokenize(source))
        {
            if (seg.kind == PseudoSegmentKind.Text) n += WordRx.Matches(seg.text).Count;
            else if (seg.kind == PseudoSegmentKind.Input) n++;
        }
        if (WordCountCache.Count > WordCountCacheLimit) WordCountCache.Clear();
        WordCountCache[source] = n;
        return n;
    }

    private bool StatsStale()
    {
        return _stats == null || _stats.entryCount != _table.entries.Count || !ReferenceEquals(_stats.scanRef, _scan);
    }

    private StatsSnapshot BuildStats()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var s = new StatsSnapshot
        {
            all = new StatRow { name = "Total" },
            dialogue = new StatRow { name = "Dialogue (all three)" },
            entryCount = _table.entries.Count,
            scanRef = _scan
        };
        var groups = new Dictionary<string, StatRow>();
        var sheets = new Dictionary<string, StatRow>();
        var langCodes = LocalizationSheetIO.StoredLanguages(_table).ToList();
        var langs = langCodes.Select(l => new LangProgress { lang = l }).ToList();
        var lengths = new List<(string key, int words)>(_table.entries.Count);

        foreach (var e in _table.entries)
        {
            string src = e.source ?? "";
            int w = CountWords(src);
            int c = PseudoTokenizer.VisibleLength(src);
            bool frozen = LocalizationSheetIO.Status(e) == "frozen";
            string group = LocBindings.GroupOf(e.key);

            s.all.Add(src, w, c, frozen);
            if (LocGroups.IsDialogue(group)) s.dialogue.Add(src, w, c, frozen);

            if (!groups.TryGetValue(group, out var gr)) groups[group] = gr = new StatRow { name = group };
            gr.Add(src, w, c, frozen);

            string sheet = e.sheet ?? "";
            if (!sheets.TryGetValue(sheet, out var sr)) sheets[sheet] = sr = new StatRow { name = sheet };
            sr.Add(src, w, c, frozen);

            for (int i = 0; i < langCodes.Count; i++)
                if (!string.IsNullOrEmpty(e.GetValue(langCodes[i]))) { langs[i].strings++; langs[i].words += w; }

            lengths.Add((e.key, w));
        }

        s.byGroup = groups.Values.OrderBy(r => r.name).ToList();
        s.bySheet = sheets.Values.OrderByDescending(r => r.words).ToList();
        s.langs = langs;
        s.longest = lengths.OrderByDescending(x => x.words).Take(10).ToList();

        if (_scan != null)
            foreach (var i in _scan)
                if (i.state == ScanState.Unlocalized) { s.pending++; s.pendingWords += CountWords(i.text); }

        // The HashSets are only needed while counting.
        s.all.seen.Clear(); s.dialogue.seen.Clear();
        foreach (var r in s.byGroup) r.seen.Clear();
        foreach (var r in s.bySheet) r.seen.Clear();

        s.millis = sw.Elapsed.TotalMilliseconds;
        return s;
    }

    private void DrawStatsTab()
    {
        // Only rebuild during Layout so Layout and Repaint see the same data.
        if (Event.current.type == EventType.Layout && StatsStale()) _stats = BuildStats();
        if (_stats == null) return;
        var s = _stats;
        var all = s.all;

        using (new EditorGUILayout.HorizontalScope())
        {
            LocUI.Muted_($"Snapshot of {s.entryCount:N0} strings, computed in {s.millis:0} ms. Refreshes when you open this tab, reload, or the string count changes.");
            GUILayout.FlexibleSpace();
            if (LocUI.Button("Refresh")) _stats = null;
        }

        _statsScroll = EditorGUILayout.BeginScrollView(_statsScroll);

        LocUI.Section("Game text", "Strings that are already in the sheets.");
        using (new EditorGUILayout.HorizontalScope())
        {
            LocUI.StatTile("words", all.words.ToString("N0"), LocUI.Accent, 150);
            LocUI.StatTile("strings", all.strings.ToString("N0"), LocUI.Violet, 130);
            LocUI.StatTile("characters", all.chars.ToString("N0"), LocUI.Neutral, 150);
            LocUI.StatTile("words without identical strings", all.uniqueWords.ToString("N0"), LocUI.Ok, 200);
            LocUI.StatTile($"new / changed words ({(all.words > 0 ? 100f * all.delta / all.words : 0f):0}%)", all.delta.ToString("N0"), LocUI.Warn, 200);
            GUILayout.FlexibleSpace();
        }

        if (s.scanRef != null)
            EditorGUILayout.HelpBox($"The last Scan found {s.pending:N0} strings ({s.pendingWords:N0} words) that are not in the sheets yet. They are not counted above until you Apply them.", MessageType.Info);
        else
            LocUI.Hint("Only strings already in the sheets are counted. Run Scan and Apply to bring in the rest of the game's text.");

        // ---- Dialogue vs other, by source group
        EditorGUILayout.Space();
        LocUI.Section("By source");
        DrawStatHeader();
        DrawStatRow(s.dialogue, true);
        foreach (var r in s.byGroup) DrawStatRow(r, false);

        // ---- By sheet
        EditorGUILayout.Space();
        LocUI.Section("By sheet (one per suspect / conversation, plus thematic sheets)");
        DrawStatHeader();
        int shown = _showAllSheets ? s.bySheet.Count : Mathf.Min(SheetRowsCollapsed, s.bySheet.Count);
        for (int i = 0; i < shown; i++) DrawStatRow(s.bySheet[i], false);
        if (s.bySheet.Count > SheetRowsCollapsed)
        {
            string label = _showAllSheets ? "Show top " + SheetRowsCollapsed + " only" : "Show all " + s.bySheet.Count + " sheets";
            if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Width(180))) _showAllSheets = !_showAllSheets;
        }

        // ---- Translation progress
        EditorGUILayout.Space();
        LocUI.Section("Translation progress");
        if (s.langs.Count == 0) EditorGUILayout.LabelField("No language columns yet (add one in the Strings tab).", EditorStyles.miniLabel);
        foreach (var p in s.langs)
        {
            float pct = all.words > 0 ? (float)p.words / all.words : 0f;
            var r = EditorGUILayout.GetControlRect();
            EditorGUI.ProgressBar(r, pct, $"{p.lang}: {p.strings:N0}/{all.strings:N0} strings, {p.words:N0}/{all.words:N0} words ({pct * 100f:0}%)");
        }

        // ---- Cost estimate
        EditorGUILayout.Space();
        LocUI.Section("Cost estimate (per language)");
        using (new EditorGUILayout.HorizontalScope())
        {
            _ratePerWord = EditorGUILayout.FloatField("Rate per word", _ratePerWord);
            _statsCurrency = EditorGUILayout.TextField(_statsCurrency, GUILayout.Width(60));
        }
        EditorGUILayout.LabelField($"Full text: {all.words * _ratePerWord:N0} {_statsCurrency} | without identical strings: {all.uniqueWords * _ratePerWord:N0} {_statsCurrency} | delta only: {all.delta * _ratePerWord:N0} {_statsCurrency}");

        // ---- Longest strings
        EditorGUILayout.Space();
        LocUI.Section("Longest strings");
        foreach (var l in s.longest)
            EditorGUILayout.LabelField(l.words + " words", l.key, EditorStyles.miniLabel);

        EditorGUILayout.Space();
        if (GUILayout.Button("Export stats (markdown)"))
        {
            var sb = new StringBuilder("# Localization stats\n\n");
            sb.AppendLine($"Total: {all.words} words, {all.strings} strings, {all.chars} characters ({all.uniqueWords} words without identical strings). Delta since freeze: {all.delta} words.\n");
            sb.AppendLine($"Dialogue: {s.dialogue.words} words in {s.dialogue.strings} strings.\n");
            sb.AppendLine("| Sheet | Strings | Words | Characters | Delta words |").AppendLine("|---|---|---|---|---|");
            foreach (var r in s.bySheet) sb.AppendLine($"| {r.name} | {r.strings} | {r.words} | {r.chars} | {r.delta} |");
            LocalizationPaths.EnsureFolders();
            string rel = LocalizationPaths.Findings + "/stats.md";
            File.WriteAllText(LocalizationPaths.Abs(rel), sb.ToString(), new UTF8Encoding(false));
            AssetDatabase.Refresh();
            _status = "Saved " + rel;
        }

        EditorGUILayout.EndScrollView();
    }

    private static void DrawStatHeader()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("Name", EditorStyles.miniBoldLabel, GUILayout.MinWidth(200));
            GUILayout.Label("Strings", EditorStyles.miniBoldLabel, GUILayout.Width(70));
            GUILayout.Label("Words", EditorStyles.miniBoldLabel, GUILayout.Width(70));
            GUILayout.Label("Unique words", EditorStyles.miniBoldLabel, GUILayout.Width(90));
            GUILayout.Label("Characters", EditorStyles.miniBoldLabel, GUILayout.Width(80));
            GUILayout.Label("Delta words", EditorStyles.miniBoldLabel, GUILayout.Width(80));
        }
    }

    private static void DrawStatRow(StatRow r, bool bold)
    {
        var style = bold ? EditorStyles.boldLabel : EditorStyles.label;
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(r.name, style, GUILayout.MinWidth(200));
            GUILayout.Label(r.strings.ToString("N0"), style, GUILayout.Width(70));
            GUILayout.Label(r.words.ToString("N0"), style, GUILayout.Width(70));
            GUILayout.Label(r.uniqueWords.ToString("N0"), style, GUILayout.Width(90));
            GUILayout.Label(r.chars.ToString("N0"), style, GUILayout.Width(80));
            GUILayout.Label(r.delta.ToString("N0"), style, GUILayout.Width(80));
        }
    }
}
