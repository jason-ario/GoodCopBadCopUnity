using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum ScanState { Unlocalized, Localized, SourceChanged }

public class ScanItem
{
    public bool selected;
    public string sheet;
    public string key;
    public string text;
    public string notes;
    public string category;
    public string context;
    public ScanState state;
    public LocBinding binding;

    /// <summary>Key of an existing item with identical text AND context. Reuse is offered, never automatic.</summary>
    public string reuseKey;
    public bool reuse;
    public string group = LocGroups.Other;
}

/// <summary>Finds every candidate string in loaded scenes, prefabs and ScriptableObjects and proposes a sheet and key.</summary>
public static class LocalizationScanner
{
    private const string ProjectRoot = "Assets/_GoodCopBadCop";

    public static List<ScanItem> Scan(LocalizationSettings settings, LocalizationTable table, bool scenes, bool prefabs, bool scriptables, bool suspectObjects = true)
    {
        var items = new List<ScanItem>();
        GameScriptCache.Clear();
        var deny = new HashSet<string>(settings.fieldDenyList ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        try
        {
            if (scenes)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded || string.IsNullOrEmpty(scene.path)) continue;
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        ScanHierarchy(root, scene.path, table, items);
                        if (suspectObjects) ScanSuspectComponents(root, scene.path, deny, table, items);
                    }
                }
            }

            if (prefabs)
            {
                var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (IsExcluded(path)) continue;
                    if (i % 25 == 0 && EditorUtility.DisplayCancelableProgressBar("Localization scan", path, (float)i / guids.Length * 0.6f)) break;
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go == null) continue;
                    ScanHierarchy(go, path, table, items);
                    if (suspectObjects) ScanSuspectComponents(go, path, deny, table, items);
                }
            }

            if (scriptables)
            {
                var guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { ProjectRoot });
                var assembly = typeof(ScriptedDialogue).Assembly;
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (IsExcluded(path) || !path.EndsWith(".asset")) continue;
                    if (i % 25 == 0 && EditorUtility.DisplayCancelableProgressBar("Localization scan", path, 0.6f + (float)i / guids.Length * 0.4f)) break;
                    var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                    if (obj == null || obj.GetType().Assembly != assembly || obj is LocalizationSettings || !IsGameScript(obj)) continue;
                    if (!IsDialogueAssetType(obj.GetType(), settings)) continue;
                    ScanScriptable(obj, path, deny, settings, table, items);
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        AssignUniqueKeys(items, table);
        MarkDuplicates(items, table);
        return items;
    }

    private static bool IsExcluded(string path)
    {
        return path.StartsWith("Packages/") || path.Contains("/TextMesh Pro/") || path.Contains("/Localization/")
            || path.Contains("/Plugins/") || path.Contains("/Editor/");
    }

    // ---- TMP discovery ----

    private static void ScanHierarchy(GameObject root, string assetPath, LocalizationTable table, List<ScanItem> items)
    {
        string assetName = Path.GetFileNameWithoutExtension(assetPath);
        foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (PrefabUtility.IsPartOfPrefabInstance(tmp)) continue; // the source prefab is scanned on its own
            string text = tmp.text;
            if (!LooksLikeCopy(text)) continue;

            string objPath = LocalizationApply.ObjectPathOf(tmp.transform);
            string category = CategoryOf(tmp);
            string sheet = SheetFor(assetPath, objPath);
            var existing = tmp.GetComponent<LocalizedText>();

            var item = new ScanItem
            {
                sheet = sheet,
                text = text,
                category = category,
                context = sheet + "|" + category + "|" + (tmp.transform.parent != null ? tmp.transform.parent.name : ""),
                notes = category + " | " + assetName + " > " + objPath,
                binding = new LocBinding { kind = "TMP", assetPath = assetPath, objectPath = objPath, category = category }
            };

            if (existing != null && !string.IsNullOrEmpty(existing.key))
            {
                item.key = existing.key;
                item.state = table.TryGet(existing.key, out var e) && e.source != text ? ScanState.SourceChanged : ScanState.Localized;
            }
            else
            {
                item.key = SheetPrefix(sheet) + "." + LocKeys.Sanitize(assetName) + "." + KeyTail(objPath) + ".text";
                item.state = ScanState.Unlocalized;
                item.selected = true;
            }
            item.binding.key = item.key;
            item.binding.context = item.context;
            items.Add(item);
        }
    }

    private static string KeyTail(string objPath)
    {
        var parts = objPath.Split('/');
        var tail = parts.Skip(Math.Max(0, parts.Length - 3));
        return LocKeys.Sanitize(string.Join(".", tail).Replace('#', '_'));
    }

    private static string CategoryOf(TMP_Text tmp)
    {
        string n = (tmp.gameObject.name + "/" + (tmp.transform.parent != null ? tmp.transform.parent.name : "")).ToLowerInvariant();
        if (tmp.GetComponentInParent<Button>(true) != null || n.Contains("button") || n.Contains("btn")) return "Button";
        if (n.Contains("subtitle")) return "Subtitle";
        if (n.Contains("tooltip") || n.Contains("hint")) return "Tooltip";
        if (n.Contains("title") || n.Contains("header") || n.Contains("heading")) return "Title";
        if (n.Contains("desc") || n.Contains("body") || n.Contains("paragraph")) return "Body";
        return "Label";
    }

    private static bool LooksLikeCopy(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text == "New Text") return false;
        bool letter = false;
        foreach (char c in text) if (char.IsLetter(c)) { letter = true; break; }
        if (!letter) return false;

        var segs = PseudoTokenizer.Tokenize(text.Trim());
        if (segs.Count == 1 && segs[0].kind == PseudoSegmentKind.Technical) return false;
        return true;
    }

    // ---- Suspect character objects: dialogue coded in components (SuspectWorldDialogue, DrunkBehaviour, ...) ----

    private static readonly Dictionary<Type, bool> GameScriptCache = new Dictionary<Type, bool>();

    /// <summary>
    /// True only for scripts that live under Assets/_GoodCopBadCop. Third-party plugins (Look Animator, Legs Animator, Obi, ...)
    /// compile into the same assembly, so the script's file location is the reliable check.
    /// </summary>
    private static bool IsGameScript(UnityEngine.Object instance)
    {
        var type = instance.GetType();
        if (GameScriptCache.TryGetValue(type, out bool cached)) return cached;

        string path = "";
        MonoScript script = null;
        if (instance is MonoBehaviour mb) script = MonoScript.FromMonoBehaviour(mb);
        else if (instance is ScriptableObject so) script = MonoScript.FromScriptableObject(so);
        if (script != null) path = AssetDatabase.GetAssetPath(script);

        bool result = path.StartsWith(ProjectRoot + "/") && !path.Contains("/Editor/");
        GameScriptCache[type] = result;
        return result;
    }

    private static bool IsIdentifierField(string fieldName, HashSet<string> deny)
    {
        if (deny.Contains(fieldName)) return true;
        string n = fieldName.ToLowerInvariant();
        return n.Contains("trigger") || n.Contains("animation") || n.Contains("animator") || n.EndsWith("id") && n.Length <= 4;
    }

    private static void ScanSuspectComponents(GameObject root, string assetPath, HashSet<string> deny, LocalizationTable table, List<ScanItem> items)
    {
        var assembly = typeof(ScriptedDialogue).Assembly;
        string assetName = Path.GetFileNameWithoutExtension(assetPath);

        foreach (var comp in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (comp == null || comp is LocalizedText || comp.GetType().Assembly != assembly || !IsGameScript(comp)) continue;
            if (PrefabUtility.IsPartOfPrefabInstance(comp)) continue; // the source prefab is scanned on its own
            var suspect = comp.GetComponentInParent<SuspectCharacter>(true);
            if (suspect == null) continue;

            string suspectName = suspect.gameObject.name.Replace("(Clone)", "").Trim();
            string sheet = "Dialogue_" + SafeName(suspectName);
            string objPath = LocalizationApply.ObjectPathOf(comp.transform);
            string typeName = comp.GetType().FullName;
            int compIndex = 0;
            foreach (var other in comp.GetComponents<Component>())
            {
                if (other == comp) break;
                if (other != null && other.GetType() == comp.GetType()) compIndex++;
            }

            var so = new SerializedObject(comp);
            var it = so.GetIterator();
            while (it.NextVisible(true))
            {
                if (it.propertyType != SerializedPropertyType.String) continue;
                string field = FieldName(it.propertyPath);
                if (IsIdentifierField(field, deny)) continue;
                string text = it.stringValue;
                if (!LooksLikeCopy(text)) continue;

                string key = LocKeys.ForAsset(comp, it.propertyPath);
                var item = new ScanItem
                {
                    sheet = sheet,
                    key = key,
                    text = text,
                    category = "Dialogue",
                    group = LocGroups.SuspectObjects,
                    context = sheet + "|" + comp.GetType().Name + "|" + field,
                    notes = comp.GetType().Name + " on " + suspectName + " (" + assetName + ") > " + it.propertyPath,
                    binding = new LocBinding
                    {
                        key = key, kind = "MB", assetPath = assetPath, objectPath = objPath, propertyPath = it.propertyPath,
                        componentType = typeName, componentIndex = compIndex, category = "Dialogue", group = LocGroups.SuspectObjects
                    }
                };
                item.binding.context = item.context;

                if (table.TryGet(key, out var e))
                    item.state = e.source != text ? ScanState.SourceChanged : ScanState.Localized;
                else
                {
                    item.state = ScanState.Unlocalized;
                    item.selected = true;
                }
                items.Add(item);
            }
        }
    }

    // ---- ScriptableObject discovery ----

    /// <summary>True when the type (or a base type) is listed in LocalizationSettings.dialogueAssetTypes.</summary>
    public static bool IsDialogueAssetType(Type type, LocalizationSettings settings)
    {
        var allowed = settings != null && settings.dialogueAssetTypes != null && settings.dialogueAssetTypes.Count > 0
            ? settings.dialogueAssetTypes
            : new List<string> { nameof(ScriptedDialogue), nameof(SuspectData) };
        for (var t = type; t != null && t != typeof(ScriptableObject); t = t.BaseType)
            if (allowed.Contains(t.Name) || allowed.Contains(t.FullName)) return true;
        return false;
    }

    /// <summary>
    /// True when the property's top-level field may be scanned on this type. Types without a
    /// rule in LocalizationSettings.dialogueFieldRules allow every field.
    /// </summary>
    public static bool IsAllowedDataField(Type type, string propertyPath, LocalizationSettings settings)
    {
        var rules = settings != null ? settings.dialogueFieldRules : null;
        if (rules == null || rules.Count == 0 || string.IsNullOrEmpty(propertyPath)) return true;

        for (var t = type; t != null && t != typeof(ScriptableObject); t = t.BaseType)
        {
            foreach (var rule in rules)
            {
                if (rule == null || string.IsNullOrEmpty(rule.typeName)) continue;
                if (rule.typeName != t.Name && rule.typeName != t.FullName) continue;
                int dot = propertyPath.IndexOf('.');
                string root = dot < 0 ? propertyPath : propertyPath.Substring(0, dot);
                return rule.fields != null && rule.fields.Contains(root);
            }
        }
        return true;
    }

    /// <summary>
    /// Keys whose every source is excluded data: a ScriptableObject that is not a dialogue type,
    /// or a dialogue type field outside its allowlist (e.g. SuspectData.CharacterArc).
    /// </summary>
    public static List<string> NonDialogueDataKeys(LocalizationTable table, LocalizationSettings settings)
    {
        var typeCache = new Dictionary<string, Type>();
        bool IsExcluded(LocBinding b)
        {
            if (b.kind != "SO") return false;
            if (!typeCache.TryGetValue(b.assetPath, out var t))
            {
                t = AssetDatabase.GetMainAssetTypeAtPath(b.assetPath);
                typeCache[b.assetPath] = t;
            }
            if (t == null) return false; // missing assets are left alone
            return !IsDialogueAssetType(t, settings) || !IsAllowedDataField(t, b.propertyPath, settings);
        }

        return LocBindings.All
            .GroupBy(b => b.key)
            .Where(g => table.TryGet(g.Key, out _) && g.All(IsExcluded))
            .Select(g => g.Key)
            .ToList();
    }

    private static void ScanScriptable(ScriptableObject obj, string path, HashSet<string> deny, LocalizationSettings settings, LocalizationTable table, List<ScanItem> items)
    {
        var so = new SerializedObject(obj);
        var it = so.GetIterator();
        string sheet = SheetForScriptable(obj);
        string assetName = obj.name;
        var objType = obj.GetType();

        while (it.NextVisible(true))
        {
            if (it.propertyType != SerializedPropertyType.String) continue;
            if (!IsAllowedDataField(objType, it.propertyPath, settings)) continue;
            if (IsIdentifierField(FieldName(it.propertyPath), deny)) continue;
            string text = it.stringValue;
            if (!LooksLikeCopy(text)) continue;

            string key = LocKeys.ForAsset(obj, it.propertyPath);
            string group = LocGroups.ForAssetType(obj.GetType());
            var item = new ScanItem
            {
                sheet = sheet,
                key = key,
                text = text,
                category = "Data",
                group = group,
                context = sheet + "|" + obj.GetType().Name + "|" + FieldName(it.propertyPath),
                notes = obj.GetType().Name + " " + assetName + " > " + it.propertyPath,
                binding = new LocBinding { key = key, kind = "SO", assetPath = path, propertyPath = it.propertyPath, category = "Data", group = group }
            };
            item.binding.context = item.context;

            if (table.TryGet(key, out var e))
                item.state = e.source != text ? ScanState.SourceChanged : ScanState.Localized;
            else
            {
                item.state = ScanState.Unlocalized;
                item.selected = true;
            }
            items.Add(item);
        }
    }

    private static string FieldName(string propertyPath)
    {
        var parts = propertyPath.Split('.');
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            if (parts[i] == "Array" || parts[i].StartsWith("data[")) continue;
            return parts[i];
        }
        return propertyPath;
    }

    // ---- Sheets and keys ----

    private static string SheetForScriptable(ScriptableObject obj)
    {
        if (obj is SuspectData || obj is ScriptedDialogue) return "Dialogue_" + SafeName(obj.name);
        return SheetByHint(obj.GetType().Name + " " + obj.name, "Data");
    }

    private static string SheetFor(string assetPath, string objPath)
    {
        return SheetByHint(assetPath + " " + objPath, "UI");
    }

    private static string SheetByHint(string hint, string fallback)
    {
        string h = hint.ToLowerInvariant();
        if (h.Contains("newspaper")) return "Newspaper";
        if (h.Contains("guidebook")) return "Guidebook";
        if (h.Contains("task") || h.Contains("objective")) return "Tasks";
        if (h.Contains("setting") || h.Contains("option")) return "Settings";
        if (h.Contains("terminal") || h.Contains("computer") || h.Contains("/pc") || h.Contains(" pc") || h.Contains("_pc")) return "PC";
        return fallback;
    }

    private static string SheetPrefix(string sheet)
    {
        return LocKeys.Sanitize(sheet);
    }

    private static string SafeName(string s)
    {
        var chars = s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars);
    }

    private static void AssignUniqueKeys(List<ScanItem> items, LocalizationTable table)
    {
        var used = new HashSet<string>(table.entries.Select(e => e.key));
        foreach (var item in items.Where(i => i.state == ScanState.Localized || i.state == ScanState.SourceChanged))
            used.Add(item.key);

        foreach (var item in items.Where(i => i.state == ScanState.Unlocalized))
        {
            string key = item.key;
            int n = 2;
            while (used.Contains(key)) key = item.key + "_" + n++;
            item.key = key;
            item.binding.key = key;
            used.Add(key);
        }
    }

    /// <summary>Strict duplicate rule: identical text in different places keeps separate keys; reuse is offered only when text AND context match.</summary>
    private static void MarkDuplicates(List<ScanItem> items, LocalizationTable table)
    {
        var firstByText = new Dictionary<string, ScanItem>();
        foreach (var i in items)
        {
            string id = i.text + "\u0001" + i.context;
            if (firstByText.TryGetValue(id, out var first))
            {
                if (i.state == ScanState.Unlocalized) i.reuseKey = first.key;
            }
            else firstByText[id] = i;
        }

        foreach (var i in items.Where(x => x.state == ScanState.Unlocalized && x.reuseKey == null))
        {
            foreach (var b in LocBindings.All)
            {
                if (b.context != i.context) continue;
                if (table.TryGet(b.key, out var e) && e.source == i.text) { i.reuseKey = b.key; break; }
            }
        }
    }

    /// <summary>Adds entries to the table, binds origins and attaches LocalizedText. Returns the number of items applied.</summary>
    public static int Apply(List<ScanItem> items, LocalizationTable table)
    {
        var toBind = new List<LocBinding>();
        var dirtySheets = new HashSet<string>();
        int applied = 0;

        foreach (var item in items.Where(i => i.selected))
        {
            string key = item.reuse && item.reuseKey != null ? item.reuseKey : item.key;

            if (item.state == ScanState.SourceChanged && table.TryGet(item.key, out var existing))
            {
                existing.source = item.text;
                dirtySheets.Add(existing.sheet);
                applied++;
                continue;
            }

            if (!table.TryGet(key, out _))
            {
                table.Add(new LocEntry { sheet = item.sheet, key = key, notes = item.notes, source = item.text, status = "new" });
                dirtySheets.Add(item.sheet);
            }

            item.binding.key = key;
            LocBindings.Upsert(item.binding);
            toBind.Add(item.binding);
            applied++;
        }

        LocBindings.Save();
        LocBindings.ClearGroupCache();
        LocalizationSheetIO.SaveSheets(table, dirtySheets);
        LocalizationApply.AttachLocalizedText(toBind);
        AssetDatabase.SaveAssets();
        return applied;
    }
}
