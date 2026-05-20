using UnityEngine;
using UnityEditor;

/// <summary>
/// One-shot editor utility that creates the two Guidebook RenderTexture assets
/// and their matching materials if they don't already exist.
/// Run via Tools > Create Guidebook Assets. Safe to run multiple times.
/// </summary>
public static class GuidebookAssetCreator
{
    private const string RtFolder   = "Assets/_GoodCopBadCop/_Textures/Render Textures";
    private const string MatFolder  = "Assets/_GoodCopBadCop/_Models/Guidebook/source";
    private const string TcpShaderGuid = "edd7abf643fa4bc4e8561d4c280c97cf";

    private const string RtHowToPlayPath = RtFolder + "/RT_GuidebookHowToPlay.renderTexture";
    private const string RtTasksPath     = RtFolder + "/RT_GuidebookTasks.renderTexture";
    private const string MatHowToPlayPath = MatFolder + "/M_GuidebookHowToPlay.mat";
    private const string MatTasksPath     = MatFolder + "/M_GuidebookTasks.mat";

    [MenuItem("Tools/Create Guidebook Assets")]
    public static void CreateAll()
    {
        RenderTexture rtHowToPlay = GetOrCreateRenderTexture(RtHowToPlayPath);
        RenderTexture rtTasks     = GetOrCreateRenderTexture(RtTasksPath);

        GetOrCreateMaterial(MatHowToPlayPath, rtHowToPlay);
        GetOrCreateMaterial(MatTasksPath, rtTasks);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[GuidebookAssetCreator] All guidebook assets created successfully.");
    }

    private static RenderTexture GetOrCreateRenderTexture(string path)
    {
        RenderTexture existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
        if (existing != null)
        {
            Debug.Log($"[GuidebookAssetCreator] RenderTexture already exists at {path}, skipping.");
            return existing;
        }

        RenderTexture rt = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32);
        rt.name         = System.IO.Path.GetFileNameWithoutExtension(path);
        rt.antiAliasing = 1;
        rt.useMipMap    = false;
        rt.wrapMode     = TextureWrapMode.Clamp;
        rt.filterMode   = FilterMode.Bilinear;
        rt.Create();

        AssetDatabase.CreateAsset(rt, path);
        Debug.Log($"[GuidebookAssetCreator] Created RenderTexture at {path}");
        return rt;
    }

    private static void GetOrCreateMaterial(string path, RenderTexture rt)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            // Make sure the RT is wired even if the mat already exists.
            existing.SetTexture("_BaseMap", rt);
            EditorUtility.SetDirty(existing);
            Debug.Log($"[GuidebookAssetCreator] Material already exists at {path}, updated _BaseMap.");
            return;
        }

        string shaderPath = AssetDatabase.GUIDToAssetPath(TcpShaderGuid);
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);

        if (shader == null)
        {
            Debug.LogError($"[GuidebookAssetCreator] Could not find TCP2 shader at path resolved from GUID {TcpShaderGuid}. Material not created.");
            return;
        }

        // Clone blend/transparency settings from PaperContents.mat.
        Material mat = new Material(shader);
        mat.name = System.IO.Path.GetFileNameWithoutExtension(path);
        mat.SetTexture("_BaseMap", rt);
        mat.SetFloat("_Surface",      1f);   // Transparent
        mat.SetFloat("_Blend",        0f);   // Alpha blend
        mat.SetFloat("_SrcBlend",     1f);   // One
        mat.SetFloat("_DstBlend",     10f);  // OneMinusSrcAlpha
        mat.SetFloat("_ZWrite",       0f);
        mat.SetFloat("_Cull",         0f);   // Off (double-sided)
        mat.renderQueue = 3000;
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.EnableKeyword("TCP2_REFLECTIONS_FRESNEL");
        mat.EnableKeyword("TCP2_RIM_LIGHTING_LIGHTMASK");
        mat.EnableKeyword("TCP2_SHADOW_LIGHT_COLOR");

        AssetDatabase.CreateAsset(mat, path);
        Debug.Log($"[GuidebookAssetCreator] Created material at {path}");
    }
}
