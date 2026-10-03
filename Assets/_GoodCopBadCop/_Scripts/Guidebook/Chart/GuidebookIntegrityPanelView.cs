using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Example state shown by the guidebook's printed copy of the HUD Checkpoint Integrity panel.</summary>
[Serializable]
public class GuidebookIntegrityPanel
{
    [Range(0, 100)] public int Percent = 100;
    [Min(0)] public int FenceCount;
    [Min(0)] public int TrashCount;
    [Min(0)] public int GraffitiCount;
}

/// <summary>
/// Prints the real HUD Checkpoint Integrity panel onto a guidebook face. The first build copies the
/// scene's <see cref="CheckpointIntegrityBar"/> panel (frame, fonts, squares, counters), strips its
/// live-data behaviours, and swaps every material for a lit one (images: the face illustration's lit
/// material; text: the page font's lit material re-pointed at each font's atlas), so it looks like
/// the HUD but is lit like the page. The copy's own <see cref="CheckpointIntegrityBar"/> renders the
/// example score with the HUD's colors via <see cref="CheckpointIntegrityBar.ShowPreview"/>.
/// Created at runtime by <see cref="GuidebookFaceView"/>.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class GuidebookIntegrityPanelView : MonoBehaviour
{
    private static readonly Dictionary<(TMP_FontAsset, Material), Material> s_litFontMaterials =
        new Dictionary<(TMP_FontAsset, Material), Material>();

    private TMP_Text _style;
    private Material _imageMaterial;

    private RectTransform          _copy;
    private CheckpointIntegrityBar _bar;
    private TMP_Text _fenceCount, _trashCount, _graffitiCount;
    private bool _searched;

    private RectTransform Rect => (RectTransform)transform;

    public void Configure(TMP_Text style, Material imageMaterial)
    {
        _style         = style;
        _imageMaterial = imageMaterial;
    }

    public void Clear() => gameObject.SetActive(false);

    /// <summary>Shows the panel at <paramref name="width"/>; returns its height (0 if there is no HUD panel to copy).</summary>
    public float Build(GuidebookIntegrityPanel state, float width)
    {
        gameObject.SetActive(true);
        if (!EnsureCopy())
        {
            gameObject.SetActive(false);
            return 0f;
        }

        Vector2 size = _copy.rect.size;
        float scale = size.x > 0f ? width / size.x : 1f;
        _copy.localScale = new Vector3(scale, scale, 1f);
        Rect.sizeDelta = new Vector2(width, size.y * scale);

        SetCount(_fenceCount,    state.FenceCount);
        SetCount(_trashCount,    state.TrashCount);
        SetCount(_graffitiCount, state.GraffitiCount);
        if (_bar != null) _bar.ShowPreview(state.Percent / 100f);

        LayoutRebuilder.ForceRebuildLayoutImmediate(_copy);
        return size.y * scale;
    }

    private static void SetCount(TMP_Text text, int count)
    {
        if (text != null) text.text = count.ToString();
    }

    // ── Copy ──────────────────────────────────────────────────────────────────

    private bool EnsureCopy()
    {
        if (_copy != null) return true;
        if (_searched) return false;
        _searched = true;

        CheckpointIntegrityBar source = FindFirstObjectByType<CheckpointIntegrityBar>(FindObjectsInactive.Include);
        if (source == null)
        {
            Debug.LogWarning("[GuidebookIntegrityPanelView] No CheckpointIntegrityBar in the scene to copy; the guidebook panel is hidden.", this);
            return false;
        }

        // Instantiate under an inactive holder so no Awake/OnEnable runs before the copy is cleaned up.
        var holder = new GameObject("Integrity Panel Holder", typeof(RectTransform));
        holder.SetActive(false);
        holder.transform.SetParent(transform, false);

        GameObject copy = Instantiate(source.gameObject, holder.transform, false);
        copy.name = "HUD Panel Copy";
        copy.SetActive(true);

        StripBehaviours(copy);
        _bar = copy.GetComponent<CheckpointIntegrityBar>();
        if (_bar != null) _bar.MarkAsPreview();

        int layer = gameObject.layer;
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

        foreach (Graphic g in copy.GetComponentsInChildren<Graphic>(true))
        {
            g.raycastTarget = false;
            if (g is TMP_Text text) text.fontSharedMaterial = LitFontMaterial(text.font);
            else if (_imageMaterial != null) g.material = _imageMaterial;
        }

        // Counters: always show every row (the HUD may hide finished categories).
        _fenceCount    = FindCount(copy.transform, "Fence Row");
        _trashCount    = FindCount(copy.transform, "Trash Row");
        _graffitiCount = FindCount(copy.transform, "Graffiti Row");

        _copy = (RectTransform)copy.transform;
        Vector2 hudSize = ((RectTransform)source.transform).rect.size; // may be stretched by the HUD layout
        _copy.SetParent(transform, false);
        _copy.anchorMin = _copy.anchorMax = new Vector2(0.5f, 1f);
        _copy.pivot = new Vector2(0.5f, 1f);
        _copy.sizeDelta = hudSize;
        _copy.anchoredPosition = Vector2.zero;
        _copy.localRotation = Quaternion.identity;
        Destroy(holder);
        return true;
    }

    /// <summary>Removes gameplay/HUD scripts, keeping UI graphics, layout and mesh effects.</summary>
    private static void StripBehaviours(GameObject root)
    {
        foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb is UIBehaviour || mb is CheckpointIntegrityBar) continue;
            DestroyImmediate(mb);
        }
        foreach (Canvas c in root.GetComponentsInChildren<Canvas>(true)) c.overrideSorting = false;
    }

    private static TMP_Text FindCount(Transform root, string rowName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != rowName) continue;
            t.gameObject.SetActive(true);
            Transform count = t.Find("Count");
            return count != null ? count.GetComponent<TMP_Text>() : null;
        }
        return null;
    }

    /// <summary>
    /// The page font's lit material re-pointed at <paramref name="font"/>'s atlas, so HUD fonts
    /// (e.g. Antonio) keep their look but respond to scene lighting like the page text.
    /// </summary>
    private Material LitFontMaterial(TMP_FontAsset font)
    {
        Material lit = _style != null ? _style.fontSharedMaterial : null;
        if (font == null || lit == null) return font != null ? font.material : lit;
        if (_style.font == font) return lit;

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
