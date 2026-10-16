using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class LocalizationPaths
{
    public const string Root = "Assets/_GoodCopBadCop/Localization";
    public const string Sheets = Root + "/Sheets";
    public const string Refs = Root + "/Refs";
    public const string Findings = Root + "/Findings";
    public const string Freeze = Root + "/Freeze";
    public const string Bindings = Root + "/Bindings.json";
    public const string RuntimeDir = "Assets/_GoodCopBadCop/Resources/Localization";
    public const string RuntimeStrings = RuntimeDir + "/strings.txt";
    public const string SettingsAsset = RuntimeDir + "/LocalizationSettings.asset";

    public static string Abs(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, assetPath);
    }

    public static void EnsureFolders()
    {
        foreach (var p in new[] { Sheets, Refs, Findings, Freeze, RuntimeDir })
            Directory.CreateDirectory(Abs(p));
    }

    public static string SheetPath(string sheet) { return Sheets + "/" + sheet + ".csv"; }
}

/// <summary>CSV per sheet: key, notes, source_en, language columns..., status, frozen_hash.</summary>
public static class LocalizationSheetIO
{
    private static readonly HashSet<string> FixedColumns = new HashSet<string> { "key", "notes", "source_en", "status", "frozen_hash" };

    public static LocalizationSettings LoadOrCreateSettings()
    {
        LocalizationPaths.EnsureFolders();
        var s = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(LocalizationPaths.SettingsAsset);
        if (s == null)
        {
            s = ScriptableObject.CreateInstance<LocalizationSettings>();
            AssetDatabase.CreateAsset(s, LocalizationPaths.SettingsAsset);
            AssetDatabase.SaveAssets();
        }
        return s;
    }

    public static LocalizationTable LoadAll()
    {
        LocalizationPaths.EnsureFolders();
        var table = new LocalizationTable();
        string dir = LocalizationPaths.Abs(LocalizationPaths.Sheets);
        foreach (var file in Directory.GetFiles(dir, "*.csv").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            LoadSheetFile(file, Path.GetFileNameWithoutExtension(file), table);

        var settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(LocalizationPaths.SettingsAsset);
        if (settings != null)
            foreach (var l in settings.languages)
                if (l != null && !PseudoLocalizer.IsPseudo(l.code)) table.AddLanguage(l.code);
        return table;
    }

    /// <summary>Parses one sheet CSV (absolute path) into <paramref name="table"/>. Returns the number of entries read.</summary>
    public static int LoadSheetFile(string absolutePath, string sheetName, LocalizationTable table)
    {
        var rows = LocalizationCsv.Parse(File.ReadAllText(absolutePath, Encoding.UTF8));
        return ReadRows(rows, sheetName, table);
    }

    public static int ReadRows(List<List<string>> rows, string sheetName, LocalizationTable table)
    {
        if (rows.Count == 0) return 0;
        var header = rows[0];
        int keyCol = header.IndexOf("key");
        if (keyCol < 0) return 0;
        int notesCol = header.IndexOf("notes");
        int srcCol = header.IndexOf("source_en");
        int statusCol = header.IndexOf("status");
        int hashCol = header.IndexOf("frozen_hash");

        var langCols = new List<KeyValuePair<int, string>>();
        for (int c = 0; c < header.Count; c++)
            if (!FixedColumns.Contains(header[c]) && !string.IsNullOrEmpty(header[c]))
            {
                langCols.Add(new KeyValuePair<int, string>(c, header[c]));
                table.AddLanguage(header[c]);
            }

        int count = 0;
        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string Cell(int i) { return i >= 0 && i < row.Count ? row[i] : ""; }
            string key = Cell(keyCol);
            if (string.IsNullOrEmpty(key)) continue;

            var e = new LocEntry
            {
                sheet = sheetName,
                key = key,
                notes = Cell(notesCol),
                source = Cell(srcCol),
                status = Cell(statusCol),
                frozenHash = Cell(hashCol)
            };
            foreach (var kv in langCols)
            {
                string v = Cell(kv.Key);
                if (!string.IsNullOrEmpty(v)) e.values[kv.Value] = v;
            }
            table.Add(e);
            count++;
        }
        return count;
    }

    public static string Status(LocEntry e)
    {
        if (string.IsNullOrEmpty(e.frozenHash)) return "new";
        return e.frozenHash == LocalizationTable.Hash(e.source) ? "frozen" : "changed";
    }

    public static List<IList<string>> BuildRows(IEnumerable<LocEntry> entries, IEnumerable<string> languages, bool includeStatus)
    {
        var langs = languages.ToList();
        var rows = new List<IList<string>>();
        var header = new List<string> { "key", "notes", "source_en" };
        header.AddRange(langs);
        if (includeStatus) { header.Add("status"); header.Add("frozen_hash"); }
        rows.Add(header);
        foreach (var e in entries)
        {
            var row = new List<string> { e.key, e.notes, e.source };
            foreach (var l in langs) row.Add(e.GetValue(l));
            if (includeStatus) { row.Add(Status(e)); row.Add(e.frozenHash); }
            rows.Add(row);
        }
        return rows;
    }

    public static IEnumerable<string> StoredLanguages(LocalizationTable table)
    {
        return table.languages.Where(l => !PseudoLocalizer.IsPseudo(l));
    }

    public static void SaveSheet(LocalizationTable table, string sheet)
    {
        LocalizationPaths.EnsureFolders();
        var entries = table.entries.Where(e => e.sheet == sheet).ToList();
        string csv = LocalizationCsv.Write(BuildRows(entries, StoredLanguages(table), true));
        File.WriteAllText(LocalizationPaths.Abs(LocalizationPaths.SheetPath(sheet)), csv, new UTF8Encoding(false));
    }

    public static void SaveSheets(LocalizationTable table, IEnumerable<string> sheets)
    {
        foreach (var s in sheets.Distinct()) SaveSheet(table, s);
        AssetDatabase.Refresh();
    }

    public static void SaveAll(LocalizationTable table)
    {
        SaveSheets(table, table.SheetNames().ToList());
    }

    /// <summary>Writes the merged runtime table to Resources/Localization/strings.txt. Generated, never hand edited.</summary>
    public static void BuildRuntime(LocalizationTable table)
    {
        LocalizationPaths.EnsureFolders();
        string csv = table.ToRuntimeCsv(StoredLanguages(table));
        File.WriteAllText(LocalizationPaths.Abs(LocalizationPaths.RuntimeStrings), csv, new UTF8Encoding(false));
        AssetDatabase.ImportAsset(LocalizationPaths.RuntimeStrings);
        AssetDatabase.Refresh();
        if (Application.isPlaying) Loc.Reload();
    }

    public static void WriteHandoff(string absolutePath, string csv)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
        File.WriteAllText(absolutePath, csv, new UTF8Encoding(true));
    }
}

[Serializable]
public class LocBinding
{
    public string key;
    public string kind;          // "TMP", "SO" or "MB" (MonoBehaviour field on a scene / prefab object)
    public string assetPath;     // scene, prefab or ScriptableObject asset
    public string objectPath;    // hierarchy path inside a scene or prefab (TMP and MB)
    public string propertyPath;  // serialized property path (SO and MB)
    public string componentType; // full type name of the component (MB only)
    public int componentIndex;   // index among components of that type on the object (MB only)
    public string category;
    public string context;
    public string group;         // see LocGroups
}

/// <summary>Source groups used by the Scan and Strings filters, mainly to separate the three places dialogue lives.</summary>
public static class LocGroups
{
    public const string SuspectObjects = "Suspect objects";
    public const string SuspectData = "Suspect data";
    public const string ConversationData = "Conversation data";
    public const string Other = "Other";

    public static readonly string[] DialogueGroups = { SuspectObjects, SuspectData, ConversationData };

    // Filter popup options.
    public const string FilterAll = "(all sources)";
    public const string FilterDialogue = "Dialogue (all three)";
    public const string FilterNonDialogue = "Non-dialogue";

    public static readonly string[] FilterOptions =
    {
        FilterAll, FilterDialogue, SuspectObjects, SuspectData, ConversationData, FilterNonDialogue
    };

    public static bool IsDialogue(string group) { return Array.IndexOf(DialogueGroups, group) >= 0; }

    public static bool Matches(string filter, string group)
    {
        switch (filter)
        {
            case FilterAll: case null: case "": return true;
            case FilterDialogue: return IsDialogue(group);
            case FilterNonDialogue: return !IsDialogue(group);
            default: return filter == group;
        }
    }

    public static string ForAssetType(Type t)
    {
        if (t == null) return Other;
        if (t == typeof(SuspectData)) return SuspectData;
        if (t == typeof(ScriptedDialogue)) return ConversationData;
        return Other;
    }
}

[Serializable]
public class LocBindingFile
{
    public List<LocBinding> items = new List<LocBinding>();
}

/// <summary>Editor-only map from key to the asset field it came from, used for write-back and "ping source".</summary>
public static class LocBindings
{
    private static LocBindingFile _file;

    public static List<LocBinding> All
    {
        get
        {
            if (_file == null) Load();
            return _file.items;
        }
    }

    public static void Load()
    {
        string abs = LocalizationPaths.Abs(LocalizationPaths.Bindings);
        _file = File.Exists(abs) ? JsonUtility.FromJson<LocBindingFile>(File.ReadAllText(abs)) : null;
        if (_file == null) _file = new LocBindingFile();
        GroupCache.Clear();
    }

    public static void Save()
    {
        if (_file == null) return;
        LocalizationPaths.EnsureFolders();
        File.WriteAllText(LocalizationPaths.Abs(LocalizationPaths.Bindings), JsonUtility.ToJson(_file, true));
        AssetDatabase.Refresh();
    }

    public static List<LocBinding> For(string key)
    {
        return All.Where(b => b.key == key).ToList();
    }

    public static int RemoveKeys(ICollection<string> keys)
    {
        int n = All.RemoveAll(b => keys.Contains(b.key));
        GroupCache.Clear();
        return n;
    }

    public static void Upsert(LocBinding b)
    {
        var list = All;
        int i = list.FindIndex(x => x.key == b.key && x.assetPath == b.assetPath && x.objectPath == b.objectPath
                                    && x.propertyPath == b.propertyPath && x.componentType == b.componentType);
        if (i >= 0) list[i] = b; else list.Add(b);
    }

    private static readonly Dictionary<string, string> GroupCache = new Dictionary<string, string>();

    /// <summary>Source group of a key. Falls back to the asset type for bindings saved before groups existed.</summary>
    public static string GroupOf(string key)
    {
        if (GroupCache.TryGetValue(key, out var cached)) return cached;
        var b = All.FirstOrDefault(x => x.key == key);
        string g = LocGroups.Other;
        if (b != null)
        {
            if (!string.IsNullOrEmpty(b.group)) g = b.group;
            else if (b.kind == "SO") g = LocGroups.ForAssetType(AssetDatabase.GetMainAssetTypeAtPath(b.assetPath));
        }
        GroupCache[key] = g;
        return g;
    }

    public static void ClearGroupCache() { GroupCache.Clear(); }
}
