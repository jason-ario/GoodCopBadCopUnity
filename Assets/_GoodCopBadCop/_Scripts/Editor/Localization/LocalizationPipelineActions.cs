using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bezi;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Scripted access to the Localization window's Scan / Apply / Delete / Build steps, so source-text passes can run without the UI.
/// Uses the same LocalizationScanner, LocalizationApply and LocalizationSheetIO code paths as the window.
/// </summary>
public static class LocalizationPipelineActions
{
    [BeziAction(
        "Run the full Localization scan (loaded scenes, prefabs, dialogue data assets, suspect objects, code strings). Reports SourceChanged items, " +
        "new items by binding kind, and deleted sheet rows (keys no scanned source produces any more, only where every recorded source was covered by the scan). " +
        "apply=true writes SourceChanged sources to the sheets and adds new items whose binding kind is in addNewKinds (comma list of TMP, SO, MB, CODE). " +
        "Deleted rows are only reported; delete them with DeleteLocalizationKeys.",
        IsReadOnly = false)]
    public static string ScanLocalization(bool apply = false, string addNewKinds = "CODE", int listMax = 150)
    {
        var settings = LocalizationSheetIO.LoadOrCreateSettings();
        var table = LocalizationSheetIO.LoadAll();
        var items = LocalizationScanner.Scan(settings, table, true, true, true, true, true);
        LocBindings.ClearGroupCache();

        var kinds = new HashSet<string>((addNewKinds ?? "").Split(',').Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0));
        var changed = items.Where(i => i.state == ScanState.SourceChanged && !i.runtime).ToList();
        var fresh = items.Where(i => i.state == ScanState.Unlocalized && !i.runtime).ToList();
        var runtime = items.Where(i => i.runtime).ToList();

        var stale = new HashSet<string>(LocalizationScanner.StaleCodeKeys(table));
        var orphans = items.Where(i => i.state == ScanState.Deleted).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("Scan: " + items.Count(i => i.state != ScanState.Deleted) + " candidates, " + changed.Count + " SourceChanged, " + fresh.Count + " new, " + runtime.Count + " runtime-filled (skipped), " + orphans.Count + " deleted row(s) of " + table.entries.Count + ".");
        sb.AppendLine("New by kind: " + string.Join(", ", fresh.GroupBy(i => i.binding.kind).Select(g => g.Key + "=" + g.Count())));

        sb.AppendLine("\n## SourceChanged");
        foreach (var i in changed.Take(listMax))
        {
            table.TryGet(i.key, out var e);
            sb.AppendLine(i.sheet + " | " + i.key + "\n  OLD: " + Flat(e != null ? e.source : "") + "\n  NEW: " + Flat(i.text));
        }

        sb.AppendLine("\n## New (" + string.Join(",", kinds) + " kinds are added on apply)");
        foreach (var i in fresh.Where(i => kinds.Contains(i.binding.kind)).Take(listMax))
            sb.AppendLine(i.binding.kind + " | " + i.sheet + " | " + i.key + " | " + Flat(i.text));

        sb.AppendLine("\n## Deleted rows (no scanned source produces them; UNSURE = no recorded source)");
        foreach (var e in orphans.Take(listMax))
            sb.AppendLine((stale.Contains(e.key) ? "STALE_CODE" : e.selected ? "DELETED" : "UNSURE") + " | " + e.sheet + " | " + e.key + " | " + Flat(e.text) + "\n  " + Flat(e.deletedReason));

        if (apply)
        {
            foreach (var i in items)
                i.selected = !i.runtime && i.state != ScanState.Deleted && (i.state == ScanState.SourceChanged || (i.state == ScanState.Unlocalized && kinds.Contains(i.binding.kind)));
            int n = LocalizationScanner.Apply(items, table);
            sb.AppendLine("\nApplied " + n + " item(s).");
        }
        else sb.AppendLine("\nDry run, nothing written.");
        return sb.ToString();
    }

    [BeziAction(
        "Find TMP labels whose text is filled by game code at runtime (loaded scenes + prefabs) and list their placeholder text. " +
        "apply=true deletes sheet rows used only by those labels and clears their LocalizedText key (the component stays for language fonts). " +
        "Overrides live in LocalizationSettings.runtimeTextLabels / staticTextLabels.",
        IsReadOnly = false)]
    public static string RemoveRuntimePlaceholders(bool apply = false, int listMax = 200)
    {
        var settings = LocalizationSheetIO.LoadOrCreateSettings();
        var table = LocalizationSheetIO.LoadAll();
        var items = LocalizationScanner.Scan(settings, table, true, true, false, false, false);
        var runtime = items.Where(i => i.runtime).ToList();
        var keys = LocalizationScanner.RuntimePlaceholderKeys(items, table);
        var labels = LocalizationScanner.RuntimeBoundLabels(items);

        var sb = new StringBuilder();
        sb.AppendLine(runtime.Count + " runtime-filled label(s); " + keys.Count + " sheet row(s) to delete; " + labels.Count + " LocalizedText key(s) to clear.");
        foreach (var i in runtime.Take(listMax))
            sb.AppendLine((keys.Contains(i.key) ? "ROW " : "    ") + i.binding.assetPath + " > " + i.binding.objectPath + " | " + i.runtimeReason + " | " + Flat(i.text));

        if (apply)
        {
            var set = new HashSet<string>(keys);
            var sheets = new HashSet<string>();
            foreach (var k in set)
                if (table.TryGet(k, out var e)) { sheets.Add(e.sheet); table.Remove(k); }
            LocBindings.RemoveKeys(set);
            LocBindings.Save();
            LocalizationSheetIO.SaveSheets(table, sheets);
            int cleared = LocalizationApply.ClearLocalizedTextKeys(labels);
            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            sb.AppendLine("\nDeleted " + set.Count + " row(s), cleared " + cleared + " key(s). Run BuildLocalizationRuntime next. Reload the Localization window if it is open.");
        }
        else sb.AppendLine("\nDry run, nothing written.");
        return sb.ToString();
    }

    [BeziAction("Delete rows from the localization sheets by key (and drop their source bindings). Sheets left empty keep only their header row.")]
    public static string DeleteLocalizationKeys(string[] keys)
    {
        if (keys == null || keys.Length == 0) return "No keys given.";
        var table = LocalizationSheetIO.LoadAll();
        var set = new HashSet<string>(keys);
        var sheets = new HashSet<string>();
        int removed = 0;
        foreach (var k in set)
            if (table.TryGet(k, out var e)) { sheets.Add(e.sheet); table.Remove(k); removed++; }
        LocBindings.RemoveKeys(set);
        LocBindings.Save();
        LocalizationSheetIO.SaveSheets(table, sheets);
        return "Deleted " + removed + " of " + set.Count + " key(s) from " + sheets.Count + " sheet(s): " + string.Join(", ", sheets);
    }

    [BeziAction(
        "Rewrite the English source of existing localization keys at their origin (ScriptableObject field, prefab/scene component field or TMP text, via " +
        "the key's bindings) and update source_en in the sheets. keys and texts are parallel arrays. Not for code.* keys (edit the C# literal instead).")]
    public static string SetLocalizationSources(string[] keys, string[] texts)
    {
        if (keys == null || texts == null || keys.Length != texts.Length) return "Error: keys and texts must be parallel arrays of equal length.";
        var table = LocalizationSheetIO.LoadAll();
        var sheets = new HashSet<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i].StartsWith("code.")) { sb.AppendLine("SKIP code key " + keys[i]); continue; }
            int origins = LocalizationApply.WriteSource(keys[i], texts[i]);
            if (table.TryGet(keys[i], out var e) && origins > 0) { e.source = texts[i]; sheets.Add(e.sheet); }
            sb.AppendLine((origins > 0 ? "OK " + origins : "NO_ORIGIN") + " | " + keys[i] + " | " + Flat(texts[i]));
        }
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (scene.isLoaded && scene.isDirty) EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
        if (sheets.Count > 0) LocalizationSheetIO.SaveSheets(table, sheets);
        return sb.ToString();
    }

    [BeziAction("Set the translator notes column for localization keys. keys and notes are parallel arrays; append=true adds the note after the existing notes with ' | '.")]
    public static string SetLocalizationNotes(string[] keys, string[] notes, bool append = true)
    {
        if (keys == null || notes == null || keys.Length != notes.Length) return "Error: keys and notes must be parallel arrays of equal length.";
        var table = LocalizationSheetIO.LoadAll();
        var sheets = new HashSet<string>();
        int n = 0;
        var missing = new List<string>();
        for (int i = 0; i < keys.Length; i++)
        {
            if (!table.TryGet(keys[i], out var e)) { missing.Add(keys[i]); continue; }
            if (append && !string.IsNullOrEmpty(e.notes))
            {
                if (e.notes.Contains(notes[i])) continue;
                e.notes = e.notes + " | " + notes[i];
            }
            else e.notes = notes[i];
            sheets.Add(e.sheet);
            n++;
        }
        if (sheets.Count > 0) LocalizationSheetIO.SaveSheets(table, sheets);
        return "Updated notes on " + n + " key(s)." + (missing.Count > 0 ? " Missing: " + string.Join(", ", missing) : "");
    }

    [BeziAction("Find localization sheet rows whose source_en (or key when inKey=true) matches a .NET regex. Optional sheet filter (substring). Read only.", IsReadOnly = true)]
    public static string FindLocalizationRows(string pattern, string sheet = "", bool inKey = false, int max = 200)
    {
        var rx = new System.Text.RegularExpressions.Regex(pattern);
        var table = LocalizationSheetIO.LoadAll();
        var hits = table.entries.Where(e => (string.IsNullOrEmpty(sheet) || e.sheet.Contains(sheet)) && rx.IsMatch(inKey ? e.key : e.source ?? "")).ToList();
        var sb = new StringBuilder(hits.Count + " match(es)\n");
        foreach (var e in hits.Take(max)) sb.AppendLine(e.sheet + " | " + e.key + " | " + Flat(e.source));
        return sb.ToString();
    }

    [BeziAction("Run the source_en style lint (whitespace, ellipsis/curly quotes, confirmed terminology, US spelling) over the sheets. Optional code filter (whitespace, typography, term, spelling) and sheet substring. Read only.", IsReadOnly = true)]
    public static string LintLocalizationSources(string code = "", string sheet = "", int max = 300)
    {
        var table = LocalizationSheetIO.LoadAll();
        var hits = LocalizationValidator.LintAll(table.entries.Where(e => string.IsNullOrEmpty(sheet) || e.sheet.Contains(sheet)))
            .Where(h => string.IsNullOrEmpty(code) || h.Contains("[" + code + "]")).ToList();
        var byCode = hits.GroupBy(h => h.Substring(h.IndexOf('[') , h.IndexOf(']') - h.IndexOf('[') + 1)).Select(g => g.Key + "=" + g.Count());
        return hits.Count + " warning(s) " + string.Join(", ", byCode) + "\n" + string.Join("\n", hits.Take(max));
    }

    [BeziAction("Rebuild Resources/Localization/strings.txt from the sheets (the window's Build runtime table). preserveRuntimeTranslations=true " +
                "keeps translations that exist only in the current strings.txt (test translations not in the sheets) for rows whose English is unchanged.")]
    public static string BuildLocalizationRuntime(bool preserveRuntimeTranslations = true)
    {
        string abs = LocalizationPaths.Abs(LocalizationPaths.RuntimeStrings);
        string previous = preserveRuntimeTranslations && System.IO.File.Exists(abs) ? System.IO.File.ReadAllText(abs, Encoding.UTF8) : null;
        var table = LocalizationSheetIO.LoadAll();
        LocalizationSheetIO.BuildRuntime(table);
        string merged = previous != null ? MergeInto(abs, previous) : "";
        return "Built runtime table: " + table.entries.Count + " entries, languages: " + string.Join(", ", LocalizationSheetIO.StoredLanguages(table)) + ". " + merged;
    }

    [BeziAction("Fill empty translation cells in Resources/Localization/strings.txt from another runtime CSV (absolute path, e.g. a git copy). " +
                "Matches by key when the English is identical, otherwise by identical English from any row. Never overwrites existing cells.")]
    public static string MergeRuntimeTranslations(string fromAbsolutePath)
    {
        if (!System.IO.File.Exists(fromAbsolutePath)) return "Error: file not found: " + fromAbsolutePath;
        return MergeInto(LocalizationPaths.Abs(LocalizationPaths.RuntimeStrings), System.IO.File.ReadAllText(fromAbsolutePath, Encoding.UTF8));
    }

    private static string MergeInto(string targetAbs, string fromCsv)
    {
        var current = LocalizationTable.FromRuntimeCsv(System.IO.File.ReadAllText(targetAbs, Encoding.UTF8));
        var old = LocalizationTable.FromRuntimeCsv(fromCsv);
        string Norm(string s) => (s ?? "").Replace("\r\n", "\n");
        var bySource = new Dictionary<string, LocEntry>();
        foreach (var e in old.entries)
            if (e.values.Count > 0 && !bySource.ContainsKey(Norm(e.source))) bySource[Norm(e.source)] = e;

        int byKey = 0, bySrc = 0, stale = 0;
        foreach (var lang in old.languages) current.AddLanguage(lang);
        foreach (var e in current.entries)
        {
            LocEntry from = null;
            bool keyMatch = old.TryGet(e.key, out var k) && Norm(k.source) == Norm(e.source);
            if (keyMatch) from = k;
            else
            {
                if (k != null && k.values.Count > 0) stale++;
                bySource.TryGetValue(Norm(e.source), out from);
            }
            if (from == null) continue;
            bool any = false;
            foreach (var kv in from.values)
                if (!e.values.ContainsKey(kv.Key) || string.IsNullOrEmpty(e.values[kv.Key])) { e.values[kv.Key] = kv.Value; any = true; }
            if (any) { if (keyMatch) byKey++; else bySrc++; }
        }

        System.IO.File.WriteAllText(targetAbs, current.ToRuntimeCsv(current.languages), new UTF8Encoding(false));
        AssetDatabase.ImportAsset(LocalizationPaths.RuntimeStrings);
        if (UnityEngine.Application.isPlaying) Loc.Reload();
        return "Merged translations into " + (byKey + bySrc) + " row(s) (" + byKey + " by key, " + bySrc + " by identical English); " +
               stale + " row(s) whose English changed were left untranslated.";
    }

    private static string Flat(string s)
    {
        return (s ?? "").Replace("\r", "\\r").Replace("\n", "\\n");
    }
}
