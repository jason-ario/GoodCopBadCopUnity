using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public partial class LocalizationWindow
{
    private class ImportRow
    {
        public bool apply;
        public string key;
        public string lang;
        public string oldValue;
        public string newValue;
        public string file;
        /// <summary>The source_en the translator worked from, when it differs from the current source; otherwise null.</summary>
        public string translatedFrom;
        /// <summary>True when translatedFrom differs from the current source by more than case, whitespace or typography.</summary>
        public bool sourceMismatch;
        /// <summary>0-1 similarity between translatedFrom and the current source (1 when they match).</summary>
        public float sourceSimilarity = 1f;
        public List<LocIssue> issues = new List<LocIssue>();
        public bool HasError { get { return issues.Any(i => i.severity == IssueSeverity.Error); } }
    }

    /// <summary>How existing translations are written into handoff CSVs. English (source_en) is always exported.</summary>
    private enum ExportTranslations { Include, BlankColumns, SourceOnly }
    private static readonly string[] ExportTranslationsLabels =
    {
        "Include current translations",
        "Empty language columns (headers kept for re-import)",
        "English source only (no language columns)"
    };

    private string _exportFolder = "";
    private ExportTranslations _exportTranslations = ExportTranslations.Include;
    private string _importFolder = "";
    private readonly HashSet<string> _exportPseudo = new HashSet<string>();
    private List<ImportRow> _importRows;
    private readonly List<string> _importUnknown = new List<string>();
    private readonly List<string> _importMissing = new List<string>();
    private readonly List<string> _importSourceChanged = new List<string>();
    private Vector2 _ioScroll;

    private enum ImportFilter { All, Selected, NotSelected, Flagged, CloseMatches, Errors }
    private static readonly string[] ImportFilterLabels =
    {
        "All", "Selected", "Not selected", "Translated from different English", "Close matches (small edits)", "Other errors (placeholders, tags...)"
    };
    private const int ImportPageSize = 100;
    private ImportFilter _importFilter = ImportFilter.All;
    private int _importPage;

    private static bool IsCloseMatch(ImportRow r) =>
        r.sourceMismatch && r.sourceSimilarity >= 0.8f && !r.issues.Any(i => i.severity == IssueSeverity.Error && i.code != "source");

    private IEnumerable<ImportRow> FilteredImportRows()
    {
        switch (_importFilter)
        {
            case ImportFilter.Selected: return _importRows.Where(r => r.apply);
            case ImportFilter.NotSelected: return _importRows.Where(r => !r.apply);
            case ImportFilter.Flagged: return _importRows.Where(r => r.sourceMismatch);
            case ImportFilter.CloseMatches: return _importRows.Where(IsCloseMatch);
            case ImportFilter.Errors: return _importRows.Where(r => r.issues.Any(i => i.severity == IssueSeverity.Error && i.code != "source"));
            default: return _importRows;
        }
    }

    /// <summary>0-1 similarity of two source strings (normalised Levenshtein over NormalizeSource).</summary>
    private static float SourceSimilarity(string a, string b)
    {
        a = NormalizeSource(a); b = NormalizeSource(b);
        int n = a.Length, m = b.Length;
        if (n == 0 && m == 0) return 1f;
        var prev = new int[m + 1]; var cur = new int[m + 1];
        for (int j = 0; j <= m; j++) prev[j] = j;
        for (int i = 1; i <= n; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= m; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            var t = prev; prev = cur; cur = t;
        }
        return 1f - (float)prev[m] / Math.Max(n, m);
    }

    private void DrawImportExportTab()
    {
        _ioScroll = EditorGUILayout.BeginScrollView(_ioScroll);

        LocUI.Section("Export (handoff)");
        EditorGUILayout.LabelField("One CSV per thematic sheet: key, notes, source_en and language columns. UTF-8 with BOM for spreadsheet tools.", EditorStyles.wordWrappedMiniLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            _exportFolder = EditorGUILayout.TextField("Folder", _exportFolder);
            if (GUILayout.Button("...", GUILayout.Width(30))) _exportFolder = EditorUtility.OpenFolderPanel("Export folder", _exportFolder, "");
        }
        _exportTranslations = (ExportTranslations)EditorGUILayout.Popup("Translations", (int)_exportTranslations, ExportTranslationsLabels);
        if (_exportTranslations != ExportTranslations.Include)
            EditorGUILayout.LabelField("Only the exported files change. Translations in the project sheets stay as they are.", EditorStyles.wordWrappedMiniLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Include pseudolocale columns", GUILayout.Width(190));
            foreach (var code in PseudoLocalizer.Codes)
            {
                bool on = _exportPseudo.Contains(code);
                bool nv = GUILayout.Toggle(on, code, GUILayout.Width(130));
                if (nv && !on) _exportPseudo.Add(code);
                if (!nv && on) _exportPseudo.Remove(code);
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = !string.IsNullOrEmpty(_exportFolder);
            if (GUILayout.Button("Export all sheets")) ExportSheets(_exportFolder, false);
            GUI.enabled = !string.IsNullOrEmpty(_exportFolder) && AnyDeltaGroup;
            if (GUILayout.Button("Export delta only")) ExportSheets(_exportFolder, true);
            GUI.enabled = true;
        }
        DrawDeltaGroups();

        EditorGUILayout.Space();
        LocUI.Section("Import");
        using (new EditorGUILayout.HorizontalScope())
        {
            _importFolder = EditorGUILayout.TextField("Folder", _importFolder);
            if (GUILayout.Button("...", GUILayout.Width(30))) _importFolder = EditorUtility.OpenFolderPanel("Import folder", _importFolder, "");
        }
        GUI.enabled = !string.IsNullOrEmpty(_importFolder) && Directory.Exists(_importFolder);
        if (GUILayout.Button("Read and diff")) BuildImportDiff(_importFolder);
        GUI.enabled = true;

        if (_importRows != null)
        {
            int mismatchCells = _importRows.Count(r => r.sourceMismatch);
            EditorGUILayout.LabelField($"{_importRows.Count} changed cell(s) | {_importUnknown.Count} unknown key(s) | {_importMissing.Count} key(s) missing from the file | {_importSourceChanged.Count} key(s) translated from different English");
            if (mismatchCells > 0)
                EditorGUILayout.HelpBox(mismatchCells + " cell(s) were translated from English that has since been edited and are unticked. Leave them out and re-send those strings, or tick individual cells to keep them as drafts (they will be marked 'changed').", MessageType.Warning);

            int selected = _importRows.Count(r => r.apply);
            int closeCells = _importRows.Count(IsCloseMatch);
            EditorGUILayout.LabelField($"{selected} of {_importRows.Count} cell(s) selected ({_importRows.Count(r => r.apply && r.sourceMismatch)} flagged).", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select valid")) foreach (var r in _importRows) r.apply = !r.HasError;
                GUI.enabled = closeCells > 0;
                if (GUILayout.Button(new GUIContent("Also select close matches (" + closeCells + ")",
                        "Flagged cells whose old and current English are at least 80% similar (small edits, spelling changes) and have no other errors. Applied as drafts marked 'changed'.")))
                    foreach (var r in _importRows.Where(IsCloseMatch)) r.apply = true;
                GUI.enabled = true;
                if (GUILayout.Button("Select none")) foreach (var r in _importRows) r.apply = false;
                if (GUILayout.Button("Apply selected")) ApplyImport();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var nf = (ImportFilter)EditorGUILayout.Popup("Show", (int)_importFilter, ImportFilterLabels);
                if (nf != _importFilter) { _importFilter = nf; _importPage = 0; }
            }
            var visible = FilteredImportRows().ToList();
            int pages = Mathf.Max(1, (visible.Count + ImportPageSize - 1) / ImportPageSize);
            _importPage = Mathf.Clamp(_importPage, 0, pages - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = _importPage > 0;
                if (GUILayout.Button("<", GUILayout.Width(30))) _importPage--;
                GUI.enabled = true;
                EditorGUILayout.LabelField($"Page {_importPage + 1} / {pages}  ({visible.Count} cell(s))", GUILayout.Width(220));
                GUI.enabled = _importPage < pages - 1;
                if (GUILayout.Button(">", GUILayout.Width(30))) _importPage++;
                GUI.enabled = true;
            }

            foreach (var r in visible.Skip(_importPage * ImportPageSize).Take(ImportPageSize))
            {
                using (new EditorGUILayout.VerticalScope(LocUI.CardStyle))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        r.apply = EditorGUILayout.Toggle(r.apply, GUILayout.Width(18));
                        EditorGUILayout.LabelField(r.key + "  [" + r.lang + "]", EditorStyles.boldLabel);
                    }
                    if (r.sourceMismatch && _table.TryGet(r.key, out var cur))
                    {
                        EditorGUILayout.LabelField("EN translated from: " + r.translatedFrom, EditorStyles.wordWrappedMiniLabel);
                        EditorGUILayout.LabelField("EN current:            " + cur.source, EditorStyles.wordWrappedMiniLabel);
                    }
                    if (!string.IsNullOrEmpty(r.oldValue)) EditorGUILayout.LabelField("- " + r.oldValue, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.LabelField("+ " + r.newValue, EditorStyles.wordWrappedMiniLabel);
                    foreach (var i in r.issues)
                        EditorGUILayout.HelpBox(i.ToString(), i.severity == IssueSeverity.Error ? MessageType.Error : MessageType.Warning);
                }
            }


            DrawStringList("Unknown keys (not in any sheet)", _importUnknown);
            DrawStringList("Keys missing from the imported file", _importMissing);
            DrawStringList("Translated from different English (unticked)", _importSourceChanged);
        }

        EditorGUILayout.EndScrollView();
    }

    private static void DrawStringList(string title, List<string> list)
    {
        if (list.Count == 0) return;
        EditorGUILayout.LabelField(title + " (" + list.Count + ")", EditorStyles.boldLabel);
        foreach (var s in list.Take(40)) EditorGUILayout.LabelField(s, EditorStyles.miniLabel);
        if (list.Count > 40) EditorGUILayout.LabelField("...");
    }

    // ---------------------------------------------------------------- Export

    private void ExportSheets(string folder, bool delta)
    {
        if (!EnsureFreshTable()) return;
        CommitPendingSource();
        SaveDirty();
        var langs = _exportTranslations == ExportTranslations.SourceOnly
            ? new List<string>()
            : LocalizationSheetIO.StoredLanguages(_table).ToList();
        var storedLangs = LocalizationSheetIO.StoredLanguages(_table).ToList();
        bool blankLangs = _exportTranslations == ExportTranslations.BlankColumns;
        string stamp = DateTime.Now.ToString("yyyyMMdd");
        int files = 0, rowsTotal = 0;

        foreach (var sheet in _table.SheetNames().ToList())
        {
            var entries = _table.entries.Where(e => e.sheet == sheet).ToList();
            if (delta) entries = entries.Where(e => InDelta(e, storedLangs)).ToList();
            if (entries.Count == 0) continue;

            var rows = new List<IList<string>>();
            var header = new List<string> { "key", "notes", LocalizationSheetIO.MaxCharsColumn, "source_en" };
            header.AddRange(langs);
            header.AddRange(_exportPseudo);
            if (delta) header.Add("change");
            rows.Add(header);

            foreach (var e in entries)
            {
                var row = new List<string> { e.key, e.notes, LocalizationSheetIO.FormatMaxChars(e.maxChars), e.source };
                foreach (var l in langs) row.Add(blankLangs ? "" : e.GetValue(l));
                foreach (var p in _exportPseudo) row.Add(PseudoLocalizer.Generate(p, e.source, e.key, _settings.pseudoExpansion));
                if (delta) row.Add(DeltaReason(e, storedLangs));
                rows.Add(row);
            }

            string name = delta ? sheet + "_delta_" + stamp + ".csv" : sheet + ".csv";
            LocalizationSheetIO.WriteHandoff(Path.Combine(folder, name), LocalizationCsv.Write(rows));
            files++;
            rowsTotal += entries.Count;
        }
        string mode = _exportTranslations == ExportTranslations.Include ? "" : _exportTranslations == ExportTranslations.BlankColumns ? ", empty language columns" : ", English only";
        _status = "Exported " + files + " file(s), " + rowsTotal + " string(s) to " + folder + (delta ? " (delta" + mode + ")." : (mode.Length > 0 ? " (" + mode.Substring(2) + ")." : "."));
    }

    /// <summary>
    /// Why a row goes into the delta: "new" (never frozen), "changed" (English edited since the freeze) or
    /// "untranslated" (frozen, but at least one stored language is still empty). Null = up to date.
    /// </summary>
    private static string DeltaReason(LocEntry e, IList<string> languages)
    {
        string status = LocalizationSheetIO.Status(e);
        if (status != "frozen") return status;
        foreach (var l in languages)
            if (string.IsNullOrEmpty(e.GetValue(l))) return "untranslated";
        return null;
    }

    // ---------------------------------------------------------------- Import

    private void BuildImportDiff(string folder)
    {
        if (!EnsureFreshTable()) return;
        _importRows = new List<ImportRow>();
        _importUnknown.Clear();
        _importMissing.Clear();
        _importSourceChanged.Clear();
        _importPage = 0;

        foreach (var file in Directory.GetFiles(folder, "*.csv").OrderBy(f => f))
        {
            var rows = LocalizationCsv.Parse(File.ReadAllText(file, Encoding.UTF8));
            if (rows.Count == 0) continue;
            var header = rows[0];
            int keyCol = header.IndexOf("key");
            int srcCol = header.IndexOf("source_en");
            if (keyCol < 0) continue;

            string sheetName = Path.GetFileNameWithoutExtension(file);
            int deltaIdx = sheetName.IndexOf("_delta_", StringComparison.Ordinal);
            if (deltaIdx > 0) sheetName = sheetName.Substring(0, deltaIdx);
            // Spreadsheet apps (Numbers) export sheets as "<Sheet>-Table 1.csv".
            sheetName = System.Text.RegularExpressions.Regex.Replace(sheetName, @"-Table \d+$", "");

            var fixedCols = new HashSet<string>(LocalizationSheetIO.FixedColumns) { "change" };
            var langCols = new List<KeyValuePair<int, string>>();
            for (int c = 0; c < header.Count; c++)
                if (!fixedCols.Contains(header[c]) && !PseudoLocalizer.IsPseudo(header[c]) && !string.IsNullOrEmpty(header[c]))
                    langCols.Add(new KeyValuePair<int, string>(c, header[c]));

            var seen = new HashSet<string>();
            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (keyCol >= row.Count) continue;
                string key = row[keyCol];
                if (string.IsNullOrEmpty(key)) continue;
                seen.Add(key);

                if (!_table.TryGet(key, out var entry)) { _importUnknown.Add(key + "  (" + Path.GetFileName(file) + ")"); continue; }
                string fileSource = srcCol >= 0 && srcCol < row.Count ? row[srcCol] : null;
                string translatedFrom = fileSource != null && fileSource != entry.source ? fileSource : null;
                bool mismatch = translatedFrom != null && NormalizeSource(translatedFrom) != NormalizeSource(entry.source);
                float similarity = mismatch ? SourceSimilarity(translatedFrom, entry.source) : 1f;
                if (mismatch) _importSourceChanged.Add(key + "  (" + Path.GetFileName(file) + ")");

                foreach (var kv in langCols)
                {
                    if (kv.Key >= row.Count || string.IsNullOrEmpty(row[kv.Key])) continue;
                    string nv = row[kv.Key];
                    string ov = entry.GetValue(kv.Value);
                    if (nv == ov) continue;

                    var ir = new ImportRow { key = key, lang = kv.Value, oldValue = ov, newValue = nv, file = Path.GetFileName(file),
                                             translatedFrom = translatedFrom, sourceMismatch = mismatch, sourceSimilarity = similarity };
                    ir.issues = LocalizationValidator.Validate(entry.source, nv, _settings, FontFor(kv.Value, null), entry.maxChars);
                    if (mismatch)
                        ir.issues.Insert(0, new LocIssue { severity = IssueSeverity.Error, code = "source", message = "Translated from different English. The source text was edited after this file was exported; this translation may not match the current line. If applied anyway, the string is marked 'changed' so it goes in the next delta." });
                    ir.apply = !ir.HasError;
                    _importRows.Add(ir);
                }
            }

            foreach (var e in _table.entries.Where(e => e.sheet == sheetName && !seen.Contains(e.key)))
                _importMissing.Add(e.key + "  (" + sheetName + ")");
        }
        _status = "Diff built. Review and press Apply selected.";
    }

    private void ApplyImport()
    {
        // The diff's rows point into the loaded table; if the sheets changed on disk since, rebuild the diff instead.
        if (SheetsFingerprint() != _sheetsFingerprint)
        {
            _importRows = null;
            EnsureFreshTable();
            _status = "Sheets changed on disk since the diff was built. Press Read and diff again.";
            return;
        }
        int applied = 0, forcedMismatch = 0;
        foreach (var r in _importRows.Where(x => x.apply))
        {
            if (!_table.TryGet(r.key, out var e)) continue;
            _table.AddLanguage(r.lang);
            e.values[r.lang] = r.newValue;
            // Pin the freeze hash to the English the translator actually saw, so Status() reports 'changed' and the delta re-sends it.
            if (r.sourceMismatch)
            {
                e.frozenHash = LocalizationTable.Hash(r.translatedFrom);
                e.status = LocalizationSheetIO.Status(e);
                forcedMismatch++;
            }
            MarkDirty(e);
            applied++;
        }
        foreach (var s in _table.SheetNames()) _dirtySheets.Add(s); // new language columns affect every sheet
        SaveDirty();
        _importRows = null;
        _filterDirty = true;
        _status = "Imported " + applied + " translation(s)." +
                  (forcedMismatch > 0 ? " " + forcedMismatch + " were translated from older English and are marked 'changed'." : "");
    }

    /// <summary>Source text with case, whitespace and typography (ellipsis, curly quotes, dashes) normalised, for spotting cosmetic-only edits.</summary>
    private static string NormalizeSource(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char raw in s.Replace("\u2026", "..."))
        {
            char c = raw;
            switch (c)
            {
                case '\u2018': case '\u2019': case '\u201A': case '\u2032': c = '\''; break;
                case '\u201C': case '\u201D': case '\u201E': case '\u2033': c = '"'; break;
                case '\u2013': case '\u2014': c = '-'; break;
                case '\u00A0': c = ' '; break;
            }
            if (char.IsWhiteSpace(c)) { space = true; continue; }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    // ================================================================ Freeze / Delta tab

    private Vector2 _freezeScroll;

    private void DrawFreezeTab()
    {
        LocUI.Section("String freeze");
        EditorGUILayout.HelpBox("Freeze after sending a table out. Edits to frozen source text stay possible but are flagged 'changed' and appear in the next delta sheet. New strings are 'new' and also go into the delta, as do frozen strings still missing a language ('untranslated'). Untick a group below to leave it out of the delta.", MessageType.Info);

        int fresh = _table.entries.Count(e => LocalizationSheetIO.Status(e) == "new");
        int changed = _table.entries.Count(e => LocalizationSheetIO.Status(e) == "changed");
        int frozen = _table.entries.Count(e => LocalizationSheetIO.Status(e) == "frozen");
        var storedLangs = LocalizationSheetIO.StoredLanguages(_table).ToList();
        int untranslated = _table.entries.Count(e => DeltaReason(e, storedLangs) == "untranslated");
        EditorGUILayout.LabelField($"{frozen} frozen ({untranslated} still missing a language) | {changed} changed since freeze | {fresh} new");

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Freeze handoff")) FreezeHandoff();
            _exportFolder = EditorGUILayout.TextField(_exportFolder);
            if (GUILayout.Button("...", GUILayout.Width(30))) _exportFolder = EditorUtility.OpenFolderPanel("Delta export folder", _exportFolder, "");
            GUI.enabled = !string.IsNullOrEmpty(_exportFolder) && AnyDeltaGroup;
            if (GUILayout.Button("Export delta")) ExportSheets(_exportFolder, true);
            GUI.enabled = true;
        }
        DrawDeltaGroups();
        _exportTranslations = (ExportTranslations)EditorGUILayout.Popup("Delta translations", (int)_exportTranslations, ExportTranslationsLabels);

        var inDelta = _table.entries.Where(e => InDelta(e, storedLangs)).ToList();
        EditorGUILayout.LabelField(inDelta.Count + " string(s) will be exported.", EditorStyles.boldLabel);
        _freezeScroll = EditorGUILayout.BeginScrollView(_freezeScroll);
        foreach (var e in inDelta.Take(300))
            EditorGUILayout.LabelField(DeltaReason(e, storedLangs).ToUpperInvariant() + "  " + e.key, EditorStyles.miniLabel);
        EditorGUILayout.EndScrollView();
    }

    // ---- Delta groups: which DeltaReason values go into a delta export. Remembered per user.

    private const string DeltaPrefPrefix = "GCBC.Localization.Delta.";

    private static bool DeltaGroupOn(string reason) { return EditorPrefs.GetBool(DeltaPrefPrefix + reason, true); }

    private static bool AnyDeltaGroup { get { return DeltaGroupOn("new") || DeltaGroupOn("changed") || DeltaGroupOn("untranslated"); } }

    private static bool InDelta(LocEntry e, IList<string> languages)
    {
        string reason = DeltaReason(e, languages);
        return reason != null && DeltaGroupOn(reason);
    }

    private static void DrawDeltaGroups()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Delta includes", GUILayout.Width(150));
            foreach (var reason in new[] { "new", "changed", "untranslated" })
            {
                bool on = DeltaGroupOn(reason);
                bool nv = GUILayout.Toggle(on, reason, GUILayout.Width(110));
                if (nv != on) EditorPrefs.SetBool(DeltaPrefPrefix + reason, nv);
            }
        }
    }

    private void FreezeHandoff()
    {
        if (!EnsureFreshTable()) return;
        CommitPendingSource();
        int changedCount = _table.entries.Count(x => LocalizationSheetIO.Status(x) == "changed");
        bool keepChanged = false;
        if (changedCount > 0)
        {
            int choice = EditorUtility.DisplayDialogComplex("Freeze handoff",
                changedCount + " string(s) are marked 'changed' (edited English, or translations imported as drafts).\n\n" +
                "Freeze them too only if they were in the handoff you just sent. Otherwise keep them 'changed' so they go in the next delta.",
                "Keep them changed", "Cancel", "Freeze them too");
            if (choice == 1) return;
            keepChanged = choice == 0;
        }
        var sb = new StringBuilder();
        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        sb.AppendLine("# String freeze " + stamp).AppendLine();
        sb.AppendLine("| Sheet | Strings |").AppendLine("|---|---|");
        foreach (var s in _table.SheetNames()) sb.AppendLine("| " + s + " | " + _table.entries.Count(e => e.sheet == s) + " |");
        sb.AppendLine().AppendLine("| Key | Hash |").AppendLine("|---|---|");

        int stamped = 0;
        foreach (var e in _table.entries)
        {
            if (keepChanged && LocalizationSheetIO.Status(e) == "changed") continue;
            e.frozenHash = LocalizationTable.Hash(e.source);
            e.status = "frozen";
            stamped++;
            sb.AppendLine("| " + e.key + " | " + e.frozenHash + " |");
        }

        LocalizationPaths.EnsureFolders();
        string rel = LocalizationPaths.Freeze + "/manifest_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".md";
        File.WriteAllText(LocalizationPaths.Abs(rel), sb.ToString(), new UTF8Encoding(false));
        LocalizationSheetIO.SaveAll(_table);
        _sheetsFingerprint = SheetsFingerprint();
        _filterDirty = true;
        _status = "Froze " + stamped + " strings" + (keepChanged ? " (" + changedCount + " kept 'changed')" : "") + ". Manifest: " + rel;
    }
}
