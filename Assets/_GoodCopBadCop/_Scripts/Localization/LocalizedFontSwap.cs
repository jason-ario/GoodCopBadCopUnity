using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Per-label font swap state. Remembers the label's own font and material the first time it is swapped away from
/// them, so switching back restores the exact authored look. When the label used a material preset (outline, lit,
/// a runtime copy) instead of its font's default material, the preset is cloned onto the new font's atlas so the
/// effect survives the swap. If other code changes the font while swapped, that new font becomes the original.
/// </summary>
public sealed class LocalizedFontSwap
{
    private static readonly int[] AtlasBoundIds =
    {
        ShaderUtilities.ID_TextureWidth, ShaderUtilities.ID_TextureHeight, ShaderUtilities.ID_GradientScale,
        ShaderUtilities.ID_WeightNormal, ShaderUtilities.ID_WeightBold
    };

    // (preset material, target font) -> clone. Shared by every label using the same preset.
    private static readonly Dictionary<(Material, TMP_FontAsset), Material> PresetClones = new Dictionary<(Material, TMP_FontAsset), Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { PresetClones.Clear(); }

    private TMP_FontAsset _originalFont;
    private Material _originalMaterial;
    private TMP_FontAsset _appliedFont;

    public TMP_FontAsset OriginalFont => _originalFont;

    /// <summary>Remembers the label's current font as its original (call before the first <see cref="Apply"/>).</summary>
    public void Capture(TMP_Text t)
    {
        if (t == null) return;
        _originalFont = t.font;
        _originalMaterial = t.fontSharedMaterial;
        _appliedFont = null;
    }

    /// <summary>Shows <paramref name="target"/> (null = the original font) on <paramref name="t"/>.</summary>
    public void Apply(TMP_Text t, TMP_FontAsset target)
    {
        if (t == null) return;
        if (_originalFont == null) Capture(t);

        // Someone else assigned a font since our last swap: adopt it as the new original.
        if (_appliedFont != null && t.font != _appliedFont) Capture(t);

        if (target == null || target == _originalFont)
        {
            if (_appliedFont != null)
            {
                if (t.font != _originalFont) t.font = _originalFont;
                if (_originalMaterial != null && t.fontSharedMaterial != _originalMaterial) t.fontSharedMaterial = _originalMaterial;
            }
            _appliedFont = null;
            return;
        }

        if (_appliedFont == null)
        {
            // Swapping away from the original: record its current material (it may have been set after Capture).
            _originalFont = t.font;
            _originalMaterial = t.fontSharedMaterial;
        }

        if (t.font != target) t.font = target;
        var material = MaterialFor(target);
        if (material != null && t.fontSharedMaterial != material) t.fontSharedMaterial = material;
        _appliedFont = target;
    }

    private Material MaterialFor(TMP_FontAsset target)
    {
        if (_originalMaterial == null || _originalFont == null || _originalMaterial == _originalFont.material)
            return target.material;

        var key = (_originalMaterial, target);
        if (PresetClones.TryGetValue(key, out var clone) && clone != null) return clone;

        clone = new Material(_originalMaterial) { name = _originalMaterial.name + " (" + target.name + ")" };
        clone.SetTexture(ShaderUtilities.ID_MainTex, target.atlasTexture);
        var source = target.material;
        if (source != null)
            foreach (int id in AtlasBoundIds)
                if (clone.HasProperty(id) && source.HasProperty(id)) clone.SetFloat(id, source.GetFloat(id));
        PresetClones[key] = clone;
        return clone;
    }
}
