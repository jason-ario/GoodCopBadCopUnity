using System;
using System.Collections.Generic;
using System.Text;

/// <summary>Minimal RFC-4180 style CSV reader/writer shared by the runtime table and the editor sheet IO.</summary>
public static class LocalizationCsv
{
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrEmpty(text)) return rows;

        int i = text[0] == '\uFEFF' ? 1 : 0;
        var row = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        bool any = false;

        for (; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') { inQuotes = true; any = true; }
            else if (c == ',') { row.Add(sb.ToString()); sb.Length = 0; any = true; }
            else if (c == '\r') { }
            else if (c == '\n')
            {
                if (any || row.Count > 0 || sb.Length > 0)
                {
                    row.Add(sb.ToString());
                    rows.Add(row);
                }
                row = new List<string>();
                sb.Length = 0;
                any = false;
            }
            else { sb.Append(c); any = true; }
        }

        if (any || row.Count > 0 || sb.Length > 0)
        {
            row.Add(sb.ToString());
            rows.Add(row);
        }
        return rows;
    }

    public static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        bool needs = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 || s[0] == ' ' || s[s.Length - 1] == ' ';
        return needs ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public static string Write(IEnumerable<IList<string>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            for (int i = 0; i < row.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(row[i]));
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

/// <summary>One localizable string: key, translator notes, English source and per-language values.</summary>
[Serializable]
public class LocEntry
{
    public string sheet = "";
    public string key = "";
    public string notes = "";
    public string source = "";
    public string status = "";
    public string frozenHash = "";
    public Dictionary<string, string> values = new Dictionary<string, string>();

    public string GetValue(string lang)
    {
        return values.TryGetValue(lang, out var v) ? v : "";
    }
}

/// <summary>In-memory model: Sheet -> Entry { key, notes, source, values, status }.</summary>
public class LocalizationTable
{
    public const string SourceLanguage = "en";
    public const string SourceColumn = "source_en";

    public readonly List<string> languages = new List<string>();
    public readonly List<LocEntry> entries = new List<LocEntry>();
    private readonly Dictionary<string, LocEntry> _byKey = new Dictionary<string, LocEntry>();

    public bool TryGet(string key, out LocEntry entry)
    {
        if (key == null) { entry = null; return false; }
        return _byKey.TryGetValue(key, out entry);
    }

    public LocEntry Add(LocEntry entry)
    {
        if (_byKey.TryGetValue(entry.key, out var existing)) return existing;
        _byKey[entry.key] = entry;
        entries.Add(entry);
        foreach (var lang in entry.values.Keys) AddLanguage(lang);
        return entry;
    }

    public void Remove(string key)
    {
        if (_byKey.TryGetValue(key, out var e))
        {
            _byKey.Remove(key);
            entries.Remove(e);
        }
    }

    public void AddLanguage(string lang)
    {
        if (!string.IsNullOrEmpty(lang) && lang != SourceLanguage && !languages.Contains(lang))
            languages.Add(lang);
    }

    public IEnumerable<string> SheetNames()
    {
        var seen = new HashSet<string>();
        foreach (var e in entries)
            if (seen.Add(e.sheet)) yield return e.sheet;
    }

    /// <summary>Stable FNV-1a 64 hash of the source text, used for freeze / delta tracking.</summary>
    public static string Hash(string s)
    {
        ulong h = 1469598103934665603UL;
        if (s != null)
            foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return h.ToString("x16");
    }

    // ---- Runtime format: sheet,key,source_en,<lang...> ----

    public string ToRuntimeCsv(IEnumerable<string> onlyLanguages = null)
    {
        var langs = onlyLanguages != null ? new List<string>(onlyLanguages) : new List<string>(languages);
        var rows = new List<IList<string>>();
        var header = new List<string> { "sheet", "key", SourceColumn };
        header.AddRange(langs);
        rows.Add(header);
        foreach (var e in entries)
        {
            var row = new List<string> { e.sheet, e.key, e.source };
            foreach (var l in langs) row.Add(e.GetValue(l));
            rows.Add(row);
        }
        return LocalizationCsv.Write(rows);
    }

    public static LocalizationTable FromRuntimeCsv(string text)
    {
        var table = new LocalizationTable();
        var rows = LocalizationCsv.Parse(text);
        if (rows.Count == 0) return table;

        var header = rows[0];
        int sheetCol = header.IndexOf("sheet");
        int keyCol = header.IndexOf("key");
        int srcCol = header.IndexOf(SourceColumn);
        if (keyCol < 0) return table;

        var langCols = new List<KeyValuePair<int, string>>();
        for (int c = 0; c < header.Count; c++)
        {
            if (c == sheetCol || c == keyCol || c == srcCol) continue;
            langCols.Add(new KeyValuePair<int, string>(c, header[c]));
            table.AddLanguage(header[c]);
        }

        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (keyCol >= row.Count || string.IsNullOrEmpty(row[keyCol])) continue;
            var e = new LocEntry
            {
                key = row[keyCol],
                sheet = sheetCol >= 0 && sheetCol < row.Count ? row[sheetCol] : "",
                source = srcCol >= 0 && srcCol < row.Count ? row[srcCol] : ""
            };
            foreach (var kv in langCols)
                if (kv.Key < row.Count && !string.IsNullOrEmpty(row[kv.Key]))
                    e.values[kv.Value] = row[kv.Key];
            table.Add(e);
        }
        return table;
    }
}
