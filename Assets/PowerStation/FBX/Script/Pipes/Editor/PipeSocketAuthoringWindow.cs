using System.IO;
using UnityEditor;
using UnityEngine;

namespace PipeSystem.Editor
{
    public class PipeSocketAuthoringWindow : EditorWindow
    {
        private GameObject targetPrefab;
        private PipeDiameter defaultDiameter = PipeDiameter.Mid_52;
        
        // Default clean path for saving definitions
        private string saveFolderPath = "Assets/Prefabs/Scriptable_Definitions";

        [MenuItem("Tools/Pipe System/Socket Authoring Tool")]
        public static void ShowWindow()
        {
            GetWindow<PipeSocketAuthoringWindow>("Socket Author");
        }

        private void OnGUI()
        {
            GUILayout.Label("Prefab Socket Authoring (Tool 4a)", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            targetPrefab = (GameObject)EditorGUILayout.ObjectField("Target Prefab", targetPrefab, typeof(GameObject), false);
            defaultDiameter = (PipeDiameter)EditorGUILayout.EnumPopup("Default Diameter", defaultDiameter);

            EditorGUILayout.Space();
            GUILayout.Label("Save Location", EditorStyles.boldLabel);
            
            // Save folder row with Browse button
            EditorGUILayout.BeginHorizontal();
            saveFolderPath = EditorGUILayout.TextField("Output Folder", saveFolderPath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                BrowseForFolder();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (targetPrefab == null)
            {
                EditorGUILayout.HelpBox("Assign a project Prefab to begin authoring.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("Add Socket to Prefab", GUILayout.Height(30)))
            {
                AddSocketToPrefab();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Create ScriptableObject Definition", GUILayout.Height(30)))
            {
                CreateDefinitionAsset();
            }
        }

        private void BrowseForFolder()
        {
            string absolutePath = EditorUtility.OpenFolderPanel("Select Definition Save Folder", Application.dataPath, "");
            if (!string.IsNullOrEmpty(absolutePath))
            {
                // Convert absolute system path to Unity relative path (Assets/...)
                if (absolutePath.StartsWith(Application.dataPath))
                {
                    saveFolderPath = "Assets" + absolutePath.Substring(Application.dataPath.Length);
                    Repaint();
                }
                else
                {
                    Debug.LogWarning("Please select a folder inside your current Unity project's Assets directory.");
                }
            }
        }

        private void AddSocketToPrefab()
        {
            string assetPath = AssetDatabase.GetAssetPath(targetPrefab);
            GameObject contents = PrefabUtility.LoadPrefabContents(assetPath);

            // Count existing sockets to name the new one consistently
            PipeSocket[] existing = contents.GetComponentsInChildren<PipeSocket>(true);
            string socketName = $"Socket_{(char)('A' + existing.Length)}";

            GameObject socketGO = new GameObject(socketName);
            socketGO.transform.SetParent(contents.transform, false);
            
            PipeSocket socketComp = socketGO.AddComponent<PipeSocket>();
            socketComp.category = defaultDiameter;

            // Position at root by default; user aligns in Prefab Mode
            socketGO.transform.localPosition = Vector3.zero;
            socketGO.transform.localRotation = Quaternion.identity;

            PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
            PrefabUtility.UnloadPrefabContents(contents);

            Debug.Log($"Added {socketName} to {targetPrefab.name}. Open Prefab Mode to position precisely at open pipe ends.");
        }

        private void CreateDefinitionAsset()
        {
            // Ensure the target folder exists on disk and in AssetDatabase
            EnsureFolderExists(saveFolderPath);

            PipePieceDefinition def = ScriptableObject.CreateInstance<PipePieceDefinition>();
            def.displayName = targetPrefab.name;
            def.prefab = targetPrefab;
            def.diameterCategory = defaultDiameter;
            def.RefreshSockets();

            // Build path using the clean user-specified folder
            string cleanPath = $"{saveFolderPath}/{targetPrefab.name}_Definition.asset";
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(cleanPath);

            AssetDatabase.CreateAsset(def, uniquePath);
            AssetDatabase.SaveAssets();

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = def;
            Debug.Log($"Created PipePieceDefinition successfully at: {uniquePath}");
        }

        private void EnsureFolderExists(string assetPath)
        {
            if (!Directory.Exists(assetPath))
            {
                Directory.CreateDirectory(assetPath);
                AssetDatabase.Refresh();
            }
        }
    }
}