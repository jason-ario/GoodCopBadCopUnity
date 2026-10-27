using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;

public partial class LocalizationWindow
{
    // ---------------------------------------------------------------- data

    private class LenResult
    {
        public string key, category, note;
        public int sourceLen, maxLen;
        public float Ratio { get { return sourceLen > 0 ? maxLen / (float)sourceLen : 0f; } }
    }

    private class InvItem
    {
        public string kind, token, classification;
        public int count;
        public List<string> keys = new List<string>();
    }

    private class ProbeRow
    {
        public char c;
        public bool csvBreaks, formatBreaks, noGlyph, parsedAsMarkup;
        public bool Any { get { return csvBreaks || formatBreaks || noGlyph || parsedAsMarkup; } }
    }

    private class ChecklistRow
    {
        public string section, text, note = "";
        public bool done;
    }

    private int _findTab;
    private static readonly string[] FindTabNames = { "Max length", "Placeholders", "Restricted chars", "Player address", "Numbers / dates", "Pseudo self-check", "Source lint", "Checklist", "Summary" };
    private Vector2 _findScroll;

    private List<LenResult> _lenResults = new List<LenResult>();
    private List<InvItem> _inventory = new List<InvItem>();
    private string _probe = "/ | \\ \" ' , ; : < > { } [ ] % & # @ $ ^ * ~ ` ! ? = + - _ ( ) . \u2026 \u2014 \u00AB \u00BB \u201C \u201D";
    private bool _probePools = true;
    private List<ProbeRow> _probeRows = new List<ProbeRow>();
    private string _probePoolReport = "";
    private List<string> _addressHits = new List<string>();
    private List<string> _numberHits = new List<string>();
    private List<string> _codeFormatHits = new List<string>();
    private List<string> _pseudoViolations;
    private List<ChecklistRow> _checklist = new List<ChecklistRow>();
    private int _checklistFocus = -1;
    private string _summaryText = "";

    private const string ChecklistPath = LocalizationPaths.Findings + "/checklist.md";

    private void DrawFindingsTab()
    {
        _findTab = GUILayout.Toolbar(_findTab, FindTabNames);
        _findScroll = EditorGUILayout.BeginScrollView(_findScroll);
        switch (_findTab)
        {
            case 0: DrawMaxLength(); break;
            case 1: DrawInventory(); break;
            case 2: DrawProbe(); break;
            case 3: DrawAddress(); break;
            case 4: DrawNumbers(); break;
            case 5: DrawPseudoCheck(); break;
            case 6: DrawSourceLint(); break;
            case 7: DrawChecklist(); break;
            case 8: DrawSummary(); break;
        }
        EditorGUILayout.EndScrollView();
    }

    // ---------------------------------------------------------------- 1. max length

    private void DrawMaxLength()
    {
        LocUI.Hint("For each bound TMP element, finds the longest text that still fits the element's rect at its real font and size, and reports it as a percent of the English length.");
        if (GUILayout.Button("Measure all TMP elements")) MeasureMaxLengths();

        if (_lenResults.Count == 0) return;
        var groups = _lenResults.Where(r => r.maxLen > 0).GroupBy(r => r.category).OrderBy(g => g.Key);
        foreach (var g in groups)
        {
            var worst = g.OrderBy(r => r.Ratio).First();
            EditorGUILayout.LabelField($"{g.Key}: {g.Count()} elements | min {worst.Ratio * 100f:0}% | avg {g.Average(r => r.Ratio) * 100f:0}% | worst: {worst.key}");
        }
        var overall = _lenResults.Where(r => r.maxLen > 0).OrderBy(r => r.Ratio).FirstOrDefault();
        if (overall != null) EditorGUILayout.LabelField($"Overall worst case: {overall.Ratio * 100f:0}% ({overall.key})", EditorStyles.boldLabel);

        EditorGUILayout.Space();
        foreach (var r in _lenResults.OrderBy(r => r.maxLen == 0 ? 999f : r.Ratio).Take(80))
            EditorGUILayout.LabelField(r.key, (r.maxLen > 0 ? $"{r.Ratio * 100f:0}% ({r.maxLen}/{r.sourceLen})" : "not measured") + " " + r.note, EditorStyles.miniLabel);
    }

    private static string PlainText(string source)
    {
        var sb = new StringBuilder();
        foreach (var s in PseudoTokenizer.Tokenize(source))
        {
            if (s.kind == PseudoSegmentKind.Text || s.kind == PseudoSegmentKind.Input || s.kind == PseudoSegmentKind.Numeric || s.kind == PseudoSegmentKind.Technical) sb.Append(s.text);
            else if (s.kind == PseudoSegmentKind.LineBreak || s.kind == PseudoSegmentKind.Escape) sb.Append(' ');
        }
        string r = sb.ToString().Trim();
        return r.Length > 0 ? r : source;
    }

    private static string Filler(string plain, int n)
    {
        if (n <= plain.Length) return plain.Substring(0, n);
        var sb = new StringBuilder(n);
        while (sb.Length < n) sb.Append(plain).Append(' ');
        return sb.ToString(0, n);
    }

    private void MeasureMaxLengths()
    {
        CommitPendingSource();
        _lenResults.Clear();
        _overflowKeys.Clear();
        var bindings = LocBindings.All.Where(b => b.kind == "TMP" && _table.TryGet(b.key, out _)).ToList();
        try
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (EditorUtility.DisplayCancelableProgressBar("Measuring", b.key, (float)i / bindings.Count)) break;
                _table.TryGet(b.key, out var entry);
                var res = new LenResult { key = b.key, category = string.IsNullOrEmpty(b.category) ? "Label" : b.category };

                var tmp = LocalizationApply.ResolveTmp(b, out var prefabRoot, out var _);
                try
                {
                    string plain = PlainText(entry.source);
                    res.sourceLen = Mathf.Max(1, PseudoTokenizer.VisibleLength(entry.source));
                    if (tmp == null) res.note = "(origin not loaded)";
                    else if (tmp.enableAutoSizing) res.note = "(autosize)";
                    else
                    {
                        var rect = tmp.rectTransform.rect;
                        if (rect.width < 1f || rect.height < 1f) res.note = "(rect unmeasurable)";
                        else
                        {
                            bool wrap = tmp.textWrappingMode != TextWrappingModes.NoWrap;
                            Func<int, bool> fits = n =>
                            {
                                var p = tmp.GetPreferredValues(Filler(plain, n), wrap ? rect.width : 100000f, 100000f);
                                return p.y <= rect.height + 0.5f && (wrap || p.x <= rect.width + 0.5f);
                            };
                            int lo = 0, hi = Mathf.Max(8, res.sourceLen * 8);
                            while (lo < hi)
                            {
                                int mid = (lo + hi + 1) / 2;
                                if (fits(mid)) lo = mid; else hi = mid - 1;
                            }
                            res.maxLen = lo;
                            if (res.Ratio < 1f + _settings.pseudoExpansion) _overflowKeys.Add(b.key);
                        }
                    }
                }
                finally { if (prefabRoot != null) PrefabUtility.UnloadPrefabContents(prefabRoot); }
                _lenResults.Add(res);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        _filterDirty = true;
        _status = "Measured " + _lenResults.Count + " elements; " + _overflowKeys.Count + " would overflow at +" + (_settings.pseudoExpansion * 100f).ToString("0") + "%.";
    }

    // ---------------------------------------------------------------- 2. placeholders

    private void DrawInventory()
    {
        LocUI.Hint("Every placeholder, tag, escape and input identifier in the English source. Tags that wrap text can be moved by translators; positional tokens must stay where the sentence logic needs them.");
        if (GUILayout.Button("Build inventory")) BuildInventory();
        foreach (var grp in _inventory.GroupBy(i => i.classification).OrderBy(g => g.Key))
        {
            EditorGUILayout.LabelField(grp.Key.ToUpperInvariant(), EditorStyles.boldLabel);
            foreach (var i in grp.OrderByDescending(x => x.count))
                EditorGUILayout.LabelField($"{i.token}   x{i.count}", string.Join(", ", i.keys.Take(3)) + (i.keys.Count > 3 ? ", ..." : ""), EditorStyles.miniLabel);
        }
    }

    private void BuildInventory()
    {
        var map = new Dictionary<string, InvItem>();
        foreach (var e in _table.entries)
        {
            var segs = PseudoTokenizer.Tokenize(e.source);
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                if (s.kind == PseudoSegmentKind.Text || s.kind == PseudoSegmentKind.Numeric || s.kind == PseudoSegmentKind.LineBreak) continue;
                string cls;
                switch (s.kind)
                {
                    case PseudoSegmentKind.Placeholder: cls = "placeholder (positional)"; break;
                    case PseudoSegmentKind.Escape: cls = "escape (positional)"; break;
                    case PseudoSegmentKind.Input: cls = "input identifier (positional)"; break;
                    case PseudoSegmentKind.Technical: cls = "technical literal"; break;
                    default: cls = ClassifyTag(segs, i); break;
                }
                string token = s.kind == PseudoSegmentKind.Tag ? NormalizeTag(s.text) : s.text;
                string id = cls + "|" + token;
                if (!map.TryGetValue(id, out var item)) map[id] = item = new InvItem { kind = s.kind.ToString(), token = token, classification = cls };
                item.count++;
                if (!item.keys.Contains(e.key)) item.keys.Add(e.key);
            }
        }
        _inventory = map.Values.ToList();
        _status = "Inventory built: " + _inventory.Count + " distinct tokens.";
    }

    private static readonly Regex TagNameRx = new Regex(@"^[<\[]\s*/?\s*([A-Za-z0-9_]+)");

    private static string NormalizeTag(string tag)
    {
        if (tag == "[[" || tag == "]]") return tag;
        var m = TagNameRx.Match(tag);
        if (!m.Success) return tag;
        bool close = tag.Contains("/") && tag.IndexOf('/') <= 2;
        string open = tag[0].ToString();
        string closeCh = open == "<" ? ">" : "]";
        return open + (close ? "/" : "") + m.Groups[1].Value + closeCh;
    }

    private static string ClassifyTag(List<PseudoSegment> segs, int index)
    {
        string t = segs[index].text;
        if (t == "[[" || t == "]]") return "wrapping tag (translators may move)";
        var m = TagNameRx.Match(t);
        if (!m.Success) return "tag (unknown)";
        string name = m.Groups[1].Value.ToLowerInvariant();
        bool close = t.Contains("/") && t.IndexOf('/') <= 2;
        for (int j = 0; j < segs.Count; j++)
        {
            if (j == index || segs[j].kind != PseudoSegmentKind.Tag) continue;
            var m2 = TagNameRx.Match(segs[j].text);
            if (!m2.Success || m2.Groups[1].Value.ToLowerInvariant() != name) continue;
            bool close2 = segs[j].text.Contains("/") && segs[j].text.IndexOf('/') <= 2;
            if (close2 != close) return "wrapping tag (translators may move)";
        }
        return "positional tag";
    }

    // ---------------------------------------------------------------- 3. restricted chars

    private void DrawProbe()
    {
        LocUI.Hint("Pushes each probe character through the CSV round trip, the runtime table and string.Format, then the TMP renderer. Only characters that break something are listed.");
        _probe = EditorGUILayout.TextField("Probe characters", _probe);
        _probePools = EditorGUILayout.ToggleLeft("Also test the full pseudolocale pools (glyph coverage of the default font)", _probePools);
        if (GUILayout.Button("Run probe")) RunProbe();

        foreach (var r in _probeRows.Where(r => r.Any))
        {
            var flags = new List<string>();
            if (r.csvBreaks) flags.Add("breaks CSV round trip");
            if (r.formatBreaks) flags.Add("breaks string.Format (use {{ }})");
            if (r.parsedAsMarkup) flags.Add("consumed by TMP rich text");
            if (r.noGlyph) flags.Add("no glyph in default font");
            EditorGUILayout.LabelField($"U+{(int)r.c:X4} '{r.c}'", string.Join("; ", flags));
        }
        if (_probeRows.Count > 0 && !_probeRows.Any(r => r.Any)) EditorGUILayout.LabelField("No probe character broke anything.");
        if (!string.IsNullOrEmpty(_probePoolReport)) LocUI.Hint(_probePoolReport);

        if (_probeRows.Any(r => r.Any) && GUILayout.Button("Copy broken characters to forbidden list in settings"))
        {
            Undo.RecordObject(_settings, "Forbidden characters");
            _settings.forbiddenCharacters = new string(_probeRows.Where(r => r.csvBreaks).Select(r => r.c).Distinct().ToArray());
            EditorUtility.SetDirty(_settings);
        }
    }

    private void RunProbe()
    {
        _probeRows.Clear();
        var font = TMP_Settings.defaultFontAsset;
        var go = EditorUtility.CreateGameObjectWithHideFlags("LocProbe", HideFlags.HideAndDontSave, typeof(TextMeshPro));
        try
        {
            var tmp = go.GetComponent<TextMeshPro>();
            tmp.font = font;
            tmp.richText = true;
            foreach (char c in _probe.Where(ch => !char.IsWhiteSpace(ch)).Distinct())
            {
                var row = new ProbeRow { c = c };
                string sample = "a" + c + "b";

                // CSV + runtime table round trip
                var t = new LocalizationTable();
                t.Add(new LocEntry { sheet = "s", key = "k", source = sample, values = { { "xx", sample } } });
                t.AddLanguage("xx");
                var back = LocalizationTable.FromRuntimeCsv(t.ToRuntimeCsv());
                row.csvBreaks = !(back.TryGet("k", out var be) && be.source == sample && be.GetValue("xx") == sample);

                try { string.Format(sample + " {0}", 1); }
                catch (FormatException) { row.formatBreaks = true; }

                row.noGlyph = font != null && !font.HasCharacter(c, true);
                tmp.text = sample;
                tmp.ForceMeshUpdate();
                row.parsedAsMarkup = tmp.textInfo.characterCount != 3 && !row.noGlyph;
                _probeRows.Add(row);
            }

            var sb = new StringBuilder();
            if (_probePools && font != null)
                foreach (var locale in PseudoLocalizer.All)
                {
                    string missing = LocalizationValidator.MissingGlyphs(font, locale.ApprovedCharacters);
                    sb.AppendLine(locale.Code + ": " + (missing.Length == 0 ? "all pool characters have glyphs in the default font." : missing.Length + " pool character(s) have no glyph in " + font.name + " (needs fallback fonts): " + new string(missing.Take(60).ToArray())));
                }
            _probePoolReport = sb.ToString().TrimEnd();
        }
        finally { DestroyImmediate(go); }
    }

    // ---------------------------------------------------------------- 4. player address

    private static readonly Regex PlayerVar = new Regex(@"\{(player|name|user|username|officer|inspector|comrade|[0-9]+)[^}]*\}", RegexOptions.IgnoreCase);
    private static readonly Regex Gendered = new Regex(@"\b(he|she|him|her|his|hers|himself|herself|sir|madam|ma'am|mr|mrs|ms|miss|mister|man|woman|men|women|boy|girl|lad|lady|gentleman|brother|sister|comrade)\b", RegexOptions.IgnoreCase);

    private void DrawAddress()
    {
        LocUI.Hint("Lists player-name variables and gendered wording. Whether the player is addressed as male, female or unspecified is a design decision.");
        if (GUILayout.Button("Scan strings"))
        {
            _addressHits.Clear();
            foreach (var e in _table.entries)
            {
                var vars = PlayerVar.Matches(e.source).Cast<Match>().Select(m => m.Value).Distinct().ToList();
                var words = Gendered.Matches(e.source).Cast<Match>().Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();
                if (vars.Count == 0 && words.Count == 0) continue;
                _addressHits.Add(e.key + " | vars: " + (vars.Count == 0 ? "-" : string.Join(" ", vars)) + " | gendered: " + (words.Count == 0 ? "-" : string.Join(", ", words)) + " | " + e.source.Replace("\n", " "));
            }
        }
        EditorGUILayout.LabelField(_addressHits.Count + " hit(s)");
        foreach (var h in _addressHits.Take(200)) EditorGUILayout.LabelField(h, EditorStyles.wordWrappedMiniLabel);
    }

    // ---------------------------------------------------------------- 5. numbers / dates

    private static readonly Regex NumberRx = new Regex(@"\d");
    private static readonly Regex DateRx = new Regex(@"\b\d{1,4}[/.\-]\d{1,2}[/.\-]\d{1,4}\b");
    private static readonly Regex TimeRx = new Regex(@"\b\d{1,2}:\d{2}(:\d{2})?\s?(am|pm)?\b", RegexOptions.IgnoreCase);
    private static readonly Regex FormatCodeRx = new Regex(@"\{\d+:[^}]+\}|%\d*\.?\d*[dfs]");
    private static readonly Regex CodeFormatRx = new Regex(@"ToString\(\s*""|string\.Format\(|String\.Format\(|DateTime\.|TimeSpan\.|\$""[^""]*\{[^}]*:[^}]*\}");

    private void DrawNumbers()
    {
        LocUI.Hint("Strings that contain numerals, dates, times or format codes, plus code lines that format values without a CultureInfo (these ignore the player's language).");
        if (GUILayout.Button("Scan strings and code"))
        {
            _numberHits.Clear();
            foreach (var e in _table.entries)
            {
                var tags = new List<string>();
                if (DateRx.IsMatch(e.source)) tags.Add("date");
                if (TimeRx.IsMatch(e.source)) tags.Add("time");
                if (FormatCodeRx.IsMatch(e.source)) tags.Add("format code");
                if (tags.Count == 0 && NumberRx.IsMatch(e.source)) tags.Add("numeral");
                if (tags.Count > 0) _numberHits.Add(e.key + " | " + string.Join(", ", tags) + " | " + e.source.Replace("\n", " "));
            }

            _codeFormatHits.Clear();
            string root = LocalizationPaths.Abs("Assets/_GoodCopBadCop/_Scripts");
            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string norm = file.Replace('\\', '/');
                if (norm.Contains("/Editor/") || norm.Contains("/Localization/")) continue;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length && _codeFormatHits.Count < 300; i++)
                {
                    string l = lines[i];
                    if (!CodeFormatRx.IsMatch(l) || l.Contains("CultureInfo") || l.Contains("InvariantCulture") || l.TrimStart().StartsWith("//")) continue;
                    _codeFormatHits.Add(Path.GetFileName(file) + ":" + (i + 1) + "  " + l.Trim());
                }
            }
        }
        EditorGUILayout.LabelField("Strings (" + _numberHits.Count + ")", EditorStyles.boldLabel);
        foreach (var h in _numberHits.Take(150)) EditorGUILayout.LabelField(h, EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField("Code formatting without CultureInfo (" + _codeFormatHits.Count + ")", EditorStyles.boldLabel);
        foreach (var h in _codeFormatHits.Take(150)) EditorGUILayout.LabelField(h, EditorStyles.wordWrappedMiniLabel);
    }

    // ---------------------------------------------------------------- 6. pseudo self check

    private void DrawPseudoCheck()
    {
        LocUI.Hint("Re-verifies the manager's QC list for every pseudolocale and every source string: length math, protected content identical, line breaks in place, only approved characters.");
        if (GUILayout.Button("Run self-check")) _pseudoViolations = LocalizationValidator.SelfCheckPseudo(_table.entries, _settings.pseudoExpansion);
        if (_pseudoViolations == null) return;
        EditorGUILayout.LabelField(_pseudoViolations.Count == 0 ? "No violations across " + _table.entries.Count + " strings." : _pseudoViolations.Count + " violation(s)");
        foreach (var v in _pseudoViolations.Take(150)) EditorGUILayout.LabelField(v, EditorStyles.wordWrappedMiniLabel);
    }

    // ---------------------------------------------------------------- 7. source lint

    private List<string> _lintHits;
    private string _lintFilter = "";

    private void DrawSourceLint()
    {
        LocUI.Hint("Checks the English source before export: leading/trailing whitespace, double spaces, '\u2026' and curly quotes (use '...' and '), " +
                   "confirmed terminology (Kill, Behavior / Physical Exam, Disposition Chart, postbox, Mutant Sample, ', Officer') and US spelling (Luka's 'Mum' is allowed).");
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Run source lint", GUILayout.Width(140))) { CommitPendingSource(); _lintHits = LocalizationValidator.LintAll(_table.entries); }
            _lintFilter = EditorGUILayout.TextField(_lintFilter, EditorStyles.toolbarSearchField);
        }
        if (_lintHits == null) return;
        var shown = string.IsNullOrEmpty(_lintFilter) ? _lintHits : _lintHits.Where(h => h.IndexOf(_lintFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        EditorGUILayout.LabelField(_lintHits.Count == 0 ? "No warnings across " + _table.entries.Count + " strings." : shown.Count + " of " + _lintHits.Count + " warning(s)");
        foreach (var h in shown.Take(300)) EditorGUILayout.LabelField(h, EditorStyles.wordWrappedMiniLabel);
    }

    // ---------------------------------------------------------------- 8. checklist

    private static readonly string[] AreaNames =
    {
        "Main menu", "Settings", "HUD", "Dialogue / subtitles", "Tooltips and prompts",
        "Guidebook", "Newspaper", "PC terminal", "Tasks / phone", "End of shift report"
    };

    private static readonly string[] LatinItems =
    {
        "No tofu (missing glyph boxes) in any font, including terminal and handwriting",
        "Both Latin Extended and Cyrillic glyphs render in every font style",
        "No text overflows or is clipped in buttons, labels and panels",
        "No text overlaps neighbouring UI",
        "Diacritics are not clipped at line top or bottom",
        "Placeholders and variables resolve (no raw {0} or %s)",
        "Rich text tags are interpreted (no raw tags visible)",
        "Line breaks are preserved and multi-line text fits",
        "Wrapper brackets are visible at both ends (nothing is truncated)",
        "No technical strings (keys, ids, paths) leak into the UI",
        "Language choice persists after restart"
    };

    private static readonly string[] CjkItems =
    {
        "No tofu in any font, including terminal and handwriting",
        "Fallback fonts apply to every text element",
        "Line height does not clip glyphs vertically",
        "Text without spaces wraps and fits",
        "Typewriter reveal works with wide characters",
        "Input prompts and numerals stay intact",
        "Text is legible at the applied size scale"
    };

    private void LoadChecklist()
    {
        _checklist = new List<ChecklistRow>();
        string abs = LocalizationPaths.Abs(ChecklistPath);
        if (File.Exists(abs))
        {
            string section = "";
            foreach (var raw in File.ReadAllLines(abs))
            {
                string line = raw.Trim();
                if (line.StartsWith("## ")) { section = line.Substring(3); continue; }
                if (!(line.StartsWith("- [ ] ") || line.StartsWith("- [x] "))) continue;
                string body = line.Substring(6);
                int bar = body.IndexOf(" | ", StringComparison.Ordinal);
                _checklist.Add(new ChecklistRow { section = section, done = line[3] == 'x', text = bar >= 0 ? body.Substring(0, bar) : body, note = bar >= 0 ? body.Substring(bar + 3) : "" });
            }
        }
        if (_checklist.Count == 0)
        {
            foreach (var t in LatinItems) _checklist.Add(new ChecklistRow { section = "Latin / Cyrillic", text = t });
            foreach (var t in CjkItems) _checklist.Add(new ChecklistRow { section = "CJK", text = t });
            foreach (var a in AreaNames) _checklist.Add(new ChecklistRow { section = "Screens: Latin / Cyrillic", text = a });
            foreach (var a in AreaNames) _checklist.Add(new ChecklistRow { section = "Screens: CJK", text = a });
        }
    }

    private void SaveChecklist()
    {
        LocalizationPaths.EnsureFolders();
        var sb = new StringBuilder("# Localization testing checklist\n");
        foreach (var g in _checklist.GroupBy(r => r.section))
        {
            sb.Append("\n## ").Append(g.Key).Append('\n');
            foreach (var r in g)
                sb.Append(r.done ? "- [x] " : "- [ ] ").Append(r.text).Append(string.IsNullOrEmpty(r.note) ? "" : " | " + r.note.Replace("\n", " ")).Append('\n');
        }
        File.WriteAllText(LocalizationPaths.Abs(ChecklistPath), sb.ToString(), new UTF8Encoding(false));
        AssetDatabase.Refresh();
    }

    private void DrawChecklist()
    {
        LocUI.Hint("Replace or extend the default items with the manager's exact checklist by editing Localization/Findings/checklist.md (format: '- [ ] item | note').");
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Save checklist")) { SaveChecklist(); _status = "Saved " + ChecklistPath; }
            if (GUILayout.Button("Reset to defaults")) { if (File.Exists(LocalizationPaths.Abs(ChecklistPath))) File.Delete(LocalizationPaths.Abs(ChecklistPath)); LoadChecklist(); }
            if (GUILayout.Button("Next screen (sets matching pseudolocale live)")) NextArea();
        }

        string current = "";
        for (int i = 0; i < _checklist.Count; i++)
        {
            var r = _checklist[i];
            if (r.section != current) { current = r.section; EditorGUILayout.LabelField(current, EditorStyles.boldLabel); }
            using (new EditorGUILayout.HorizontalScope(i == _checklistFocus ? EditorStyles.helpBox : GUIStyle.none))
            {
                r.done = EditorGUILayout.ToggleLeft(r.text, r.done, GUILayout.Width(position.width * 0.55f));
                r.note = EditorGUILayout.TextField(r.note);
            }
        }
    }

    private void NextArea()
    {
        var screens = Enumerable.Range(0, _checklist.Count).Where(i => _checklist[i].section.StartsWith("Screens")).ToList();
        if (screens.Count == 0) return;
        int pos = screens.IndexOf(_checklistFocus);
        _checklistFocus = screens[(pos + 1) % screens.Count];
        var row = _checklist[_checklistFocus];
        string code = row.section.Contains("CJK") ? "pseudo-cjk" : "pseudo-latincyr";
        if (Application.isPlaying) Loc.SetLanguage(code);
        _status = "Review area: " + row.text + " (" + code + ")" + (Application.isPlaying ? "" : " - enter Play Mode to switch the game");
    }

    // ---------------------------------------------------------------- 8. summary

    private void DrawSummary()
    {
        LocUI.Hint("Builds the findings report for the localization manager from the results gathered in the other sub-tabs (run them first).");
        if (GUILayout.Button("Build and save findings summary"))
        {
            _summaryText = BuildSummary();
            LocalizationPaths.EnsureFolders();
            File.WriteAllText(LocalizationPaths.Abs(LocalizationPaths.Findings + "/findings_summary.md"), _summaryText, new UTF8Encoding(false));
            AssetDatabase.Refresh();
            _status = "Saved " + LocalizationPaths.Findings + "/findings_summary.md";
        }
        if (!string.IsNullOrEmpty(_summaryText)) EditorGUILayout.TextArea(_summaryText, GUILayout.MinHeight(300));
    }

    private string BuildSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Localization findings").AppendLine();
        sb.AppendLine("Generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ". " + _table.entries.Count + " strings in " + _table.SheetNames().Count() + " sheets. Pseudolocale expansion: " + (_settings.pseudoExpansion * 100f).ToString("0") + "%.").AppendLine();

        sb.AppendLine("## Max string length per UI element");
        var measured = _lenResults.Where(r => r.maxLen > 0).ToList();
        if (measured.Count == 0) sb.AppendLine("Not measured yet.");
        else
        {
            sb.AppendLine("| Category | Elements | Min % of source | Avg % | Worst key |").AppendLine("|---|---|---|---|---|");
            foreach (var g in measured.GroupBy(r => r.category).OrderBy(g => g.Key))
            {
                var worst = g.OrderBy(r => r.Ratio).First();
                sb.AppendLine($"| {g.Key} | {g.Count()} | {worst.Ratio * 100f:0} | {g.Average(r => r.Ratio) * 100f:0} | {worst.key} |");
            }
            var overall = measured.OrderBy(r => r.Ratio).First();
            sb.AppendLine().AppendLine($"Overall worst case: {overall.Ratio * 100f:0}% of source ({overall.key}).");
            int over = measured.Count(r => r.Ratio < 1f + _settings.pseudoExpansion);
            sb.AppendLine($"{over} element(s) cannot hold +{_settings.pseudoExpansion * 100f:0}% and need layout work.");
        }

        sb.AppendLine().AppendLine("## Placeholders, tags and variables");
        if (_inventory.Count == 0) sb.AppendLine("Not built yet.");
        else foreach (var g in _inventory.GroupBy(i => i.classification).OrderBy(g => g.Key))
            {
                sb.AppendLine("### " + g.Key);
                foreach (var i in g.OrderByDescending(x => x.count)) sb.AppendLine("- `" + i.token + "` x" + i.count + " (e.g. " + string.Join(", ", i.keys.Take(2)) + ")");
            }

        sb.AppendLine().AppendLine("## Restricted characters");
        if (_probeRows.Count == 0) sb.AppendLine("Probe not run yet.");
        else
        {
            var broken = _probeRows.Where(r => r.Any).ToList();
            if (broken.Count == 0) sb.AppendLine("No probe character broke the CSV round trip, runtime parser, string.Format or TMP rendering.");
            foreach (var r in broken)
                sb.AppendLine($"- U+{(int)r.c:X4} `{r.c}`: " + string.Join(", ", new[] { r.csvBreaks ? "CSV" : null, r.formatBreaks ? "string.Format" : null, r.parsedAsMarkup ? "TMP markup" : null, r.noGlyph ? "no glyph" : null }.Where(x => x != null)));
            if (!string.IsNullOrEmpty(_probePoolReport)) sb.AppendLine().AppendLine(_probePoolReport);
        }

        sb.AppendLine().AppendLine("## Player address");
        sb.AppendLine(_addressHits.Count == 0 ? "No player-name variables or gendered wording found (or scan not run)." : _addressHits.Count + " string(s) with player variables or gendered wording:");
        foreach (var h in _addressHits.Take(60)) sb.AppendLine("- " + h);
        sb.AppendLine("Decision needed: gender-neutral, male or female address.");

        sb.AppendLine().AppendLine("## Numbers, dates and times");
        sb.AppendLine(_numberHits.Count + " string(s) contain numerals, dates, times or format codes; " + _codeFormatHits.Count + " code line(s) format values without a CultureInfo.");
        foreach (var h in _codeFormatHits.Take(30)) sb.AppendLine("- " + h);

        sb.AppendLine().AppendLine("## Pseudolocale self-check");
        sb.AppendLine(_pseudoViolations == null ? "Not run." : _pseudoViolations.Count + " violation(s).");

        sb.AppendLine().AppendLine("## Testing checklist");
        foreach (var g in _checklist.GroupBy(r => r.section))
        {
            sb.AppendLine("### " + g.Key + " (" + g.Count(r => r.done) + "/" + g.Count() + ")");
            foreach (var r in g.Where(r => !r.done || !string.IsNullOrEmpty(r.note)))
                sb.AppendLine("- [" + (r.done ? "x" : " ") + "] " + r.text + (string.IsNullOrEmpty(r.note) ? "" : " - " + r.note));
        }

        sb.AppendLine().AppendLine("## Other technical constraints");
        sb.AppendLine("- Fonts: Liberation Sans is the default; CJK needs fallback fonts (see Fonts tab coverage report).");
        sb.AppendLine("- Localization is client-side only; network messages carry ids, never translated text.");
        return sb.ToString();
    }
}
