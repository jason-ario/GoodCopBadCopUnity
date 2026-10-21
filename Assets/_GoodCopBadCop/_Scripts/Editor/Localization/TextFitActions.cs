using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Bezi;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Bezi actions that drive the Text Fit presets by path and measure pseudolocale fit without opening the window.</summary>
public static class TextFitActions
{
    [BeziAction(
        "Apply a Text Fit category (Label, Header, Body, Grow, Skip, or Current to keep each text's existing preset) to TMP texts in one prefab or loaded scene, using TextFit.Apply. " +
        "assetPath: 'Assets/...prefab' or 'Assets/...unity'. objectPaths: newline-separated hierarchy paths including the prefab root name " +
        "(e.g. 'Tutorial Overlay/Top Tutorial/Screens/Daily Fax Tutorial/Header'). minRatioOverride (>0) replaces the category's min-size ratio. " +
        "growHeightForEnglish raises anchored Header/Body rect heights so English fits at design size. Returns the changed count and paths not found or skipped.")]
    public static string ApplyTextFit(string assetPath, string objectPaths, string category, float minRatioOverride = -1f, bool growHeightForEnglish = false)
    {
        // "Current" re-applies the preset each text already matches (Label, Header or Body), e.g. to change only the min ratio.
        bool keepCurrent = string.Equals(category, "Current", StringComparison.OrdinalIgnoreCase);
        FitCategory cat = FitCategory.Label;
        if (!keepCurrent && !Enum.TryParse(category, true, out cat)) return "Error: unknown category '" + category + "'. Use Label, Header, Body, Grow, Skip or Current.";
        var wanted = SplitPaths(objectPaths);
        if (wanted.Count == 0) return "Error: objectPaths is empty.";
        assetPath = Normalize(assetPath);
        var rows = TextFit.ScanAsset(assetPath);
        if (rows == null) return "Error: '" + assetPath + "' was not found (or the scene is not loaded).";

        var matched = new List<FitRow>();
        var notFound = new List<string>();
        var skipped = new List<string>();
        foreach (string p in wanted)
        {
            var row = Find(rows, p);
            if (row == null) { notFound.Add(p); continue; }
            if (row.inGroup) { skipped.Add(p + " (in a TextSizeGroup)"); continue; }
            if (!keepCurrent && cat == FitCategory.Skip) { skipped.Add(p + " (Skip)"); continue; }
            // Siblings can share a name; apply to every text at this path.
            foreach (var same in rows.Where(r => r.path == row.path && !r.inGroup && !matched.Contains(r)))
            {
                var c = cat;
                if (keepCurrent)
                {
                    var found = new[] { FitCategory.Label, FitCategory.Header, FitCategory.Body }.Where(x => TextFit.Matches(same.tmp, x)).ToList();
                    if (found.Count == 0) { skipped.Add(p + " (no preset to keep)"); continue; }
                    c = found[0];
                }
                same.chosen = c;
                matched.Add(same);
            }
        }

        int changed = matched.Count > 0
            ? TextFit.Apply(matched, LocalizationSettings.Instance, minRatioOverride, growHeightForEnglish, saveScenes: true)
            : 0;

        var sb = new StringBuilder();
        sb.AppendLine("Changed " + changed + " of " + wanted.Count + " text(s) in " + assetPath + " to " + (keepCurrent ? "their current preset" : cat.ToString()) + ".");
        foreach (var r in matched)
            sb.AppendLine("  " + r.path + ": size " + r.tmp.fontSizeMin.ToString("0.#") + "-" + r.tmp.fontSizeMax.ToString("0.#") +
                          ", rect " + r.tmp.rectTransform.rect.size);
        if (notFound.Count > 0) sb.AppendLine("Not found:\n  " + string.Join("\n  ", notFound));
        if (skipped.Count > 0) sb.AppendLine("Skipped:\n  " + string.Join("\n  ", skipped));
        return sb.ToString();
    }

    [BeziAction(
        "Add a TextSizeGroup on the closest common parent of the given texts (TextFit.AddSizeGroup). assetPath: 'Assets/...prefab' or a loaded " +
        "'Assets/...unity'. objectPaths: newline-separated hierarchy paths including the prefab root name. Apply presets before grouping.")]
    public static string AddTextSizeGroup(string assetPath, string objectPaths)
    {
        var wanted = SplitPaths(objectPaths);
        if (wanted.Count < 2) return "Error: give at least two objectPaths.";
        assetPath = Normalize(assetPath);
        var rows = TextFit.ScanAsset(assetPath);
        if (rows == null) return "Error: '" + assetPath + "' was not found (or the scene is not loaded).";

        var matched = new List<FitRow>();
        var notFound = new List<string>();
        foreach (string p in wanted)
        {
            var row = Find(rows, p);
            if (row == null) notFound.Add(p); else matched.Add(row);
        }
        if (matched.Count < 2) return "Error: fewer than two paths were found. Not found:\n  " + string.Join("\n  ", notFound);

        string result = TextFit.AddSizeGroup(matched, LocalizationSettings.Instance);
        if (matched[0].isScene)
        {
            var scene = SceneManager.GetSceneByPath(assetPath);
            if (scene.IsValid()) EditorSceneManager.SaveScene(scene);
        }
        if (notFound.Count > 0) result += "\nNot found:\n  " + string.Join("\n  ", notFound);
        return result;
    }

    // ---------------------------------------------------------------- measurement

    private static readonly Regex Letters = new Regex("[A-Za-z]");
    private static readonly Regex Tags = new Regex("<[^>]*>");

    private class Measure
    {
        public string status, asset, path, text;
        public float scale = 1f, enScale = 1f;
    }

    [BeziAction(
        "Measure every TMP text in prefabs under folder (plus loaded scenes) against its pseudo-localized length, using TMP GetPreferredValues. " +
        "Nothing is saved. Returns status counts plus rows whose status is in statuses (comma-separated: OK, SHRINK, SHRINK_HEAVY, SPILL, TRUNCATE, " +
        "ELLIPSIS, CLIP_*, EN_FAIL, LAYOUT, LAYOUT_OVERFLOW, SCROLL, SKIP).",
        IsReadOnly = true)]
    public static string MeasurePseudoFit(string folder = "Assets/_GoodCopBadCop", string statuses = "SPILL,TRUNCATE,ELLIPSIS,LAYOUT_OVERFLOW,EN_FAIL")
    {
        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return "Error: '" + folder + "' is not a folder.";
        var settings = LocalizationSettings.Instance;
        float expansion = settings != null ? settings.pseudoExpansion : 0.4f;
        var results = new List<Measure>();

        try
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.Contains("/Editor/")) continue;
                if (i % 20 == 0) EditorUtility.DisplayProgressBar("Measure pseudo fit", path, i / (float)guids.Length);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || asset.GetComponentInChildren<TMP_Text>(true) == null) continue;

                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (contents.transform is RectTransform rootRt) LayoutRebuilder.ForceRebuildLayoutImmediate(rootRt);
                    foreach (var t in contents.GetComponentsInChildren<TMP_Text>(true))
                    {
                        if (PrefabUtility.IsPartOfPrefabInstance(t)) continue;
                        results.Add(MeasureText(t, path, PathOf(t.transform), expansion));
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded || string.IsNullOrEmpty(scene.path)) continue;
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
                    {
                        if (PrefabUtility.IsPartOfPrefabInstance(t)) continue;
                        results.Add(MeasureText(t, scene.path, PathOf(t.transform), expansion));
                    }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        var filter = new HashSet<string>((statuses ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToUpperInvariant()));
        var sb = new StringBuilder();
        sb.AppendLine("Measured " + results.Count + " texts.");
        foreach (var g in results.GroupBy(r => r.status).OrderByDescending(g => g.Count()))
            sb.AppendLine("  " + g.Key + ": " + g.Count());
        sb.AppendLine();
        foreach (var r in results.Where(r => filter.Contains(r.status) || (r.status.StartsWith("CLIP_") && filter.Contains("CLIP_*")))
                                 .OrderBy(r => r.status).ThenBy(r => r.asset).ThenBy(r => r.path))
        {
            string shown = r.text.Replace("\n", " ");
            if (shown.Length > 40) shown = shown.Substring(0, 40) + "...";
            sb.AppendLine(r.status + " | " + r.asset.Replace(folder + "/", "") + " | " + r.path + " | pseudo " + r.scale.ToString("0.00") +
                          " en " + r.enScale.ToString("0.00") + " | " + shown);
        }
        return sb.ToString();
    }

    private static Measure MeasureText(TMP_Text t, string asset, string path, float expansion)
    {
        string src = SourceOf(t);
        var m = new Measure { asset = asset, path = path, text = src ?? "" };
        string visible = Tags.Replace(src ?? "", "").Trim();
        var rt = t.rectTransform;

        if (string.IsNullOrEmpty(visible) || !Letters.IsMatch(visible) || t.GetComponentInParent<TMP_InputField>(true) != null)
        { m.status = "SKIP"; return m; }

        string pseudo = PseudoLocalizer.Generate("pseudo", src, src, expansion);
        float design = t.enableAutoSizing ? t.fontSizeMax : t.fontSize;
        float min = t.enableAutoSizing ? t.fontSizeMin : design;
        bool wrap = t.textWrappingMode == TextWrappingModes.Normal || t.textWrappingMode == TextWrappingModes.PreserveWhitespace;

        // Snapshot so measuring never leaves changes on scene objects.
        bool wasAuto = t.enableAutoSizing;
        float wasSize = t.fontSize;
        try
        {
            t.enableAutoSizing = false;

            if (LayoutDriven(t, out float available))
            {
                bool scroll = t.GetComponentInParent<ScrollRect>(true) != null;
                t.fontSize = design;
                float pw = t.GetPreferredValues(pseudo, Mathf.Infinity, Mathf.Infinity).x;
                float ew = t.GetPreferredValues(src, Mathf.Infinity, Mathf.Infinity).x;
                m.scale = available > 0f && pw > 0f ? Mathf.Min(1f, available / pw) : 1f;
                m.enScale = available > 0f && ew > 0f ? Mathf.Min(1f, available / ew) : 1f;
                if (scroll) m.status = "SCROLL";
                else if (!wrap && available > 1f && pw > available + 0.5f) m.status = "LAYOUT_OVERFLOW";
                else m.status = "LAYOUT";
                return m;
            }

            float width = rt.rect.width, height = rt.rect.height;
            var le = t.GetComponent<LayoutElement>();
            if (le != null && le.preferredWidth > 0f)
            {
                // Fixed-width label in a layout: the stored rect may be stale, so use the layout's sizes.
                width = le.preferredWidth;
                if (le.preferredHeight > 0f) height = le.preferredHeight;
                else if (height < 1f)
                {
                    t.fontSize = design;
                    height = t.GetPreferredValues(src, Mathf.Infinity, Mathf.Infinity).y;
                }
            }
            if (width < 1f || height < 1f) { m.status = "SKIP"; return m; }

            m.enScale = SmallestScale(t, src, design, width, height, wrap);
            m.scale = SmallestScale(t, pseudo, design, width, height, wrap);

            float enNeeded = m.enScale * design, psNeeded = m.scale * design;
            if (m.scale >= 0.999f) m.status = "OK";
            else if (m.enScale < 0.999f && enNeeded < min - 0.05f) m.status = "EN_FAIL";
            else if (psNeeded >= min - 0.05f) m.status = m.scale >= 0.75f ? "SHRINK" : "SHRINK_HEAVY";
            else
            {
                switch (t.overflowMode)
                {
                    case TextOverflowModes.Overflow: m.status = "SPILL"; break;
                    case TextOverflowModes.Truncate: m.status = "TRUNCATE"; break;
                    case TextOverflowModes.Ellipsis: m.status = "ELLIPSIS"; break;
                    default: m.status = "CLIP_" + t.overflowMode.ToString().ToUpperInvariant(); break;
                }
            }
            return m;
        }
        finally
        {
            t.enableAutoSizing = wasAuto;
            t.fontSize = wasSize;
        }
    }

    /// <summary>Binary search for the smallest font scale (0.05..1) at which the text fits the rect.</summary>
    private static float SmallestScale(TMP_Text t, string text, float design, float width, float height, bool wrap)
    {
        bool Fits(float k)
        {
            t.fontSize = design * k;
            var p = t.GetPreferredValues(text, wrap ? width : Mathf.Infinity, Mathf.Infinity);
            return p.y <= height + 0.5f && p.x <= width + 0.5f;
        }
        if (Fits(1f)) return 1f;
        float lo = 0.05f, hi = 1f;
        if (!Fits(lo)) return lo;
        for (int i = 0; i < 14; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (Fits(mid)) lo = mid; else hi = mid;
        }
        return lo;
    }

    /// <summary>Same rule as the Text Fit tab, plus the width the layout gives the text to grow into.</summary>
    private static bool LayoutDriven(TMP_Text t, out float available)
    {
        available = 0f;
        bool fitter = t.GetComponent<ContentSizeFitter>() != null || t.GetComponent<TMPWidthFitter>() != null;
        var parent = t.transform.parent as RectTransform;
        var lg = parent != null ? parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
        bool inGroup = lg != null && (lg.childControlHeight || lg.childControlWidth) && t.GetComponent<LayoutElement>() == null;
        if (!fitter && !inGroup) return false;

        // Walk up past rects that are themselves sized by fitters or layouts to the first bounded one.
        var rt = parent;
        while (rt != null)
        {
            var up = rt.parent as RectTransform;
            bool sized = rt.GetComponent<ContentSizeFitter>() != null ||
                         (up != null && up.GetComponent<HorizontalOrVerticalLayoutGroup>() is HorizontalOrVerticalLayoutGroup ulg && ulg.childControlWidth);
            if (!sized) break;
            rt = up;
        }
        if (rt == null) return true;
        var g = rt.GetComponent<LayoutGroup>();
        available = rt.rect.width - (g != null ? g.padding.horizontal : 0);
        return true;
    }

    private static string SourceOf(TMP_Text t)
    {
        var lt = t.GetComponent<LocalizedText>();
        if (lt != null && !string.IsNullOrEmpty(lt.key) && Loc.Table != null && Loc.Table.TryGet(lt.key, out var e) && !string.IsNullOrEmpty(e.source))
            return e.source;
        return t.text;
    }

    private static string PathOf(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    private static List<string> SplitPaths(string s)
    {
        // Object names can end in spaces ("NAME: "), so only leading whitespace and the leading '/' are stripped.
        return (s ?? "").Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.TrimStart().TrimStart('/')).Where(x => x.Trim().Length > 0).Distinct().ToList();
    }

    private static FitRow Find(List<FitRow> rows, string path)
    {
        return rows.FirstOrDefault(r => r.path == path) ?? rows.FirstOrDefault(r => r.path.Trim() == path.Trim());
    }

    private static string Normalize(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return assetPath;
        assetPath = assetPath.Replace('\\', '/');
        int i = assetPath.IndexOf("Assets/", StringComparison.Ordinal);
        return i > 0 ? assetPath.Substring(i) : assetPath;
    }
}
