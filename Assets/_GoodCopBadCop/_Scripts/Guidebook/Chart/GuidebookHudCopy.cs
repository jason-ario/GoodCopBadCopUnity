using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Shared helper for printing real HUD elements onto guidebook faces. Copies a HUD hierarchy,
/// strips its gameplay behaviours, and swaps every material for a lit one (images: the face
/// illustration's lit material; text: the page font's lit material re-pointed at each font's atlas)
/// so the copy looks like the HUD but is lit like the paper.
/// Used by <see cref="GuidebookIntegrityPanelView"/> and <see cref="GuidebookHudArtView"/>.
/// </summary>
public static class GuidebookHudCopy
{
    private static readonly Dictionary<(TMP_FontAsset, Material), Material> s_litFontMaterials =
        new Dictionary<(TMP_FontAsset, Material), Material>();

    private static readonly Vector3[] s_corners = new Vector3[4];

    /// <summary>
    /// Copies <paramref name="source"/> under <paramref name="parent"/>, anchored top-centre at its HUD size.
    /// The copy is built under an inactive holder, so no Awake/OnEnable runs until after the cleanup and
    /// <paramref name="beforeEnable"/> (e.g. to mark a kept behaviour as a preview).
    /// </summary>
    /// <param name="keep">Non-UI behaviours to keep; everything else that isn't a <see cref="UIBehaviour"/> is destroyed.</param>
    public static RectTransform Create(GameObject source, Transform parent, TMP_Text style, Material imageMaterial,
                                       Func<MonoBehaviour, bool> keep = null, Action<GameObject> beforeEnable = null)
    {
        var holder = new GameObject("HUD Copy Holder", typeof(RectTransform));
        holder.SetActive(false);
        holder.transform.SetParent(parent, false);

        GameObject copy = Object.Instantiate(source, holder.transform, false);
        copy.name = source.name + " (Guidebook Copy)";
        copy.SetActive(true);

        foreach (MonoBehaviour mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb is UIBehaviour || (keep != null && keep(mb))) continue;
            Object.DestroyImmediate(mb);
        }
        foreach (Canvas c in copy.GetComponentsInChildren<Canvas>(true)) c.overrideSorting = false;
        // The HUD fades elements in/out with CanvasGroups; the printed copy is always fully visible.
        foreach (CanvasGroup g in copy.GetComponentsInChildren<CanvasGroup>(true)) g.alpha = 1f;

        int layer = parent.gameObject.layer;
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

        foreach (Graphic g in copy.GetComponentsInChildren<Graphic>(true))
        {
            g.raycastTarget = false;
            if (g is TMP_Text text) text.fontSharedMaterial = LitFontMaterial(text.font, style);
            else if (imageMaterial != null) g.material = imageMaterial;
        }

        beforeEnable?.Invoke(copy);

        var rt = (RectTransform)copy.transform;
        Vector2 hudSize = ((RectTransform)source.transform).rect.size; // may be stretched by the HUD layout
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = hudSize;
        rt.anchoredPosition = Vector2.zero;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;
        Object.Destroy(holder);
        return rt;
    }

    /// <summary>
    /// Scales <paramref name="copy"/> uniformly so its visible graphics fit in <paramref name="width"/> x
    /// <paramref name="maxHeight"/>, then moves it so those graphics are centred horizontally with their top
    /// at <paramref name="space"/>'s origin (its pivot, which must be top-centre). Returns the height used.
    /// Uses drawn content rather than the root rect, because HUD art often overhangs its RectTransform.
    /// </summary>
    public static float FitVisible(RectTransform copy, RectTransform space, float width, float maxHeight)
    {
        copy.localScale = Vector3.one;
        copy.anchoredPosition = Vector2.zero;

        Rect b = VisibleBounds(copy, space);
        if (b.width <= 0f || b.height <= 0f) return 0f;

        float s = Mathf.Min(width / b.width, maxHeight / b.height);
        copy.localScale = new Vector3(s, s, 1f);

        b = VisibleBounds(copy, space);
        copy.anchoredPosition = new Vector2(-b.center.x, -b.yMax);
        return b.height;
    }

    /// <summary>Bounds of every active, visible graphic under <paramref name="root"/> in <paramref name="space"/> local space.</summary>
    public static Rect VisibleBounds(RectTransform root, RectTransform space)
    {
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        bool any = false;

        foreach (Graphic g in root.GetComponentsInChildren<Graphic>(false))
        {
            if (!g.enabled || g.color.a <= 0.01f) continue;

            if (g is TMP_Text text)
            {
                // Text rects are often far wider than the glyphs (stretched labels); measure the glyphs.
                if (string.IsNullOrEmpty(text.text)) continue;
                text.ForceMeshUpdate();
                Bounds tb = text.textBounds;
                if (tb.size.x <= 0f || tb.size.y <= 0f) continue;
                s_corners[0] = text.rectTransform.TransformPoint(new Vector3(tb.min.x, tb.min.y));
                s_corners[1] = text.rectTransform.TransformPoint(new Vector3(tb.min.x, tb.max.y));
                s_corners[2] = text.rectTransform.TransformPoint(new Vector3(tb.max.x, tb.max.y));
                s_corners[3] = text.rectTransform.TransformPoint(new Vector3(tb.max.x, tb.min.y));
            }
            else
            {
                g.rectTransform.GetWorldCorners(s_corners);
            }

            foreach (Vector3 c in s_corners)
            {
                Vector3 p = space.InverseTransformPoint(c);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            any = true;
        }
        return any ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : Rect.zero;
    }

    /// <summary>
    /// The page font's lit material re-pointed at <paramref name="font"/>'s atlas, so HUD fonts
    /// (e.g. Antonio) keep their look but respond to scene lighting like the page text.
    /// </summary>
    public static Material LitFontMaterial(TMP_FontAsset font, TMP_Text style)
    {
        Material lit = style != null ? style.fontSharedMaterial : null;
        if (font == null || lit == null) return font != null ? font.material : lit;
        if (style.font == font) return lit;

        if (s_litFontMaterials.TryGetValue((font, lit), out Material cached) && cached != null) return cached;

        Material src = font.material;
        var m = new Material(lit) { name = $"{font.name} Lit (Guidebook)" };
        m.SetTexture(ShaderUtilities.ID_MainTex, src.GetTexture(ShaderUtilities.ID_MainTex));
        CopyFloat(src, m, ShaderUtilities.ID_GradientScale);
        CopyFloat(src, m, ShaderUtilities.ID_TextureWidth);
        CopyFloat(src, m, ShaderUtilities.ID_TextureHeight);
        CopyFloat(src, m, ShaderUtilities.ID_WeightNormal);
        CopyFloat(src, m, ShaderUtilities.ID_WeightBold);
        CopyFloat(src, m, ShaderUtilities.ID_ScaleRatio_A);
        CopyFloat(src, m, ShaderUtilities.ID_ScaleRatio_B);
        CopyFloat(src, m, ShaderUtilities.ID_ScaleRatio_C);
        s_litFontMaterials[(font, lit)] = m;
        return m;
    }

    private static void CopyFloat(Material from, Material to, int id)
    {
        if (from.HasProperty(id) && to.HasProperty(id)) to.SetFloat(id, from.GetFloat(id));
    }
}
