using UnityEngine;
using UnityEditor;

public static class UpdateWobbleSettings
{
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