using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine.SceneManagement;

/// <summary>
/// Deleted rows: sheet entries that no scanned source produces any more. A row is only flagged when every source recorded
/// for it (LocBindings) was covered by this scan, so unticked categories and unloaded scenes never look deleted.
/// Keys that game code names directly (Loc.Get("doc.wrong_name"), "doc.sex." + value) always count as live.
/// </summary>
public static partial class LocalizationScanner
{
    public class DeletedScope
    {
        public bool scenes, prefabs, scriptables, suspectObjects, code;
        public Func<HashSet<string>> used;
        public bool All => scenes && prefabs && scriptables && suspectObjects && code;
    }

    // "doc.wrong_name", "doc.sex." + x, $"date.month.{m}"
    private static readonly Regex KeyLiteral = new Regex(@"""([A-Za-z][\w\-]*(?:\.[\w\-]*)+)(?=[""{])", RegexOptions.Compiled);

    private static Dictionary<string, string> _boundTmpKeys;

    /// <summary>Sheet key recorded for a TMP label (asset + object path) by an earlier apply, or null.</summary>
    private static string BoundTmpKey(string assetPath, string objectPath, LocalizationTable table)
    {
        if (_boundTmpKeys == null)
        {
            _boundTmpKeys = new Dictionary<string, string>();
            foreach (var b in LocBindings.All)
                if (b.kind == "TMP" && !string.IsNullOrEmpty(b.key) && table.TryGet(b.key, out _))
                    _boundTmpKeys[b.assetPath + "|" + b.objectPath] = b.key;
        }
        return _boundTmpKeys.TryGetValue(assetPath + "|" + objectPath, out var k) ? k : null;
    }

    private static void AddDeletedItems(List<ScanItem> items, LocalizationTable table, LocalizationSettings settings, DeletedScope scope)
    {
        var live = new HashSet<string>(items.Select(i => i.key));
        foreach (var i in items) if (i.reuseKey != null) live.Add(i.reuseKey);

        var literals = CodeKeyLiterals();
        var exact = new HashSet<string>(literals.Where(l => !l.EndsWith(".")));
        var prefixes = literals.Where(l => l.EndsWith(".") && l.Count(c => c == '.') >= 2).ToList();

        var bindings = LocBindings.All.Where(b => !string.IsNullOrEmpty(b.key))
            .GroupBy(b => b.key).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var e in table.entries)
        {
            if (live.Contains(e.key) || exact.Contains(e.key) || prefixes.Any(p => e.key.StartsWith(p, StringComparison.Ordinal))) continue;

            bindings.TryGetValue(e.key, out var bs);
            string reason;
            bool sure = true;
            if (bs == null || bs.Count == 0)
            {
                if (!scope.All) continue;
                reason = "No recorded source (added by hand or by an older tool) and nothing in the scan produces it. Check before removing.";
                sure = false;
            }
            else
            {
                if (!bs.All(b => Covered(b, scope))) continue;
                reason = string.Join("\n", bs.Select(b => DeletedReason(b, settings, scope)).Distinct().Take(3));
            }

            int translations = e.values.Count(v => !string.IsNullOrEmpty(v.Value));
            if (translations > 0) reason += "\nHas " + translations + " translation(s) that will be lost.";

            var first = bs != null && bs.Count > 0 ? bs[0] : new LocBinding { key = e.key, kind = "", assetPath = "" };
            items.Add(new ScanItem
            {
                key = e.key,
                sheet = e.sheet,
                text = e.source ?? "",
                notes = e.notes,
                category = "Deleted",
                context = "",
                group = LocBindings.GroupOf(e.key),
                state = ScanState.Deleted,
                binding = first,
                deletedReason = reason,
                selected = sure
            });
        }
    }

    /// <summary>True when this scan would have produced the binding's text if its source still existed.</summary>
    private static bool Covered(LocBinding b, DeletedScope scope)
    {
        if (!AssetExists(b.assetPath)) return true;
        bool suspectOnly = b.kind == "MB" && b.group == LocGroups.SuspectObjects;
        switch (b.kind)
        {
            case "CODE": return scope.code;
            case "SO": return scope.scriptables;
            case "TMP":
            case "MB":
                if (suspectOnly && !scope.suspectObjects) return false;
                if (b.assetPath.EndsWith(".prefab")) return scope.prefabs;
                if (b.assetPath.EndsWith(".unity")) return scope.scenes && SceneManager.GetSceneByPath(b.assetPath).isLoaded;
                return false;
            default: return false;
        }
    }

    private static string DeletedReason(LocBinding b, LocalizationSettings settings, DeletedScope scope)
    {
        string file = Path.GetFileName(b.assetPath ?? "");
        if (!AssetExists(b.assetPath)) return "Source asset deleted: " + b.assetPath;
        bool prefab = b.assetPath.EndsWith(".prefab");
        if ((prefab && settings.onlyUsedPrefabs) || (b.kind == "SO" && settings.onlyUsedDialogueAssets))
            if (!scope.used().Contains(b.assetPath)) return file + " is no longer used in game (not referenced by an enabled build scene).";
        switch (b.kind)
        {
            case "CODE": return "Loc.T literal changed or removed (last seen in " + file + ").";
            case "SO": return "Field text removed, changed or excluded in " + file + (string.IsNullOrEmpty(b.propertyPath) ? "" : " > " + b.propertyPath) + ".";
            default: return "No longer produced by " + file + (string.IsNullOrEmpty(b.objectPath) ? "" : " > " + b.objectPath)
                            + " (object or component removed, text cleared, or the label now uses a different key).";
        }
    }

    private static bool AssetExists(string assetPath)
    {
        return !string.IsNullOrEmpty(assetPath) && File.Exists(LocalizationPaths.Abs(assetPath));
    }

    /// <summary>Dotted string literals and prefixes in game scripts (Editor excluded) that may be localization keys.</summary>
    private static HashSet<string> CodeKeyLiterals()
    {
        var result = new HashSet<string>();
        foreach (var src in TypeSources().Values.Distinct())
            foreach (Match m in KeyLiteral.Matches(src))
                result.Add(m.Groups[1].Value);
        return result;
    }
}
