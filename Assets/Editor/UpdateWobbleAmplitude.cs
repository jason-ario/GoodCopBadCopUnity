using UnityEngine;
using UnityEditor;

public static class UpdateWobbleSettings
{
    private static readonly string[] SnapShaderNames =
    {
        "Toony Colors Pro 2/User/Sketch Shader",
        "Toony Colors Pro 2/User/Character Shader",
    };

    /// <summary>
    /// Sets _SnapResolution to 1024 on all materials using the Sketch or Character shaders.
    /// </summary>
    [MenuItem("Tools/Vertex Snap/Set Snap Resolution to 1024")]
    public static void ApplySnapResolution()
    {
        const float snapResolution = 1024f;
        const string propertyName  = "_SnapResolution";

        string[] guids = AssetDatabase.FindAssets("t:Material");
        int updated = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;

            bool isTargetShader = System.Array.IndexOf(SnapShaderNames, mat.shader.name) >= 0;
            if (!isTargetShader) continue;

            if (!mat.HasProperty(propertyName)) continue;

            float current = mat.GetFloat(propertyName);
            if (Mathf.Approximately(current, snapResolution)) continue;

            Undo.RecordObject(mat, "Set Snap Resolution");
            mat.SetFloat(propertyName, snapResolution);
            EditorUtility.SetDirty(mat);
            updated++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[VertexSnap] Updated {updated} material(s) to _SnapResolution = {snapResolution}.");
    }

    [MenuItem("Tools/Wobble/Apply Default Wobble Settings")]
    public static void ApplyWobbleSettings()
    {
        const string targetShaderName = "Toony Colors Pro 2/User/Sketch Shader";

        const float amplitude = 0.001f;
        const float frequency = 15f;
        const float speed = 15f;

        string[] guids = AssetDatabase.FindAssets("t:Material");
        int updated = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null)
                continue;

            if (mat.shader.name != targetShaderName)
                continue;

            if (!mat.HasProperty("_WobbleAmplitude"))
                continue;

            Undo.RecordObject(mat, "Update Wobble Settings");

            mat.SetFloat("_WobbleAmplitude", amplitude);
            mat.SetFloat("_WobbleFrequency", frequency);
            mat.SetFloat("_WobbleSpeed", speed);

            EditorUtility.SetDirty(mat);
            updated++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Updated {updated} materials:\nAmplitude={amplitude}, Frequency={frequency}, Speed={speed}");
    }
}