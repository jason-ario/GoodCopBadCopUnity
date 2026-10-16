using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum FitCategory { Label, Header, Body, Grow, Skip }

/// <summary>Ordered from worst to best so sorting by risk puts problems first.</summary>
public enum FitRisk { High, Medium, AutoNoClip, ClippedNoShrink, Grows, Grouped, Ok, Skipped }

public class FitRow
{
    public TMP_Text tmp;
    public string assetPath;     // prefab path, or scene path for scene-owned objects
    public bool isScene;
    public string path;          // hierarchy path inside the prefab / scene
    public int[] indexPath;      // sibling indices from the root, used to find the object in prefab contents
    public string text;
    public FitCategory suggested, chosen;
    public FitRisk risk;
    public bool matchesPreset;
    public bool inLayout, inGroup;
    public float designSize;
}

/// <summary>Finds TMP texts, classifies how they behave with longer translations, and applies fit presets.</summary>
public static class TextFit
{
    public const string DefaultFolder = "Assets/_GoodCopBadCop";
    private static readonly Regex DynamicText = new Regex(@"^[\s\d%/:;.,+\-xX#$()\u200B\[\]]*$");
    private static readonly Regex HeaderName = new Regex("title|header|heading", RegexOptions.IgnoreCase);

    public static readonly string[] RiskLabels = { "HIGH", "MEDIUM", "AUTO, NO CLIP", "CLIPS, NO SHRINK", "GROWS", "GROUPED", "OK", "SKIPPED" };

    public static string Describe(FitRisk r)
    {
        switch (r)
        {
            case FitRisk.High: return "No wrap and no clipping: long text runs past the sides.";
            case FitRisk.Medium: return "Wraps, then spills out of the bottom of its box.";
            case FitRisk.AutoNoClip: return "Shrinks, but overflows once it hits its minimum size.";
            case FitRisk.ClippedNoShrink: return "Cuts text off at full size instead of shrinking first.";
            case FitRisk.Grows: return "Grows with its text (layout or ContentSizeFitter); the parent must be bounded.";
            case FitRisk.Grouped: return "Sized by a TextSizeGroup.";
            case FitRisk.Ok: return "Shrinks within a range and clips as a last resort.";
            default: return "Numbers, input fields or animated text: left alone.";
        }
    }

    // ---------------------------------------------------------------- scan

    public static List<FitRow> Scan(bool prefabs, bool loadedScenes, string folder)
    {
        var rows = new List<FitRow>();
        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) folder = DefaultFolder;

        try
        {
            if (prefabs)
            {
                var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (path.Contains("/Editor/")) continue;
                    if (i % 20 == 0 && EditorUtility.DisplayCancelableProgressBar("Text Fit", path, i / (float)guids.Length)) break;
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (root == null) continue;
                    foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                    {
                        // Nested prefab and variant-inherited texts are listed once, at their source prefab.
                        if (PrefabUtility.GetCorrespondingObjectFromSource(t) != null) continue;
                        rows.Add(Make(t, path, false, root.transform));
                    }
                }
            }

            if (loadedScenes)
            {
                for (int s = 0; s < SceneManager.sceneCount; s++)
                {
                    var scene = SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded || string.IsNullOrEmpty(scene.path)) continue;
                    foreach (var go in scene.GetRootGameObjects())
                        foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
                        {
                            if (PrefabUtility.IsPartOfPrefabInstance(t)) continue; // edited at the source prefab
                            rows.Add(Make(t, scene.path, true, null));
                        }
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        return rows;
    }

    private static FitRow Make(TMP_Text t, string assetPath, bool isScene, Transform root)
    {
        var row = new FitRow { tmp = t, assetPath = assetPath, isScene = isScene };
        var names = new List<string>();
        var idx = new List<int>();
        for (var tr = t.transform; tr != null && tr != root; tr = tr.parent)
        {
            names.Add(tr.name);
            idx.Add(tr.GetSiblingIndex());
        }
        if (root != null) names.Add(root.name);
        names.Reverse(); idx.Reverse();
        row.path = string.Join("/", names);
        row.indexPath = idx.ToArray();
        row.suggested = Suggest(t);
        row.chosen = row.suggested;
        Refresh(row);
        return row;
    }

    /// <summary>Re-reads the component after changes.</summary>
    public static void Refresh(FitRow row)
    {
        var t = row.tmp;
        if (t == null) return;
        row.text = (t.text ?? "").Replace("\n", " \u21B5 ");
        row.designSize = t.enableAutoSizing ? t.fontSizeMax : t.fontSize;
        row.inLayout = ControlledByLayout(t);
        row.inGroup = t.GetComponentInParent<TextSizeGroup>(true) is TextSizeGroup g && g.members.Any(m => m.text == t);
        row.risk = Risk(t, row);
        row.matchesPreset = Matches(t, row.chosen);
    }

    private static bool ControlledByLayout(TMP_Text t)
    {
        if (t.GetComponent<ContentSizeFitter>() != null || t.GetComponent<TMPWidthFitter>() != null) return true;
        var p = t.transform.parent;
        if (p == null) return false;
        var lg = p.GetComponent<HorizontalOrVerticalLayoutGroup>();
        return lg != null && (lg.childControlHeight || lg.childControlWidth) && t.GetComponent<LayoutElement>() == null;
    }

    private static bool IsDynamic(TMP_Text t)
    {
        string s = Visible(t.text);
        if (DynamicText.IsMatch(s)) return true;
        if (t.GetComponentInParent<TMP_InputField>(true) != null) return true;
        if (t.GetComponent<TMPTextReveal>() != null || t.GetComponent<TMPWobbleText>() != null) return true;
        return false;
    }

    public static FitCategory Suggest(TMP_Text t)
    {
        if (IsDynamic(t)) return FitCategory.Skip;
        if (ControlledByLayout(t)) return FitCategory.Grow;

        string s = Visible(t.text);
        if (s.Length <= 1) return FitCategory.Skip; // single glyphs: compass letters, arrows, badges
        bool multiLine = s.Contains("\n");
        bool listItem = ListMarker.IsMatch(s);
        int words = s.Split(new[] { ' ', '\t', '\n' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
        float size = t.enableAutoSizing ? t.fontSizeMax : t.fontSize;
        bool inButton = t.GetComponentInParent<Selectable>(true) != null;

        if (inButton && !multiLine && s.Length <= 40) return FitCategory.Label;
        if (!multiLine && !listItem && words <= 8 && (size >= 40f || HeaderName.IsMatch(t.name))) return FitCategory.Header;
        if (!multiLine && !listItem && words <= 4 && s.Length <= 32) return FitCategory.Label;
        return FitCategory.Body;
    }

    private static readonly Regex Tags = new Regex("<[^>]*>");
    private static readonly Regex ListMarker = new Regex(@"^\s*(\d+[.)]|[-*\u2022])\s");

    private static string Visible(string s) { return Tags.Replace(s ?? "", "").Trim(); }

    private static FitRisk Risk(TMP_Text t, FitRow row)
    {
        if (row.inGroup) return FitRisk.Grouped;
        if (row.suggested == FitCategory.Skip && row.chosen == FitCategory.Skip) return FitRisk.Skipped;
        if (row.inLayout) return FitRisk.Grows;
        bool clips = t.overflowMode != TextOverflowModes.Overflow;
        if (t.enableAutoSizing) return clips ? FitRisk.Ok : FitRisk.AutoNoClip;
        if (clips) return FitRisk.ClippedNoShrink;
        return t.textWrappingMode == TextWrappingModes.NoWrap || t.textWrappingMode == TextWrappingModes.PreserveWhitespaceNoWrap
            ? FitRisk.High : FitRisk.Medium;
    }

    public static bool IsAtRisk(FitRisk r) { return r == FitRisk.High || r == FitRisk.Medium || r == FitRisk.AutoNoClip; }

    // ---------------------------------------------------------------- presets

    public static float MinRatio(FitCategory c, LocalizationSettings s)
    {
        switch (c)
        {
            case FitCategory.Label: return s != null ? s.fitLabelMin : 0.7f;
            case FitCategory.Header: return s != null ? s.fitHeaderMin : 0.6f;
            case FitCategory.Body: return s != null ? s.fitBodyMin : 0.75f;
            default: return 1f;
        }
    }

    private static void Target(FitCategory c, out bool auto, out TextWrappingModes wrap, out TextOverflowModes overflow)
    {
        auto = c == FitCategory.Label || c == FitCategory.Header || c == FitCategory.Body;
        wrap = c == FitCategory.Label ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
        overflow = c == FitCategory.Body ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
    }

    public static bool Matches(TMP_Text t, FitCategory c)
    {
        if (c == FitCategory.Skip) return true;
        Target(c, out bool auto, out var wrap, out var overflow);
        if (c == FitCategory.Grow) return t.textWrappingMode != TextWrappingModes.NoWrap && t.textWrappingMode != TextWrappingModes.PreserveWhitespaceNoWrap;
        return t.enableAutoSizing == auto && t.textWrappingMode == wrap && t.overflowMode == overflow;
    }

    /// <summary>Applies each row's chosen category. Returns how many components changed.</summary>
    public static int Apply(IEnumerable<FitRow> rows, LocalizationSettings settings)
    {
        Undo.SetCurrentGroupName("Text Fit");
        int group = Undo.GetCurrentGroup();
        var prefabRoots = new HashSet<GameObject>();
        var scenes = new HashSet<Scene>();
        int changed = 0;

        foreach (var row in rows)
        {
            var t = row.tmp;
            if (t == null || row.inGroup || row.chosen == FitCategory.Skip) continue;
            if (Matches(t, row.chosen) && row.chosen == FitCategory.Grow) continue;
            if (row.chosen == FitCategory.Grow && t.textWrappingMode == TextWrappingModes.Normal) continue;

            Undo.RecordObject(t, "Text Fit");
            float design = t.enableAutoSizing ? t.fontSizeMax : t.fontSize;
            Target(row.chosen, out bool auto, out var wrap, out var overflow);

            if (row.chosen == FitCategory.Grow)
            {
                // Layout-driven text keeps its sizing; it only needs to wrap so the layout can grow it.
                t.textWrappingMode = TextWrappingModes.Normal;
            }
            else
            {
                t.fontSize = design;
                t.fontSizeMax = design;
                t.fontSizeMin = Mathf.Min(design, Mathf.Round(design * MinRatio(row.chosen, settings) * 10f) / 10f);
                t.enableAutoSizing = auto;
                t.textWrappingMode = wrap;
                t.overflowMode = overflow;
            }

            EditorUtility.SetDirty(t);
            if (row.isScene) scenes.Add(t.gameObject.scene);
            else prefabRoots.Add(t.transform.root.gameObject);
            changed++;
        }

        foreach (var root in prefabRoots) PrefabUtility.SavePrefabAsset(root);
        foreach (var s in scenes) EditorSceneManager.MarkSceneDirty(s);
        Undo.CollapseUndoOperations(group);

        foreach (var row in rows) Refresh(row);
        return changed;
    }

    // ---------------------------------------------------------------- size groups

    /// <summary>Adds a TextSizeGroup on the closest common parent of the rows. All rows must share one prefab or scene.</summary>
    public static string AddSizeGroup(List<FitRow> rows, LocalizationSettings settings)
    {
        rows = rows.Where(r => r.tmp != null).ToList();
        if (rows.Count < 2) return "Select at least two texts.";
        if (rows.Select(r => r.assetPath).Distinct().Count() > 1) return "All selected texts must be in the same prefab or scene.";

        if (rows[0].isScene)
        {
            var parent = CommonParent(rows.Select(r => r.tmp.transform).ToList());
            if (parent == null) return "The selected texts have no common parent.";
            var g = parent.GetComponent<TextSizeGroup>() ?? Undo.AddComponent<TextSizeGroup>(parent.gameObject);
            Undo.RecordObject(g, "Text Size Group");
            Fill(g, rows.Select(r => (r.tmp, r)).ToList(), settings);
            EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
            return "Added a TextSizeGroup on '" + parent.name + "' with " + rows.Count + " texts.";
        }

        string path = rows[0].assetPath;
        var contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var pairs = new List<(TMP_Text, FitRow)>();
            foreach (var r in rows)
            {
                var tr = contents.transform;
                foreach (int i in r.indexPath)
                {
                    if (tr == null || i >= tr.childCount) { tr = null; break; }
                    tr = tr.GetChild(i);
                }
                var t = tr != null ? tr.GetComponent<TMP_Text>() : null;
                if (t == null) return "Could not find '" + r.path + "' in the prefab. Rescan and try again.";
                pairs.Add((t, r));
            }
            var parent = CommonParent(pairs.Select(p => p.Item1.transform).ToList());
            if (parent == null) return "The selected texts have no common parent.";
            var g = parent.GetComponent<TextSizeGroup>() ?? parent.gameObject.AddComponent<TextSizeGroup>();
            Fill(g, pairs, settings);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            return "Added a TextSizeGroup on '" + parent.name + "' in " + System.IO.Path.GetFileName(path) + " with " + rows.Count + " texts. Rescan to refresh the list.";
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
    }

    private static void Fill(TextSizeGroup g, List<(TMP_Text t, FitRow row)> pairs, LocalizationSettings settings)
    {
        foreach (var (t, row) in pairs)
        {
            g.members.RemoveAll(m => m.text == t || m.text == null);
            float design = t.enableAutoSizing ? t.fontSizeMax : t.fontSize;
            var cat = row.chosen == FitCategory.Skip || row.chosen == FitCategory.Grow ? FitCategory.Label : row.chosen;
            float min = Mathf.Round(design * MinRatio(cat, settings) * 10f) / 10f;
            Target(cat, out _, out var wrap, out var overflow);
            t.textWrappingMode = wrap;
            t.overflowMode = overflow;
            g.members.Add(new TextSizeGroup.Member { text = t, maxSize = design, minSize = min });
        }
        EditorUtility.SetDirty(g);
    }

    private static Transform CommonParent(List<Transform> items)
    {
        var first = new List<Transform>();
        for (var p = items[0].parent; p != null; p = p.parent) first.Add(p);
        foreach (var candidate in first)
            if (items.All(i => i.IsChildOf(candidate) && i != candidate)) return candidate;
        return null;
    }

    // ---------------------------------------------------------------- navigation

    public static void Open(FitRow row)
    {
        if (row.isScene)
        {
            if (row.tmp != null) { Selection.activeGameObject = row.tmp.gameObject; EditorGUIUtility.PingObject(row.tmp.gameObject); }
            return;
        }
        var stage = PrefabStageUtility.OpenPrefab(row.assetPath);
        if (stage == null) return;
        var tr = stage.prefabContentsRoot.transform;
        foreach (int i in row.indexPath)
        {
            if (i >= tr.childCount) return;
            tr = tr.GetChild(i);
        }
        Selection.activeGameObject = tr.gameObject;
        EditorGUIUtility.PingObject(tr.gameObject);
    }
}
