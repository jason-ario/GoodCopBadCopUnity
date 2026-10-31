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
            if (GUILayout.Button("Export delta only")) ExportSheets(_exportFolder, true);
            GUI.enabled = true;
        }

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
            EditorGUILayout.LabelField($"{_importRows.Count} changed cell(s) | {_importUnknown.Count} unknown key(s) | {_importMissing.Count} key(s) missing from the file | {_importSourceChanged.Count} source text mismatch(es)");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select valid")) foreach (var r in _importRows) r.apply = !r.HasError;
                if (GUILayout.Button("Select none")) foreach (var r in _importRows) r.apply = false;
                if (GUILayout.Button("Apply selected")) ApplyImport();
            }

            foreach (var r in _importRows.Take(250))
            {
                using (new EditorGUILayout.VerticalScope(LocUI.CardStyle))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        r.apply = EditorGUILayout.Toggle(r.apply, GUILayout.Width(18));
                        EditorGUILayout.LabelField(r.key + "  [" + r.lang + "]", EditorStyles.boldLabel);
                    }
                    if (!string.IsNullOrEmpty(r.oldValue)) EditorGUILayout.LabelField("- " + r.oldValue, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.LabelField("+ " + r.newValue, EditorStyles.wordWrappedMiniLabel);
                    foreach (var i in r.issues)
                        EditorGUILayout.HelpBox(i.ToString(), i.severity == IssueSeverity.Error ? MessageType.Error : MessageType.Warning);
                }
            }
            if (_importRows.Count > 250) EditorGUILayout.LabelField("Showing first 250 changes.");

            DrawStringList("Unknown keys (not in any sheet)", _importUnknown);
            DrawStringList("Keys missing from the imported file", _importMissing);
            DrawStringList("Source text differs from current source", _importSourceChanged);
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
        CommitPendingSource();
        SaveDirty();
        var langs = _exportTranslations == ExportTranslations.SourceOnly
            ? new List<string>()
            : LocalizationSheetIO.StoredLanguages(_table).ToList();
        bool blankLangs = _exportTranslations == ExportTranslations.BlankColumns;
        string stamp = DateTime.Now.ToString("yyyyMMdd");
        int files = 0, rowsTotal = 0;

        foreach (var sheet in _table.SheetNames().ToList())
        {
            var entries = _table.entries.Where(e => e.sheet == sheet).ToList();
            if (delta) entries = entries.Where(e => LocalizationSheetIO.Status(e) != "frozen").ToList();
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
                if (delta) row.Add(string.IsNullOrEmpty(e.frozenHash) ? "new" : "changed");
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

    // ---------------------------------------------------------------- Import

    private void BuildImportDiff(string folder)
    {
        _importRows = new List<ImportRow>();
        _importUnknown.Clear();
        _importMissing.Clear();
        _importSourceChanged.Clear();

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
                if (srcCol >= 0 && srcCol < row.Count && row[srcCol] != entry.source)
                    _importSourceChanged.Add(key);

                foreach (var kv in langCols)
                {
                    if (kv.Key >= row.Count || string.IsNullOrEmpty(row[kv.Key])) continue;
                    string nv = row[kv.Key];
                    string ov = entry.GetValue(kv.Value);
                    if (nv == ov) continue;

                    var ir = new ImportRow { key = key, lang = kv.Value, oldValue = ov, newValue = nv, file = Path.GetFileName(file) };
                    ir.issues = LocalizationValidator.Validate(entry.source, nv, _settings, FontFor(kv.Value, null), entry.maxChars);
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
        int applied = 0;
        foreach (var r in _importRows.Where(x => x.apply))
        {
            if (!_table.TryGet(r.key, out var e)) continue;
            _table.AddLanguage(r.lang);
            e.values[r.lang] = r.newValue;
            MarkDirty(e);
            applied++;
        }
        foreach (var s in _table.SheetNames()) _dirtySheets.Add(s); // new language columns affect every sheet
        SaveDirty();
        _importRows = null;
        _filterDirty = true;
        _status = "Imported " + applied + " translation(s).";
    }

    // ================================================================ Freeze / Delta tab

    private Vector2 _freezeScroll;

    private void DrawFreezeTab()
    {
        LocUI.Section("String freeze");
        EditorGUILayout.HelpBox("Freeze after sending a table out. Edits to frozen source text stay possible but are flagged 'changed' and appear in the next delta sheet. New strings are 'new' and also go into the delta.", MessageType.Info);

        int fresh = _table.entries.Count(e => LocalizationSheetIO.Status(e) == "new");
        int changed = _table.entries.Count(e => LocalizationSheetIO.Status(e) == "changed");
        int frozen = _table.entries.Count(e => LocalizationSheetIO.Status(e) == "frozen");
        EditorGUILayout.LabelField($"{frozen} frozen | {changed} changed since freeze | {fresh} new");

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Freeze handoff")) FreezeHandoff();
            _exportFolder = EditorGUILayout.TextField(_exportFolder);
            if (GUILayout.Button("...", GUILayout.Width(30))) _exportFolder = EditorUtility.OpenFolderPanel("Delta export folder", _exportFolder, "");
            GUI.enabled = !string.IsNullOrEmpty(_exportFolder);
            if (GUILayout.Button("Export delta")) ExportSheets(_exportFolder, true);
            GUI.enabled = true;
        }
        _exportTranslations = (ExportTranslations)EditorGUILayout.Popup("Delta translations", (int)_exportTranslations, ExportTranslationsLabels);

        _freezeScroll = EditorGUILayout.BeginScrollView(_freezeScroll);
        foreach (var e in _table.entries.Where(e => LocalizationSheetIO.Status(e) != "frozen").Take(300))
            EditorGUILayout.LabelField(LocalizationSheetIO.Status(e).ToUpperInvariant() + "  " + e.key, EditorStyles.miniLabel);
        EditorGUILayout.EndScrollView();
    }

    private void FreezeHandoff()
    {
        CommitPendingSource();
        var sb = new StringBuilder();
        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        sb.AppendLine("# String freeze " + stamp).AppendLine();
        sb.AppendLine("| Sheet | Strings |").AppendLine("|---|---|");
        foreach (var s in _table.SheetNames()) sb.AppendLine("| " + s + " | " + _table.entries.Count(e => e.sheet == s) + " |");
        sb.AppendLine().AppendLine("| Key | Hash |").AppendLine("|---|---|");

        foreach (var e in _table.entries)
        {
            e.frozenHash = LocalizationTable.Hash(e.source);
            e.status = "frozen";
            sb.AppendLine("| " + e.key + " | " + e.frozenHash + " |");
        }

        LocalizationPaths.EnsureFolders();
        string rel = LocalizationPaths.Freeze + "/manifest_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".md";
        File.WriteAllText(LocalizationPaths.Abs(rel), sb.ToString(), new UTF8Encoding(false));
        LocalizationSheetIO.SaveAll(_table);
        _filterDirty = true;
        _status = "Froze " + _table.entries.Count + " strings. Manifest: " + rel;
    }
}
