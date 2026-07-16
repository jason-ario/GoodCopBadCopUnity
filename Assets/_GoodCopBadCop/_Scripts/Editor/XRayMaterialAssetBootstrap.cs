#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.Editor
{
    /// <summary>
    /// Keeps the authored X-ray material assets linked to the procedural X-ray shader.
    /// The work is delayed until the AssetDatabase is ready, so Unity owns all .meta creation.
    /// </summary>
    [InitializeOnLoad]
    internal static class XRayMaterialAssetBootstrap
    {
        private const string ShaderPath = "Assets/_GoodCopBadCop/_Shaders/XRayAnatomy.shader";
        private const string BodyMaterialPath = "Assets/_GoodCopBadCop/_Materials/X Ray.mat";
        private const string AnatomyMaterialPath = "Assets/_GoodCopBadCop/_Materials/X Ray Anatomy.mat";
        private const string AnomalyMaterialPath = "Assets/_GoodCopBadCop/_Materials/X Ray Anomaly.mat";

        static XRayMaterialAssetBootstrap()
        {
            EditorApplication.delayCall += EnsureMaterials;
        }

        private static void EnsureMaterials()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
                return;

            Material body = LoadOrCreate(BodyMaterialPath, shader, out bool configureBody);
            Material anatomy = LoadOrCreate(AnatomyMaterialPath, shader, out bool configureAnatomy);
            Material anomaly = LoadOrCreate(AnomalyMaterialPath, shader, out bool configureAnomaly);
            if (body == null || anatomy == null || anomaly == null)
                return;

            // These are project-owned X-ray assets. Configure on every domain reload so newly
            // added shader properties also reach assets created by an earlier version.
            ConfigureBody(body);
            ConfigureAnatomy(anatomy);
            ConfigureAnomaly(anomaly);
            AssetDatabase.SaveAssets();
        }

        private static Material LoadOrCreate(string path, Shader shader, out bool requiresConfiguration)
        {
            requiresConfiguration = false;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                if (material.shader != shader)
                {
                    material.shader = shader;
                    requiresConfiguration = true;
                }
                return material;
            }

            material = new Material(shader)
            {
                name = System.IO.Path.GetFileNameWithoutExtension(path)
            };
            AssetDatabase.CreateAsset(material, path);
            requiresConfiguration = true;
            return material;
        }

        private static void ConfigureBody(Material material)
        {
            material.SetFloat("_Mode", 0f);
            material.SetColor("_Color", new Color(0.08f, 0.32f, 0.48f, 1f));
            material.SetColor("_EmissionColor", new Color(0.34f, 0.9f, 1f, 1f));
            material.SetFloat("_Alpha", 0.2f);
            material.SetFloat("_RimPower", 2.2f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
        }

        private static void ConfigureAnatomy(Material material)
        {
            material.SetFloat("_Mode", 1f);
            material.SetColor("_EmissionColor", new Color(0.62f, 0.96f, 1f, 1f));
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10;
            EditorUtility.SetDirty(material);
        }

        private static void ConfigureAnomaly(Material material)
        {
            material.SetFloat("_Mode", 2f);
            material.SetColor("_RimColor", new Color(0.85f, 0.05f, 1f, 1f));
            material.SetFloat("_RimPower", 2.4f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 11;
            EditorUtility.SetDirty(material);
        }
    }
}
#endif
