using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Runtime-filled TMP labels: a game script holds a serialized reference to the label (or GetComponent's it on its own object)
/// and its source writes <c>.text</c> / <c>SetText</c> on that reference. The authored text is then only a placeholder, so the
/// scan marks it runtime and never applies it. Overrides: LocalizationSettings.runtimeTextLabels / staticTextLabels.
/// </summary>
public static partial class LocalizationScanner
{
    private const string TextWrite = @"\s*\??\s*\.\s*(?:text\s*\+?=(?!=)|SetText\s*\(|SetCharArray\s*\()";
    private static readonly Regex AnyTextWrite = new Regex(@"\.\s*(?:text\s*\+?=(?!=)|SetText\s*\(|SetCharArray\s*\()", RegexOptions.Compiled);
    private static readonly Regex GetTmpOnSelf = new Regex(@"GetComponent\s*<\s*(?:TMP_Text|TextMeshProUGUI|TextMeshPro)\s*>", RegexOptions.Compiled);
    private static readonly Regex TypeDecl = new Regex(@"\b(?:class|struct)\s+(\w+)", RegexOptions.Compiled);
    private static readonly Regex Comments = new Regex(@"/\*.*?\*/|//[^\n]*", RegexOptions.Singleline | RegexOptions.Compiled);

    private static Dictionary<string, string> _typeSources;
    private static HashSet<string> _relayTypes = new HashSet<string>();
    private static readonly Dictionary<Type, string> RelayCallCache = new Dictionary<Type, string>();
    private static readonly Dictionary<Type, string> ChainSourceCache = new Dictionary<Type, string>();
    private static readonly Dictionary<string, bool> FieldWriteCache = new Dictionary<string, bool>();
    private static readonly Dictionary<TMP_Text, string> RuntimeDriven = new Dictionary<TMP_Text, string>();

    /// <summary>Identifier used by the override lists in LocalizationSettings.</summary>
    public static string RuntimeTextId(LocBinding b) => b.assetPath + "|" + b.objectPath;

    private static void ResetRuntimeDetection(LocalizationSettings settings)
    {
        _typeSources = null;
        _textHelpers = null;
        _relayTypes = new HashSet<string>(settings.textRelayTypes ?? new List<string>());
        RelayCallCache.Clear();
        ChainSourceCache.Clear();
        FieldWriteCache.Clear();
        RuntimeDriven.Clear();
    }

    /// <summary>For a text relay type: a regex suffix matching a call to any of its public string-taking methods; otherwise null.</summary>
    private static string RelayCall(Type type)
    {
        if (RelayCallCache.TryGetValue(type, out var cached)) return cached;
        string result = null;
        for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
        {
            if (!_relayTypes.Contains(t.Name)) continue;
            var names = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(string)))
                .Select(m => Regex.Escape(m.Name)).Distinct().ToList();
            if (names.Count > 0) result = @"\s*\??\s*\.\s*(?:" + string.Join("|", names) + @")\s*\(";
            break;
        }
        return RelayCallCache[type] = result;
    }

    /// <summary>Type name -> source of every game file declaring it (partials merged), comments stripped.</summary>
    private static Dictionary<string, string> TypeSources()
    {
        if (_typeSources != null) return _typeSources;
        var map = new Dictionary<string, StringBuilder>();
        foreach (var file in Directory.GetFiles(ProjectRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Replace('\\', '/').Contains("/Editor/")) continue;
            string src = Comments.Replace(File.ReadAllText(file), "");
            var names = new HashSet<string>();
            foreach (Match m in TypeDecl.Matches(src)) names.Add(m.Groups[1].Value);
            foreach (var n in names)
            {
                if (!map.TryGetValue(n, out var sb)) map[n] = sb = new StringBuilder();
                sb.Append(src).Append('\n');
            }
        }
        _typeSources = map.ToDictionary(p => p.Key, p => p.Value.ToString());
        _textHelpers = FindTextHelpers(map.Values.Select(sb => sb.ToString()).Distinct());
        return _typeSources;
    }

    // ---- Helper methods that write a label passed as an argument: SetText(nameLabel, "Name: " + ...) ----

    private static Dictionary<string, HashSet<int>> _textHelpers;
    private static readonly Regex MethodDecl = new Regex(@"\b(\w+)\s*\(([^()]*)\)\s*(\{|=>)", RegexOptions.Compiled);
    private static readonly Regex TmpParam = new Regex(@"^\s*(?:this\s+)?(?:TMPro\.)?(?:TMP_Text|TextMeshProUGUI|TextMeshPro)\s+(\w+)", RegexOptions.Compiled);
    private static readonly HashSet<string> NotMethods = new HashSet<string> { "if", "for", "foreach", "while", "switch", "using", "lock", "catch", "return", "new" };

    /// <summary>Method name -> indices of its TMP parameters that the method body writes text to.</summary>
    private static Dictionary<string, HashSet<int>> FindTextHelpers(IEnumerable<string> sources)
    {
        var helpers = new Dictionary<string, HashSet<int>>();
        foreach (var src in sources)
        {
            foreach (Match m in MethodDecl.Matches(src))
            {
                string name = m.Groups[1].Value;
                if (NotMethods.Contains(name)) continue;
                var pars = m.Groups[2].Value.Split(',');
                string body = null;
                for (int i = 0; i < pars.Length; i++)
                {
                    var p = TmpParam.Match(pars[i]);
                    if (!p.Success) continue;
                    if (body == null) body = BodyAt(src, m.Index + m.Length - m.Groups[3].Length, m.Groups[3].Value == "=>");
                    if (!Regex.IsMatch(body, @"(?<!\w)" + Regex.Escape(p.Groups[1].Value) + TextWrite)) continue;
                    if (!helpers.TryGetValue(name, out var set)) helpers[name] = set = new HashSet<int>();
                    set.Add(i);
                }
            }
        }
        return helpers;
    }

    /// <summary>Text of a method body starting at "{" (brace-matched) or "=>" (up to the next ';').</summary>
    private static string BodyAt(string src, int start, bool expression)
    {
        if (expression)
        {
            int end = src.IndexOf(';', start);
            return end < 0 ? src.Substring(start) : src.Substring(start, end - start);
        }
        int depth = 0;
        for (int i = start; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(start, i - start + 1);
        }
        return src.Substring(start);
    }

    /// <summary>True when <paramref name="source"/> calls a text helper with <paramref name="field"/> at a written parameter position.</summary>
    private static bool PassesToTextHelper(string source, string field)
    {
        if (_textHelpers == null) TypeSources();
        if (_textHelpers.Count == 0 || source.IndexOf(field, StringComparison.Ordinal) < 0) return false;
        var fieldArg = new Regex(@"^\s*(?:this\s*\.\s*)?" + Regex.Escape(field) + @"\s*(?:\[[^\]]*\])?\s*$");
        foreach (var helper in _textHelpers)
        {
            foreach (Match call in Regex.Matches(source, @"(?<!\w)" + Regex.Escape(helper.Key) + @"\s*\("))
            {
                var args = CallArgs(source, call.Index + call.Length);
                foreach (int i in helper.Value)
                    if (i < args.Count && fieldArg.IsMatch(args[i])) return true;
            }
        }
        return false;
    }

    /// <summary>Top-level comma-separated arguments of a call whose "(" ends right before <paramref name="start"/>.</summary>
    private static List<string> CallArgs(string src, int start)
    {
        var args = new List<string>();
        int depth = 0, from = start;
        for (int i = start; i < src.Length; i++)
        {
            char c = src[i];
            if (c == '(' || c == '[' || c == '{') depth++;
            else if (c == ',' && depth == 0) { args.Add(src.Substring(from, i - from)); from = i + 1; }
            else if (c == ')' || c == ']' || c == '}')
            {
                if (depth == 0) { args.Add(src.Substring(from, i - from)); break; }
                depth--;
            }
            else if (c == ';') break;
        }
        return args;
    }

    private static string SourceOf(Type t)
    {
        string name = t.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0) name = name.Substring(0, tick);
        return TypeSources().TryGetValue(name, out var s) ? s : "";
    }

    /// <summary>Source of the type and its game base classes (inherited fields are written from base code).</summary>
    private static string ChainSource(Type type)
    {
        if (ChainSourceCache.TryGetValue(type, out var cached)) return cached;
        var sb = new StringBuilder();
        for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(object); t = t.BaseType)
            sb.Append(SourceOf(t));
        return ChainSourceCache[type] = sb.ToString();
    }

    /// <summary>Leaf field of "ui.labels.Array.data[2]" ("labels") and the type that declares it.</summary>
    private static string LeafField(Type rootType, string propertyPath, out Type declaringType)
    {
        declaringType = null;
        Type current = rootType;
        System.Reflection.FieldInfo field = null;
        var parts = propertyPath.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "Array") { i++; if (field != null) current = ElementType(field.FieldType); continue; }
            field = FindField(current, parts[i]);
            if (field == null) break;
            current = field.FieldType;
        }
        if (field != null) declaringType = field.DeclaringType;
        return parts.LastOrDefault(p => p != "Array" && !p.StartsWith("data[")) ?? propertyPath;
    }

    /// <summary>"field.text = ...", "field[i].SetText(...)", or "foreach (var x in field) x.text = ..." (<paramref name="write"/> = the member access suffix).</summary>
    private static bool WritesField(string source, string field, string write)
    {
        string f = Regex.Escape(field);
        if (Regex.IsMatch(source, @"(?<!\w)" + f + @"\s*(?:\[[^\]]*\]\s*)?" + write)) return true;
        foreach (Match m in Regex.Matches(source, @"foreach\s*\(\s*[\w<>\.]+\s+(\w+)\s+in\s+(?:this\s*\.\s*)?" + f + @"\s*\)"))
            if (Regex.IsMatch(source, @"(?<!\w)" + Regex.Escape(m.Groups[1].Value) + write)) return true;
        return write == TextWrite && PassesToTextHelper(source, field);
    }

    /// <summary>Records every TMP label under <paramref name="root"/> that a game script writes at runtime.</summary>
    private static void CollectRuntimeDriven(GameObject root)
    {
        var assembly = typeof(ScriptedDialogue).Assembly;
        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb is LocalizedText || mb.GetType().Assembly != assembly || !IsGameScript(mb)) continue;
            var type = mb.GetType();
            string src = ChainSource(type);
            if (src.Length == 0) continue;

            // Text relays (typewriter etc.) re-display whatever they are given, including the label's own authored text.
            if (RelayCall(type) == null && GetTmpOnSelf.IsMatch(src) && AnyTextWrite.IsMatch(src))
                foreach (var t in mb.GetComponents<TMP_Text>()) MarkDriven(t, type.Name + " (GetComponent)");

            var it = new SerializedObject(mb).GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = it.propertyType == SerializedPropertyType.Generic;
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;

                TMP_Text tmp;
                string write;
                if (it.objectReferenceValue is TMP_Text direct) { tmp = direct; write = TextWrite; }
                else if (it.objectReferenceValue is Component relay && (write = RelayCall(relay.GetType())) != null) tmp = relay.GetComponent<TMP_Text>();
                else continue;
                if (tmp == null) continue;

                string field = LeafField(type, it.propertyPath, out var declaring);
                string cacheKey = type.FullName + "|" + field + "|" + write;
                if (!FieldWriteCache.TryGetValue(cacheKey, out bool writes))
                {
                    // Nested types live in their outer type's file; looking them up by bare name ("Row") hits unrelated classes.
                    var owner = declaring;
                    while (owner != null && owner.IsNested) owner = owner.DeclaringType;
                    string scope = owner != null && !owner.IsAssignableFrom(type) ? src + SourceOf(owner) : src;
                    FieldWriteCache[cacheKey] = writes = WritesField(scope, field, write);
                }
                if (writes) MarkDriven(tmp, type.Name + "." + field);
            }
        }
    }

    private static void MarkDriven(TMP_Text tmp, string reason)
    {
        if (tmp == null) return;
        if (!RuntimeDriven.ContainsKey(tmp)) RuntimeDriven[tmp] = reason;
        // A scene/prefab writer pointing into a prefab instance: the label is scanned in its source prefab.
        if (PrefabUtility.IsPartOfPrefabInstance(tmp))
        {
            var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(tmp);
            if (src != null && !RuntimeDriven.ContainsKey(src)) RuntimeDriven[src] = reason;
        }
    }

    private static void MarkRuntimeItems(List<ScanItem> items, LocalizationSettings settings)
    {
        var forced = new HashSet<string>(settings.runtimeTextLabels ?? new List<string>());
        var allowed = new HashSet<string>(settings.staticTextLabels ?? new List<string>());
        foreach (var item in items)
        {
            if (item.source == null) continue;
            string id = RuntimeTextId(item.binding);
            bool detected = RuntimeDriven.TryGetValue(item.source, out var reason);
            if (forced.Contains(id)) reason = "marked runtime";
            else if (allowed.Contains(id)) { item.runtimeOverridden = detected; continue; }
            else if (!detected) continue;

            item.runtime = true;
            item.runtimeReason = reason;
            item.selected = false;
        }
    }

    /// <summary>Sheet keys that only runtime-filled labels use (no other binding or scanned source shares them).</summary>
    public static List<string> RuntimePlaceholderKeys(List<ScanItem> items, LocalizationTable table)
    {
        var runtime = items.Where(i => i.runtime).ToList();
        var runtimeIds = new HashSet<string>(runtime.Select(i => RuntimeTextId(i.binding)));
        var otherKeys = new HashSet<string>(items.Where(i => !i.runtime).Select(i => i.key));
        return runtime.Select(i => i.key).Distinct()
            .Where(k => table.TryGet(k, out _) && !otherKeys.Contains(k))
            .Where(k => LocBindings.All.Where(b => b.key == k).All(b => b.kind == "TMP" && runtimeIds.Contains(RuntimeTextId(b))))
            .ToList();
    }

    /// <summary>Runtime-filled labels that already carry a LocalizedText key.</summary>
    public static List<LocBinding> RuntimeBoundLabels(List<ScanItem> items)
    {
        return items.Where(i => i.runtime && (i.state != ScanState.Unlocalized || i.keyFixed)).Select(i => i.binding).ToList();
    }
}
