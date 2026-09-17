using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Adds a "Newspaper Baking" section to the <see cref="NewspaperContentsController"/> inspector.
/// Mirrors <see cref="ExamPageEditor"/>'s "Bake to PNG" tool.
///
/// Workflow (Edit Mode — no Play Mode required):
///   1. Select the GameObject with a NewspaperContentsController component (scene or prefab
///      editor — e.g. "Newspaper Contents.prefab").
///   2. Set "Day To Bake" to the day number whose contents you want to capture.
///   3. Click "Bake to PNG". The tool populates the newspaper's text fields for that day, renders
///      the newspaper camera through URP into a temporary RenderTexture, reads the pixels back,
///      and saves a PNG to <see cref="PngSaveFolder"/>/<c>ControllerName_DayN_Baked.png</c>.
/// </summary>
[CustomEditor(typeof(NewspaperContentsController))]
public class NewspaperContentsControllerEditor : Editor
{
    private const string PngSaveFolder = "Assets/_GoodCopBadCop/_Textures/Baked Newspapers";

    private static readonly FieldInfo CameraField = typeof(NewspaperContentsController).GetField(
        "camera", BindingFlags.NonPublic | BindingFlags.Instance);

    private int _dayToBake = 1;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Newspaper Baking", EditorStyles.boldLabel);

        NewspaperContentsController controller = (NewspaperContentsController)target;

        int dayCount = controller.DayCount;
        _dayToBake = EditorGUILayout.IntField("Day To Bake", _dayToBake);
        if (dayCount > 0)
            EditorGUILayout.HelpBox($"{dayCount} day(s) of content authored.", MessageType.None);

        if (GUILayout.Button("Bake to PNG"))
            BakeToPng(controller, Mathf.Max(1, _dayToBake));
    }

    // ─── Bake ─────────────────────────────────────────────────────────────────

    private static void BakeToPng(NewspaperContentsController controller, int day)
    {
        GameObject camGo = CameraField?.GetValue(controller) as GameObject;
        Camera cam = camGo != null ? camGo.GetComponent<Camera>() : null;
        if (cam == null)
        {
            Debug.LogError("[NewspaperBaker] camera is null or missing a Camera component. Make sure it is assigned in the prefab.");
            return;
        }

        // Set the newspaper's text fields for the requested day. This is safe to call in Edit
        // Mode — unlike PopulateFromDay, it does not start the runtime snapshot coroutine.
        controller.SetContentsForDay(day);

        // Build a temporary RT from the camera's assigned target texture descriptor so URP GPU
        // flags match. This is never saved as a project asset, so the asset pipeline can't clear it.
        RenderTexture template = cam.targetTexture;
        RenderTextureDescriptor desc = template != null
            ? template.descriptor
            : new RenderTextureDescriptor(1024, 1024, RenderTextureFormat.Default, 24);

        RenderTexture tempRt = new RenderTexture(desc)
        {
            name       = "NewspaperBake_Temp",
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
            Debug.LogError("[NewspaperBaker] SingleCameraRequest not supported. " +
                           "Confirm URP is active in Project Settings > Graphics.");
            cam.gameObject.SetActive(wasActive);
            tempRt.Release();
            DestroyImmediate(tempRt);
            return;
        }

        RenderPipeline.SubmitRenderRequest(cam, request);
        cam.gameObject.SetActive(wasActive);
        controller.gameObject.SetActive(false);

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
        string fileName    = $"{controller.name}_Day{day}_Baked.png";
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
            importer.textureType         = TextureImporterType.Default;
            importer.sRGBTexture         = false;   // RT content is linear
            importer.alphaIsTransparency = true;
            importer.textureCompression  = TextureImporterCompression.Uncompressed;
            importer.isReadable          = false;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        // Ping the asset in the Project window.
        Texture2D saved = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        EditorGUIUtility.PingObject(saved);

        Debug.Log($"[NewspaperBaker] PNG saved → {assetPath}");
    }
}
