using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Adds a "Checklist Baking" section to the <see cref="ExamPage"/> inspector.
///
/// Workflow (Edit Mode — no Play Mode required):
///   1. Select a GameObject with an ExamPage component (scene or prefab editor).
///   2. Click "Bake to PNG". The tool renders the checklist camera through URP into a
///      temporary RenderTexture, reads the pixels back, and saves a PNG to
///      <see cref="PngSaveFolder"/>/<c>PageName_Baked.png</c>.
///   3. Assign the resulting PNG to <see cref="StaticExamPageDisplay._bakedOverlay"/> on the
///      shop variant prefab. The PNG is a regular file-backed asset and persists across saves.
/// </summary>
[CustomEditor(typeof(ExamPage))]
public class ExamPageEditor : Editor
{
    private const string PngSaveFolder = "Assets/_GoodCopBadCop/_Textures/Baked Checklists";

    private static readonly FieldInfo CameraField = typeof(ExamPage).GetField(
        "_checklistCamera", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo RtTemplateField = typeof(ExamPage).GetField(
        "_renderTextureTemplate", BindingFlags.NonPublic | BindingFlags.Instance);

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Checklist Baking", EditorStyles.boldLabel);

        ExamPage page = (ExamPage)target;

        if (GUILayout.Button("Bake to PNG"))
            BakeToPng(page);
    }

    // ─── Bake ─────────────────────────────────────────────────────────────────

    private static void BakeToPng(ExamPage page)
    {
        Camera cam = CameraField?.GetValue(page) as Camera;
        if (cam == null)
        {
            Debug.LogError("[ExamPageBaker] _checklistCamera is null. Make sure it is assigned in the prefab.");
            return;
        }

        // Build a temporary RT from the same descriptor as the template so URP GPU flags match.
        // This is never saved as a project asset, so the asset pipeline can't clear it.
        RenderTexture template = RtTemplateField?.GetValue(page) as RenderTexture;
        RenderTextureDescriptor desc = template != null
            ? template.descriptor
            : new RenderTextureDescriptor(1024, 1024, RenderTextureFormat.Default, 24);

        RenderTexture tempRt = new RenderTexture(desc)
        {
            name       = "ExamPageBake_Temp",
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        tempRt.Create();

        // Enable the camera temporarily so URP includes it in the render request.
        bool wasActive = cam.gameObject.activeSelf;
        cam.gameObject.SetActive(true);

        // SubmitRenderRequest goes through the full URP pipeline.
        // Camera.Render() bypasses URP and produces empty output with URP shaders.
        var request = new UniversalRenderPipeline.SingleCameraRequest
        {
            destination = tempRt
        };

        if (!RenderPipeline.SupportsRenderRequest(cam, request))
        {
            Debug.LogError("[ExamPageBaker] SingleCameraRequest not supported. " +
                           "Confirm URP is active in Project Settings > Graphics.");
            cam.gameObject.SetActive(wasActive);
            tempRt.Release();
            DestroyImmediate(tempRt);
            return;
        }

        RenderPipeline.SubmitRenderRequest(cam, request);
        cam.gameObject.SetActive(wasActive);

        // Read pixels synchronously while the RT content is still live.
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = tempRt;
        Texture2D tex = new Texture2D(tempRt.width, tempRt.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, tempRt.width, tempRt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        // Done with the temp RT — release it before touching the asset database.
        tempRt.Release();
        DestroyImmediate(tempRt);

        // Write the PNG to disk.
        string projectRoot = Application.dataPath[..^"Assets".Length];
        string absFolder   = Path.Combine(projectRoot, PngSaveFolder);
        string fileName    = page.name + "_Baked.png";
        string assetPath   = PngSaveFolder + "/" + fileName;
        string absFilePath = Path.Combine(absFolder, fileName);

        Directory.CreateDirectory(absFolder);
        File.WriteAllBytes(absFilePath, tex.EncodeToPNG());
        DestroyImmediate(tex);

        // Import and configure the texture so the pipeline doesn't alter the pixels.
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType        = TextureImporterType.Default;
            importer.sRGBTexture        = false;   // RT content is linear
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable         = false;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        // Ping the asset in the Project window.
        Texture2D saved = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        EditorGUIUtility.PingObject(saved);

        Debug.Log($"[ExamPageBaker] PNG saved → {assetPath}");
    }
}
