using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor window for capturing screenshots from a Camera with a transparent (alpha) background.
/// Renders the selected camera into an ARGB32 RenderTexture with a zero-alpha clear color,
/// then encodes the result to a PNG file.
/// </summary>
public class TransparentScreenshotTool : EditorWindow
{
    private Camera targetCamera;
    private int width = 1920;
    private int height = 1080;
    private int superSize = 1;
    private int antiAliasing = 8;
    private string outputFolder = "Assets/_GoodCopBadCop/_Previews";
    private string fileName = "Screenshot";

    [MenuItem("Tools/Screenshot/Transparent Screenshot Tool")]
    private static void ShowWindow()
    {
        TransparentScreenshotTool window = GetWindow<TransparentScreenshotTool>();
        window.titleContent = new GUIContent("Transparent Screenshot");
        window.minSize = new Vector2(340f, 260f);
        window.Show();
    }

    private void OnEnable()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        targetCamera = (Camera)EditorGUILayout.ObjectField("Camera", targetCamera, typeof(Camera), true);

        if (GUILayout.Button("Use Scene View Camera"))
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.camera != null)
                targetCamera = sceneView.camera;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Resolution", EditorStyles.boldLabel);
        width = Mathf.Max(1, EditorGUILayout.IntField("Width", width));
        height = Mathf.Max(1, EditorGUILayout.IntField("Height", height));
        superSize = Mathf.Clamp(EditorGUILayout.IntField("Super Size", superSize), 1, 8);
        antiAliasing = EditorGUILayout.IntPopup("Anti Aliasing", antiAliasing,
            new[] { "Disabled", "2x", "4x", "8x" },
            new[] { 1, 2, 4, 8 });

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        outputFolder = EditorGUILayout.TextField("Folder", outputFolder);
        if (GUILayout.Button("...", GUILayout.Width(28f)))
        {
            string picked = EditorUtility.OpenFolderPanel("Choose Output Folder", outputFolder, "");
            if (!string.IsNullOrEmpty(picked))
                outputFolder = ToRelativeIfPossible(picked);
        }
        EditorGUILayout.EndHorizontal();
        fileName = EditorGUILayout.TextField("File Name", fileName);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(targetCamera == null))
        {
            if (GUILayout.Button("Capture Transparent Screenshot", GUILayout.Height(32f)))
                Capture();
        }

        if (targetCamera == null)
            EditorGUILayout.HelpBox("Assign a Camera to capture from.", MessageType.Warning);
    }

    private void Capture()
    {
        if (targetCamera == null)
        {
            Debug.LogWarning("[TransparentScreenshotTool] No camera assigned.");
            return;
        }

        int captureWidth = width * superSize;
        int captureHeight = height * superSize;

        CameraClearFlags originalClearFlags = targetCamera.clearFlags;
        Color originalBackgroundColor = targetCamera.backgroundColor;
        RenderTexture originalTargetTexture = targetCamera.targetTexture;

        RenderTexture renderTexture = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = Mathf.Max(1, antiAliasing)
        };

        Texture2D screenshot = null;

        try
        {
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            targetCamera.targetTexture = renderTexture;

            targetCamera.Render();

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = renderTexture;

            screenshot = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false);
            screenshot.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
            screenshot.Apply();

            RenderTexture.active = previousActive;

            string folderPath = string.IsNullOrEmpty(outputFolder) ? "Assets" : outputFolder;
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            string safeFileName = string.IsNullOrEmpty(fileName) ? "Screenshot" : fileName;
            string filePath = Path.Combine(folderPath, safeFileName + ".png");
            filePath = AssetDatabase.GenerateUniqueAssetPath(filePath);

            byte[] pngData = screenshot.EncodeToPNG();
            File.WriteAllBytes(filePath, pngData);

            if (filePath.Replace('\\', '/').StartsWith("Assets/"))
                AssetDatabase.ImportAsset(filePath);

            Debug.Log($"[TransparentScreenshotTool] Saved transparent screenshot to: {filePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[TransparentScreenshotTool] Failed to capture screenshot: {e}");
        }
        finally
        {
            targetCamera.clearFlags = originalClearFlags;
            targetCamera.backgroundColor = originalBackgroundColor;
            targetCamera.targetTexture = originalTargetTexture;

            if (screenshot != null)
                DestroyImmediate(screenshot);

            renderTexture.Release();
            DestroyImmediate(renderTexture);
        }
    }

    private static string ToRelativeIfPossible(string absolutePath)
    {
        string projectPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
        string normalized = Path.GetFullPath(absolutePath).Replace('\\', '/');

        if (normalized.StartsWith(Path.GetDirectoryName(projectPath).Replace('\\', '/')))
        {
            string relative = "Assets" + normalized.Substring(projectPath.Length);
            return relative;
        }

        return absolutePath;
    }
}
