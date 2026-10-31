using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bezi;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>One localized TMP text whose pseudo version doesn't fit, with a snapshot of the settings needed to preview it.</summary>
public class CharFitItem
{
    public string key, asset, path, source, pseudo;
    public int limit, enLen, pseudoLen;
    public float pseudoScale, floorRatio;
    public bool enOver, inSheet, pseudoFitsFloor;

    // TMP snapshot (assets stay valid after the prefab contents are unloaded).
    public TMP_FontAsset font;
    public Material material;
    public FontStyles fontStyle;
    public TextAlignmentOptions alignment;
    public TextWrappingModes wrapping;
    public TextOverflowModes overflow;
    public bool autoSize;
    public float design, fontSize, fontSizeMin, fontSizeMax, floorSize;
    public float characterSpacing, wordSpacing, lineSpacing, paragraphSpacing;
    public Vector4 margin;
    public Vector2 box;
    public bool layoutLine;
    public bool isOrthographic; // 3D TextMeshPro that isn't orthographic renders at 0.1 scale; the preview must match.
}

/// <summary>
/// Measures per-key character limits (sheet column max_chars): the most visible characters a localized TMP text can hold
/// at its smallest readable font size. Only the worst pseudo-fit texts get a limit; every other key stays unlimited.
/// Shared by the ComputeCharLimits action and the Fit Review tab.
/// </summary>
public static class CharLimitActions
{
    public const float DefaultReadableRatio = 0.6f;
    public const float DefaultPseudoScaleBelow = 0.75f;

    public class CollectStats
    {
        public int texts, instances, unkeyed, unknownKey, measured;
    }

    [BeziAction(
        "Measure max_chars for localized TMP texts (LocalizedText keys) in prefabs under folder plus loaded scenes. The limit is how many visible " +
        "characters fit the text's box at its readable floor size: max(fontSizeMin, design * readableRatio), never above design. Only texts whose " +
        "pseudo version needs a scale below pseudoScaleBelow, or can't fit at the floor, get a limit. A key used in several places takes the smallest. " +
        "Keys approved in the Fit Review tab (FitReview.json) are skipped; keys reviewed as 'limit' use the reviewed value. " +
        "write=false is a dry run. write=true stores limits in the sheet CSVs; existing max_chars values are kept unless overwrite=true.")]
    public static string ComputeCharLimits(string folder = "Assets/_GoodCopBadCop", float readableRatio = DefaultReadableRatio, float pseudoScaleBelow = DefaultPseudoScaleBelow,
                                           bool write = false, bool overwrite = false)
    {
        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return "Error: '" + folder + "' is not a folder.";
        var table = LocalizationSheetIO.LoadAll();
        var items = Collect(folder, readableRatio, pseudoScaleBelow, table, out var st);
        var review = FitReviewStore.Load();

        int written = 0, kept = 0, approved = 0;
        var changedSheets = new HashSet<string>();
        if (write)
        {
            foreach (var it in items)
            {
                if (!table.TryGet(it.key, out var e)) continue;
                var d = review.Get(it.key, it.source);
                if (d != null && d.decision == FitReviewStore.Approved) { approved++; continue; }
                int limit = d != null && d.decision == FitReviewStore.Limit && d.limit > 0 ? d.limit : it.limit;
                if (e.maxChars > 0 && !overwrite) { kept++; continue; }
                if (e.maxChars == limit) continue;
                e.maxChars = limit;
                changedSheets.Add(e.sheet);
                written++;
            }
            if (changedSheets.Count > 0) LocalizationSheetIO.SaveSheets(table, changedSheets);
        }

        var sb = new StringBuilder();
        sb.AppendLine("Measured " + st.measured + " localized texts. " + items.Count + " key(s) need a limit (readable floor " +
                      (Mathf.Clamp(readableRatio, 0.05f, 1f) * 100f).ToString("0") + "%, pseudo scale below " + pseudoScaleBelow.ToString("0.00") + ").");
        sb.AppendLine("Texts seen " + st.texts + ": " + st.instances + " nested-instance (counted at source), " + st.unkeyed + " without a LocalizedText key, " +
                      st.unknownKey + " with a key missing from the sheets (" + table.entries.Count + " entries loaded).");
        if (write) sb.AppendLine("Wrote " + written + " limit(s) to " + changedSheets.Count + " sheet(s); kept " + kept + " existing value(s); skipped " + approved + " approved.");
        else sb.AppendLine("Dry run, nothing written.");
        int enOver = items.Count(h => h.enOver);
        if (enOver > 0) sb.AppendLine(enOver + " key(s) marked EN_OVER: English already needs a size below the floor, so the limit is set to the English length. Widen these boxes.");
        int notInSheet = items.Count(h => !h.inSheet);
        if (notInSheet > 0) sb.AppendLine(notInSheet + " key(s) marked NOT_IN_SHEET: measured from the authored text; run Scan in the Localization window, then rerun with write=true.");
        sb.AppendLine();
        sb.AppendLine("key | sheet | max_chars | en | pseudo | pseudo scale | floor | review | where");
        foreach (var h in items.OrderBy(h => h.limit).ThenBy(h => h.key))
        {
            table.TryGet(h.key, out var e);
            var d = review.Get(h.key, h.source);
            sb.AppendLine(h.key + " | " + (e != null ? e.sheet : "NOT_IN_SHEET") + " | " + h.limit + (h.enOver ? " EN_OVER" : "") + " | " + h.enLen + " | " + h.pseudoLen +
                          " | " + h.pseudoScale.ToString("0.00") + " | " + h.floorRatio.ToString("0.00") + " | " + (d != null ? d.decision : "-") +
                          " | " + h.asset.Replace(folder + "/", "") + " : " + h.path);
        }
        return sb.ToString();
    }

    /// <summary>Scans prefabs under folder plus loaded scenes. Returns one item per key (its tightest occurrence), sorted by asset then path.</summary>
    public static List<CharFitItem> Collect(string folder, float readableRatio, float pseudoScaleBelow, LocalizationTable table, out CollectStats stats)
    {
        readableRatio = Mathf.Clamp(readableRatio, 0.05f, 1f);
        var settings = LocalizationSettings.Instance;
        float expansion = settings != null ? settings.pseudoExpansion : 0.4f;
        var hits = new List<CharFitItem>();
        var st = new CollectStats();

        void Visit(TMP_Text t, string asset)
        {
            st.texts++;
            if (PrefabUtility.IsPartOfPrefabInstance(t)) { st.instances++; return; }
            var lt = t.GetComponent<LocalizedText>();
            if (lt == null || string.IsNullOrEmpty(lt.key)) { st.unkeyed++; return; }
            // Keys not scanned into the sheets yet are measured from the authored text, reported, and written after the next Scan.
            bool inSheet = table.TryGet(lt.key, out var entry) && !string.IsNullOrEmpty(entry.source);
            if (!inSheet) st.unknownKey++;
            string src = inSheet ? entry.source : t.text;
            if (string.IsNullOrEmpty(src)) return;
            st.measured++;
            var h = Measure(t, lt.key, src, expansion, readableRatio, pseudoScaleBelow);
            if (h == null) return;
            h.inSheet = inSheet;
            h.asset = asset;
            h.path = TextFitActions.PathOf(t.transform);
            hits.Add(h);
        }

        try
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.Contains("/Editor/")) continue;
                if (i % 10 == 0) EditorUtility.DisplayProgressBar("Measure pseudo fit", path, i / (float)guids.Length);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || asset.GetComponentInChildren<LocalizedText>(true) == null) continue;

                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (contents.transform is RectTransform rootRt) LayoutRebuilder.ForceRebuildLayoutImmediate(rootRt);
                    foreach (var t in contents.GetComponentsInChildren<TMP_Text>(true)) Visit(t, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded || string.IsNullOrEmpty(scene.path)) continue;
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) Visit(t, scene.path);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        stats = st;
        // One item per key: the tightest place it appears.
        return hits.GroupBy(h => h.key).Select(g => g.OrderBy(h => h.limit).First())
                   .OrderBy(h => h.asset, StringComparer.OrdinalIgnoreCase).ThenBy(h => h.path, StringComparer.Ordinal).ToList();
    }

    private static CharFitItem Measure(TMP_Text t, string key, string src, float expansion, float readableRatio, float pseudoScaleBelow)
    {
        string visible = TextFitActions.Tags.Replace(src, "").Trim();
        if (visible.Length == 0 || !TextFitActions.Letters.IsMatch(visible) || t.GetComponentInParent<TMP_InputField>(true) != null) return null;

        string pseudo = PseudoLocalizer.Generate("pseudo", src, key, expansion);
        float design = t.enableAutoSizing ? t.fontSizeMax : t.fontSize;
        if (design <= 0f) return null;
        float ownMin = t.enableAutoSizing && t.fontSizeMin > 0f ? t.fontSizeMin : design;
        float floor = Mathf.Min(design, Mathf.Max(ownMin, design * readableRatio));
        bool wrap = t.textWrappingMode == TextWrappingModes.Normal || t.textWrappingMode == TextWrappingModes.PreserveWhitespace;

        bool wasAuto = t.enableAutoSizing;
        float wasSize = t.fontSize;
        try
        {
            t.enableAutoSizing = false;
            float width, height;
            bool layoutLine = false;

            if (TextFitActions.LayoutDriven(t, out float available))
            {
                // Only single-line layout labels with a bounded parent have a meaningful character budget.
                if (wrap || available <= 1f || t.GetComponentInParent<ScrollRect>(true) != null) return null;
                width = available;
                height = Mathf.Infinity;
                layoutLine = true;
            }
            else
            {
                width = t.rectTransform.rect.width;
                height = t.rectTransform.rect.height;
                var le = t.GetComponent<LayoutElement>();
                if (le != null && le.preferredWidth > 0f)
                {
                    width = le.preferredWidth;
                    if (le.preferredHeight > 0f) height = le.preferredHeight;
                    else if (height < 1f) { t.fontSize = design; height = t.GetPreferredValues(src, Mathf.Infinity, Mathf.Infinity).y; }
                }
                if (width < 1f || height < 1f) return null;
            }

            bool Fits(string text, float size)
            {
                t.fontSize = size;
                var p = t.GetPreferredValues(text, wrap ? width : Mathf.Infinity, Mathf.Infinity);
                return p.x <= width + 0.5f && p.y <= height + 0.5f;
            }

            float pseudoScale = TextFitActions.SmallestScale(t, pseudo, design, width, float.IsInfinity(height) ? 100000f : height, wrap);
            bool pseudoFitsFloor = Fits(pseudo, floor);
            if (pseudoFitsFloor && pseudoScale >= pseudoScaleBelow) return null;

            // Largest n where n visible characters (English words repeated) still fit at the floor size.
            int enLen = LocalizationValidator.VisibleLength(src);
            int lo = 0, hi = Mathf.Max(enLen * 4, 400);
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Fits(Filler(visible, mid), floor)) lo = mid; else hi = mid - 1;
            }

            // Single-line layout labels: preview them in a one-line box of the available width.
            if (layoutLine) { t.fontSize = design; height = t.GetPreferredValues(src, Mathf.Infinity, Mathf.Infinity).y; }

            var h = new CharFitItem
            {
                key = key,
                source = src,
                pseudo = pseudo,
                enLen = enLen,
                pseudoLen = LocalizationValidator.VisibleLength(pseudo),
                pseudoScale = pseudoScale,
                pseudoFitsFloor = pseudoFitsFloor,
                floorRatio = floor / design,
                limit = lo,

                font = t.font,
                material = t.fontSharedMaterial,
                fontStyle = t.fontStyle,
                alignment = t.alignment,
                wrapping = t.textWrappingMode,
                overflow = t.overflowMode,
                autoSize = wasAuto,
                design = design,
                fontSize = wasSize,
                fontSizeMin = t.fontSizeMin,
                fontSizeMax = t.fontSizeMax,
                floorSize = floor,
                characterSpacing = t.characterSpacing,
                wordSpacing = t.wordSpacing,
                lineSpacing = t.lineSpacing,
                paragraphSpacing = t.paragraphSpacing,
                margin = t.margin,
                box = new Vector2(width, height),
                layoutLine = layoutLine,
                isOrthographic = t.isOrthographic
            };
            if (h.limit < enLen) { h.enOver = true; h.limit = enLen; }
            return h;
        }
        finally
        {
            t.enableAutoSizing = wasAuto;
            t.fontSize = wasSize;
        }
    }

    /// <summary>The English text repeated (keeping its word breaks) and cut to exactly n characters.</summary>
    private static string Filler(string visible, int n)
    {
        if (n <= 0) return "";
        var sb = new StringBuilder(n + visible.Length + 1);
        while (sb.Length < n) { if (sb.Length > 0) sb.Append(' '); sb.Append(visible); }
        sb.Length = n;
        return sb.ToString().TrimEnd().PadRight(n, '.');
    }
}
