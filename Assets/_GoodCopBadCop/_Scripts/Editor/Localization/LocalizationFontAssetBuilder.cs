using System;
using System.IO;
using Bezi;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Creates Dynamic, multi-atlas TMP font assets for language fonts (Assets/_GoodCopBadCop/_Fonts/Fallback/*.ttf|otf).
/// Optionally clones a template font asset's material (shader + properties), so world-space "3D Lit" variants keep
/// their lit shader while sampling the new atlas.
/// </summary>
public static class LocalizationFontAssetBuilder
{
    private static readonly string[] AtlasBoundProperties =
    {
        "_MainTex", "_TextureWidth", "_TextureHeight", "_GradientScale", "_WeightNormal", "_WeightBold"
    };

    [BeziAction(
        "Create a Dynamic multi-atlas TMP font asset from a .ttf/.otf font file (paths start with Assets/). " +
        "templateFontAssetPath (optional) copies that font asset's material shader and properties, e.g. a '3D Lit' world font. " +
        "Overwrites nothing: fails if outputAssetPath exists. Returns the created asset path.")]
    public static string CreateDynamicFontAsset(
        string sourceFontPath,
        string outputAssetPath,
        string templateFontAssetPath = "",
        int samplingPointSize = 90,
        int atlasPadding = 9,
        int atlasSize = 2048)
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(sourceFontPath);
        if (font == null && File.Exists(sourceFontPath))
        {
            // Freshly downloaded files may not be imported yet.
            AssetDatabase.ImportAsset(sourceFontPath, ImportAssetOptions.ForceSynchronousImport);
            font = AssetDatabase.LoadAssetAtPath<Font>(sourceFontPath);
        }
        if (font == null) throw new ArgumentException("No font file at " + sourceFontPath);
        if (string.IsNullOrEmpty(outputAssetPath) || !outputAssetPath.EndsWith(".asset"))
            throw new ArgumentException("outputAssetPath must end with .asset");
        if (AssetDatabase.LoadMainAssetAtPath(outputAssetPath) != null)
            throw new InvalidOperationException("Asset already exists: " + outputAssetPath);

        TMP_FontAsset template = null;
        if (!string.IsNullOrEmpty(templateFontAssetPath))
        {
            template = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(templateFontAssetPath);
            if (template == null) throw new ArgumentException("No TMP font asset at " + templateFontAssetPath);
        }

        string dir = Path.GetDirectoryName(outputAssetPath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir)) CreateFolderRecursive(dir);

        var fontAsset = TMP_FontAsset.CreateFontAsset(
            font, samplingPointSize, atlasPadding, GlyphRenderMode.SDFAA, atlasSize, atlasSize,
            AtlasPopulationMode.Dynamic, true);
        if (fontAsset == null) throw new InvalidOperationException("TMP could not create a font asset from " + sourceFontPath);

        string name = Path.GetFileNameWithoutExtension(outputAssetPath);
        fontAsset.name = name;
        AssetDatabase.CreateAsset(fontAsset, outputAssetPath);

        var atlas = fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0 ? fontAsset.atlasTextures[0] : null;
        if (atlas != null)
        {
            atlas.name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(atlas, fontAsset);
        }

        var material = fontAsset.material;
        if (material != null)
        {
            material.name = name + " Material";
            if (template != null && template.material != null)
            {
                CopyTemplateMaterial(material, template.material);
                if (atlas != null && material.HasProperty("_MainTex")) material.SetTexture("_MainTex", atlas);
            }
            AssetDatabase.AddObjectToAsset(material, fontAsset);
        }

        var so = new SerializedObject(fontAsset);
        var clear = so.FindProperty("m_ClearDynamicDataOnBuild");
        if (clear != null) clear.boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(outputAssetPath);
        return outputAssetPath;
    }

    private static void CopyTemplateMaterial(Material target, Material template)
    {
        var saved = new object[AtlasBoundProperties.Length];
        for (int i = 0; i < AtlasBoundProperties.Length; i++)
        {
            string p = AtlasBoundProperties[i];
            if (!target.HasProperty(p)) continue;
            saved[i] = p == "_MainTex" ? (object)target.GetTexture(p) : target.GetFloat(p);
        }

        target.shader = template.shader;
        target.CopyPropertiesFromMaterial(template);
        target.shaderKeywords = template.shaderKeywords;

        for (int i = 0; i < AtlasBoundProperties.Length; i++)
        {
            string p = AtlasBoundProperties[i];
            if (saved[i] == null || !target.HasProperty(p)) continue;
            if (saved[i] is Texture tex) target.SetTexture(p, tex);
            else target.SetFloat(p, (float)saved[i]);
        }
    }

    private static void CreateFolderRecursive(string folder)
    {
        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) CreateFolderRecursive(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
