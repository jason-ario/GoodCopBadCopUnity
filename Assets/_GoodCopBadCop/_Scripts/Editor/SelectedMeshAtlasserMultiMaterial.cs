using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class SelectedMeshAtlasserMultiMaterial
{
    private const int AtlasSize = 4096;
    private const int Padding = 8;

    [MenuItem("Tools/Atlas/Combine Selected Objects Into Atlas (Final)")]
    private static void CombineSelectedObjectsIntoAtlas()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            EditorUtility.DisplayDialog("Atlas Tool", "Select one or more GameObjects first.", "OK");
            return;
        }

        var entries = new List<Entry>();
        var uniqueTextures = new List<Texture2D>();
        var textureToAtlasIndex = new Dictionary<Texture2D, int>();
        var readableTexturePaths = new HashSet<string>();

        foreach (GameObject go in selected)
        {
            if (go == null)
                continue;

            MeshFilter mf = go.GetComponent<MeshFilter>();
            MeshRenderer mr = go.GetComponent<MeshRenderer>();

            if (mf == null || mr == null)
            {
                Debug.LogWarning($"Skipping {go.name}: needs MeshFilter + MeshRenderer.");
                continue;
            }

            Mesh sourceMesh = mf.sharedMesh;
            if (sourceMesh == null)
            {
                Debug.LogWarning($"Skipping {go.name}: no shared mesh.");
                continue;
            }

            Material[] materials = mr.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                Debug.LogWarning($"Skipping {go.name}: no shared materials.");
                continue;
            }

            int subMeshCount = sourceMesh.subMeshCount;
            if (subMeshCount == 0)
            {
                Debug.LogWarning($"Skipping {go.name}: mesh has no submeshes.");
                continue;
            }

            if (materials.Length < subMeshCount)
            {
                Debug.LogWarning($"Skipping {go.name}: material count ({materials.Length}) is less than submesh count ({subMeshCount}).");
                continue;
            }

            int[] submeshTextureIndices = new int[subMeshCount];
            bool[] submeshIsTiling = new bool[subMeshCount];
            bool valid = true;

            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                Material mat = materials[subMeshIndex];
                if (mat == null)
                {
                    Debug.LogWarning($"Skipping {go.name}: submesh {subMeshIndex} material is null.");
                    valid = false;
                    break;
                }

                Texture tex = GetMainTexture(mat);
                if (!(tex is Texture2D tex2D))
                {
                    Debug.LogWarning($"Skipping {go.name}: material '{mat.name}' has no valid main texture.");
                    valid = false;
                    break;
                }

                string texPath = AssetDatabase.GetAssetPath(tex2D);
                if (string.IsNullOrEmpty(texPath))
                {
                    Debug.LogWarning($"Skipping {go.name}: texture '{tex2D.name}' is not a project asset.");
                    valid = false;
                    break;
                }

                readableTexturePaths.Add(texPath);

                if (!textureToAtlasIndex.TryGetValue(tex2D, out int atlasIndex))
                {
                    atlasIndex = uniqueTextures.Count;
                    uniqueTextures.Add(tex2D);
                    textureToAtlasIndex.Add(tex2D, atlasIndex);
                }

                submeshTextureIndices[subMeshIndex] = atlasIndex;
                submeshIsTiling[subMeshIndex] = IsSubmeshTiling(sourceMesh, subMeshIndex, mat);
            }

            if (!valid)
                continue;

            entries.Add(new Entry
            {
                gameObject = go,
                meshFilter = mf,
                meshRenderer = mr,
                sourceMesh = sourceMesh,
                sourceMaterials = materials,
                submeshTextureIndices = submeshTextureIndices,
                submeshIsTiling = submeshIsTiling
            });
        }

        if (entries.Count == 0)
        {
            EditorUtility.DisplayDialog("Atlas Tool", "No valid objects found.", "OK");
            return;
        }

        foreach (string path in readableTexturePaths)
            EnsureTextureReadable(path);

        string outputFolder = EditorUtility.SaveFolderPanel("Choose Output Folder", "Assets", "AtlasOutput");
        if (string.IsNullOrEmpty(outputFolder))
            return;

        string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        outputFolder = Path.GetFullPath(outputFolder);

        if (!outputFolder.StartsWith(projectPath))
        {
            EditorUtility.DisplayDialog("Atlas Tool", "Output folder must be inside this Unity project.", "OK");
            return;
        }

        string relativeOutputFolder = outputFolder
            .Replace("\\", "/")
            .Replace(projectPath.Replace("\\", "/") + "/", "");

// Ensure it starts with Assets/
        if (!relativeOutputFolder.StartsWith("Assets"))
        {
            relativeOutputFolder = "Assets/" + relativeOutputFolder.TrimStart('/');
        }
        
        if (!AssetDatabase.IsValidFolder(relativeOutputFolder))
        {
            AssetDatabase.Refresh();

            if (!AssetDatabase.IsValidFolder(relativeOutputFolder))
            {
                EditorUtility.DisplayDialog("Atlas Tool", $"Invalid output folder inside Assets:\n{relativeOutputFolder}", "OK");
                return;
            }
        }

        Texture2D atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Rect[] atlasRects = atlas.PackTextures(uniqueTextures.ToArray(), Padding, AtlasSize, false);

        string atlasPngPath = $"{relativeOutputFolder}/CombinedAtlas.png";
        File.WriteAllBytes(atlasPngPath, atlas.EncodeToPNG());
        AssetDatabase.ImportAsset(atlasPngPath, ImportAssetOptions.ForceUpdate);

        TextureImporter atlasImporter = AssetImporter.GetAtPath(atlasPngPath) as TextureImporter;
        if (atlasImporter != null)
        {
            atlasImporter.textureType = TextureImporterType.Default;
            atlasImporter.alphaIsTransparency = true;
            atlasImporter.mipmapEnabled = true;
            atlasImporter.wrapMode = TextureWrapMode.Clamp;
            atlasImporter.SaveAndReimport();
        }

        Texture2D atlasAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPngPath);

        Material atlasMaterial = new Material(entries[0].sourceMaterials[0]);
        atlasMaterial.name = "MAT_CombinedAtlas";

        AssignMainTexture(atlasMaterial, atlasAsset);
        ResetTextureScaleAndOffset(atlasMaterial);

        string materialPath = $"{relativeOutputFolder}/MAT_CombinedAtlas.mat";
        AssetDatabase.CreateAsset(atlasMaterial, materialPath);

        Material savedMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];

            Mesh rebuiltMesh = BuildAtlasedSingleSubmeshMesh(
                entry.sourceMesh,
                entry.sourceMaterials,
                entry.submeshTextureIndices,
                entry.submeshIsTiling,
                atlasRects,
                atlasAsset.width,
                atlasAsset.height);

            rebuiltMesh.name = entry.sourceMesh.name + "_Atlased";

            string safeName = MakeSafeFileName(entry.gameObject.name);
            string meshPath = $"{relativeOutputFolder}/{safeName}_Atlased.asset";
            AssetDatabase.CreateAsset(rebuiltMesh, meshPath);

            Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            Undo.RecordObject(entry.meshFilter, "Assign Atlased Mesh");
            Undo.RecordObject(entry.meshRenderer, "Assign Atlased Material");

            entry.meshFilter.sharedMesh = savedMesh;
            entry.meshRenderer.sharedMaterials = new[] { savedMaterial };

            PrefabUtility.RecordPrefabInstancePropertyModifications(entry.meshFilter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(entry.meshRenderer);
            EditorUtility.SetDirty(entry.meshFilter);
            EditorUtility.SetDirty(entry.meshRenderer);

            for (int sub = 0; sub < entry.submeshIsTiling.Length; sub++)
            {
                if (entry.submeshIsTiling[sub])
                {
                    Debug.Log($"[Atlas Tool] {entry.gameObject.name} submesh {sub} used tiled UV remap.");
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Atlas Tool",
            $"Done.\nCreated atlas, one material, and {entries.Count} rebuilt meshes in:\n{relativeOutputFolder}",
            "Nice");
    }

    [MenuItem("Tools/Atlas/Combine Selected Objects Into Atlas (Final)", true)]
    private static bool ValidateCombineSelectedObjectsIntoAtlas()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    private static Mesh BuildAtlasedSingleSubmeshMesh(
        Mesh sourceMesh,
        Material[] sourceMaterials,
        int[] submeshTextureIndices,
        bool[] submeshIsTiling,
        Rect[] atlasRects,
        int atlasWidth,
        int atlasHeight)
    {
        Vector3[] srcVertices = sourceMesh.vertices;
        Vector3[] srcNormals = sourceMesh.normals;
        Vector4[] srcTangents = sourceMesh.tangents;
        Color[] srcColors = sourceMesh.colors;
        Vector2[] srcUV0 = sourceMesh.uv;
        Vector2[] srcUV1 = sourceMesh.uv2;
        Vector2[] srcUV2 = sourceMesh.uv3;
        Vector2[] srcUV3 = sourceMesh.uv4;

        bool hasNormals = srcNormals != null && srcNormals.Length == srcVertices.Length;
        bool hasTangents = srcTangents != null && srcTangents.Length == srcVertices.Length;
        bool hasColors = srcColors != null && srcColors.Length == srcVertices.Length;
        bool hasUV0 = srcUV0 != null && srcUV0.Length == srcVertices.Length;
        bool hasUV1 = srcUV1 != null && srcUV1.Length == srcVertices.Length;
        bool hasUV2 = srcUV2 != null && srcUV2.Length == srcVertices.Length;
        bool hasUV3 = srcUV3 != null && srcUV3.Length == srcVertices.Length;

        var newVertices = new List<Vector3>();
        var newNormals = hasNormals ? new List<Vector3>() : null;
        var newTangents = hasTangents ? new List<Vector4>() : null;
        var newColors = hasColors ? new List<Color>() : null;
        var newUV0 = new List<Vector2>();
        var newUV1 = hasUV1 ? new List<Vector2>() : null;
        var newUV2 = hasUV2 ? new List<Vector2>() : null;
        var newUV3 = hasUV3 ? new List<Vector2>() : null;
        var newTriangles = new List<int>();

        float epsilonX = 1.0f / atlasWidth;
        float epsilonY = 1.0f / atlasHeight;

        for (int subMeshIndex = 0; subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
        {
            int[] triangles = sourceMesh.GetTriangles(subMeshIndex);
            Rect atlasRect = atlasRects[submeshTextureIndices[subMeshIndex]];
            Material subMat = sourceMaterials[subMeshIndex];

            Vector2 texScale = GetTextureScale(subMat);
            Vector2 texOffset = GetTextureOffset(subMat);
            bool useTilingRemap = submeshIsTiling[subMeshIndex];

            float innerX = atlasRect.x + epsilonX;
            float innerY = atlasRect.y + epsilonY;
            float innerW = Mathf.Max(0.000001f, atlasRect.width - epsilonX * 2f);
            float innerH = Mathf.Max(0.000001f, atlasRect.height - epsilonY * 2f);

            for (int i = 0; i < triangles.Length; i++)
            {
                int srcIndex = triangles[i];
                int newIndex = newVertices.Count;

                newVertices.Add(srcVertices[srcIndex]);

                if (hasNormals) newNormals.Add(srcNormals[srcIndex]);
                if (hasTangents) newTangents.Add(srcTangents[srcIndex]);
                if (hasColors) newColors.Add(srcColors[srcIndex]);

                Vector2 uv = hasUV0 ? srcUV0[srcIndex] : Vector2.zero;
                uv = Vector2.Scale(uv, texScale) + texOffset;

                if (useTilingRemap)
                {
                    uv.x = Mathf.Repeat(uv.x, 1f);
                    uv.y = Mathf.Repeat(uv.y, 1f);
                }

                uv.x = innerX + uv.x * innerW;
                uv.y = innerY + uv.y * innerH;

                newUV0.Add(uv);

                if (hasUV1) newUV1.Add(srcUV1[srcIndex]);
                if (hasUV2) newUV2.Add(srcUV2[srcIndex]);
                if (hasUV3) newUV3.Add(srcUV3[srcIndex]);

                newTriangles.Add(newIndex);
            }
        }

        Mesh mesh = new Mesh
        {
            name = sourceMesh.name + "_Atlased",
            indexFormat = newVertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };

        mesh.SetVertices(newVertices);

        if (hasNormals) mesh.SetNormals(newNormals);
        if (hasTangents) mesh.SetTangents(newTangents);
        if (hasColors) mesh.SetColors(newColors);

        mesh.SetUVs(0, newUV0);
        if (hasUV1) mesh.SetUVs(1, newUV1);
        if (hasUV2) mesh.SetUVs(2, newUV2);
        if (hasUV3) mesh.SetUVs(3, newUV3);

        mesh.subMeshCount = 1;
        mesh.SetTriangles(newTriangles, 0, true);

        if (!hasNormals)
            mesh.RecalculateNormals();

        mesh.RecalculateBounds();

        return mesh;
    }

    private static bool IsSubmeshTiling(Mesh mesh, int subMeshIndex, Material material)
    {
        Vector2 scale = GetTextureScale(material);
        Vector2 offset = GetTextureOffset(material);

        if (!Approximately(scale, Vector2.one))
            return true;

        if (!Approximately(offset, Vector2.zero))
            return true;

        Vector2[] uvs = mesh.uv;
        if (uvs == null || uvs.Length == 0)
            return false;

        int[] triangles = mesh.GetTriangles(subMeshIndex);
        for (int i = 0; i < triangles.Length; i++)
        {
            Vector2 uv = uvs[triangles[i]];
            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
                return true;
        }

        return false;
    }

    private static Texture GetMainTexture(Material mat)
    {
        if (mat == null)
            return null;

        if (mat.HasProperty("_BaseMap"))
            return mat.GetTexture("_BaseMap");

        if (mat.HasProperty("_MainTex"))
            return mat.GetTexture("_MainTex");

        return mat.mainTexture;
    }

    private static void AssignMainTexture(Material mat, Texture tex)
    {
        if (mat == null)
            return;

        if (mat.HasProperty("_BaseMap"))
            mat.SetTexture("_BaseMap", tex);

        if (mat.HasProperty("_MainTex"))
            mat.SetTexture("_MainTex", tex);

        mat.mainTexture = tex;
    }

    private static Vector2 GetTextureScale(Material mat)
    {
        if (mat == null)
            return Vector2.one;

        if (mat.HasProperty("_BaseMap"))
            return mat.GetTextureScale("_BaseMap");

        if (mat.HasProperty("_MainTex"))
            return mat.GetTextureScale("_MainTex");

        return mat.mainTextureScale;
    }

    private static Vector2 GetTextureOffset(Material mat)
    {
        if (mat == null)
            return Vector2.zero;

        if (mat.HasProperty("_BaseMap"))
            return mat.GetTextureOffset("_BaseMap");

        if (mat.HasProperty("_MainTex"))
            return mat.GetTextureOffset("_MainTex");

        return mat.mainTextureOffset;
    }

    private static void ResetTextureScaleAndOffset(Material mat)
    {
        if (mat == null)
            return;

        if (mat.HasProperty("_BaseMap"))
        {
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.SetTextureOffset("_BaseMap", Vector2.zero);
        }

        if (mat.HasProperty("_MainTex"))
        {
            mat.SetTextureScale("_MainTex", Vector2.one);
            mat.SetTextureOffset("_MainTex", Vector2.zero);
        }

        mat.mainTextureScale = Vector2.one;
        mat.mainTextureOffset = Vector2.zero;
    }

    private static bool Approximately(Vector2 a, Vector2 b)
    {
        return Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y);
    }

    private static void EnsureTextureReadable(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        bool changed = false;

        if (!importer.isReadable)
        {
            importer.isReadable = true;
            changed = true;
        }

        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            changed = true;
        }

        if (changed)
            importer.SaveAndReimport();
    }

    private static string MakeSafeFileName(string input)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            input = input.Replace(c, '_');

        return input.Replace(" ", "_");
    }

    private class Entry
    {
        public GameObject gameObject;
        public MeshFilter meshFilter;
        public MeshRenderer meshRenderer;
        public Mesh sourceMesh;
        public Material[] sourceMaterials;
        public int[] submeshTextureIndices;
        public bool[] submeshIsTiling;
    }
}