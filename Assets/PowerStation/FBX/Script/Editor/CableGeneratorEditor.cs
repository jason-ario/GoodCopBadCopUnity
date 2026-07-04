using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

[CustomEditor(typeof(CableGenerator))]
public class CableGeneratorEditor : Editor
{
    // Persistent key to remember your chosen save folder across Unity sessions
    private const string PREFS_KEY_FOLDER = "Megalomania_CableBakeFolder";
    private string _targetFolder = "Assets";

    private void OnEnable()
    {
        // Load the saved folder path, defaulting to "Assets" if not set
        _targetFolder = EditorPrefs.GetString(PREFS_KEY_FOLDER, "Assets");
    }

    private void OnSceneGUI()
    {
        CableGenerator cable = (CableGenerator)target;
        if (cable.anchorA == null || cable.anchorB == null) return;

        EditorGUI.BeginChangeCheck();
        Vector3 newPosA = Handles.PositionHandle(cable.anchorA.position, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(cable.anchorA, "Move Anchor A");
            cable.anchorA.position = newPosA;
        }

        EditorGUI.BeginChangeCheck();
        Vector3 newPosB = Handles.PositionHandle(cable.anchorB.position, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(cable.anchorB, "Move Anchor B");
            cable.anchorB.position = newPosB;
        }

        Handles.color = Color.yellow;
        float spanLength = cable.SpanLength();
        
        // Use local coordinates for the curve preview matching the generator math
        Vector3 localPosA = cable.transform.InverseTransformPoint(cable.anchorA.position);
        Vector3 localPosB = cable.transform.InverseTransformPoint(cable.anchorB.position);
        var samples = CableSpline.SamplePath(localPosA, localPosB, spanLength * cable.sagRatio, cable.pathSegments);
        
        for (int i = 0; i < samples.Count - 1; i++)
        {
            Vector3 worldA = cable.transform.TransformPoint(samples[i].position);
            Vector3 worldB = cable.transform.TransformPoint(samples[i + 1].position);
            Handles.DrawLine(worldA, worldB);
        }
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CableGenerator cable = (CableGenerator)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("PS3 Budget & Statistics", EditorStyles.boldLabel);
        
        int tris = cable.GetEstimatedTriangles();
        GUI.color = (tris <= 2500) ? Color.green : Color.red;
        EditorGUILayout.HelpBox($"Span Length: {cable.SpanLength():F2}m\nEstimated Triangles: {tris} / 2500", MessageType.Info);
        GUI.color = Color.white;

        EditorGUILayout.Space(5);
        if (GUILayout.Button("Regenerate Mesh", GUILayout.Height(28)))
        {
            cable.Regenerate();
        }

        // --- BAKE TO ASSET SECTION ---
        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("Mesh Baking & Asset Storage", EditorStyles.boldLabel);

        // Folder selection UI
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Save Folder");
        EditorGUILayout.SelectableLabel(_targetFolder, EditorStyles.textField, GUILayout.Height(18));
        if (GUILayout.Button("Browse...", GUILayout.Width(70), GUILayout.Height(18)))
        {
            SelectBakeFolder();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // Bake Selected Button
        if (GUILayout.Button("Bake This Cable to Asset", GUILayout.Height(30)))
        {
            BakeSingleCable(cable);
        }

        // Find all cables in scene to show accurate count on the bulk button
        CableGenerator[] allCables = Object.FindObjectsByType<CableGenerator>(FindObjectsInactive.Exclude);
        
        GUI.backgroundColor = new Color(0.85f, 0.95f, 1f); // Subtle blue highlight for bulk action
        if (GUILayout.Button($"Bake ALL Cables in Scene ({allCables.Length} Found)", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("Confirm Bulk Bake", 
                $"Are you sure you want to regenerate and bake {allCables.Length} cable meshes into:\n\n{_targetFolder}?", 
                "Bake All", "Cancel"))
            {
                BakeAllCablesInScene(allCables);
            }
        }
        GUI.backgroundColor = Color.white;
    }

    private void SelectBakeFolder()
    {
        string absolutePath = EditorUtility.OpenFolderPanel("Select Folder to Save Baked Cable Meshes", _targetFolder, "");
        if (!string.IsNullOrEmpty(absolutePath))
        {
            // Convert OS absolute path to Unity relative path (Assets/...)
            if (absolutePath.StartsWith(Application.dataPath))
            {
                _targetFolder = "Assets" + absolutePath.Substring(Application.dataPath.Length);
                EditorPrefs.SetString(PREFS_KEY_FOLDER, _targetFolder);
            }
            else
            {
                EditorUtility.DisplayDialog("Invalid Folder", "Please select a folder inside this Unity Project's 'Assets' directory!", "OK");
            }
        }
    }

    private void BakeSingleCable(CableGenerator cable)
    {
        if (!EnsureValidFolder()) return;

        // Force a clean regeneration before saving
        cable.Regenerate();

        MeshFilter mf = cable.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogError($"[CableGenerator] No mesh found on {cable.gameObject.name} to bake!");
            return;
        }

        string fileName = $"{cable.gameObject.name}_Mesh.asset";
        string fullPath = Path.Combine(_targetFolder, fileName).Replace("\\", "/");

        SaveMeshToDatabase(mf, fullPath);
        Debug.Log($"[CableGenerator] Successfully baked mesh for '{cable.gameObject.name}' to: {fullPath}", cable.gameObject);
    }

    private void BakeAllCablesInScene(CableGenerator[] allCables)
    {
        if (!EnsureValidFolder()) return;

        int bakedCount = 0;
        try
        {
            for (int i = 0; i < allCables.Length; i++)
            {
                CableGenerator cable = allCables[i];
                
                // Display progress bar for large environments
                EditorUtility.DisplayProgressBar("Baking All Cables", $"Baking {cable.gameObject.name} ({i + 1}/{allCables.Length})...", (float)i / allCables.Length);

                cable.Regenerate();

                MeshFilter mf = cable.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    string fileName = $"{cable.gameObject.name}_Mesh.asset";
                    string fullPath = Path.Combine(_targetFolder, fileName).Replace("\\", "/");
                    
                    SaveMeshToDatabase(mf, fullPath);
                    bakedCount++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            // Mark scene dirty so Unity knows the MeshFilter references have been updated to asset files
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            
            EditorUtility.DisplayDialog("Bulk Bake Complete!", $"Successfully baked {bakedCount} cable meshes into:\n\n{_targetFolder}", "Awesome!");
        }
    }

    private void SaveMeshToDatabase(MeshFilter mf, string assetPath)
    {
        Mesh existingAsset = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);

        if (existingAsset != null)
        {
            // 1. OVERWRITE EXISTING ASSET (Preserves GUIDs and scene references)
            Mesh tempMesh = Instantiate(mf.sharedMesh);
            tempMesh.name = Path.GetFileNameWithoutExtension(assetPath);

            EditorUtility.CopySerialized(tempMesh, existingAsset);
            AssetDatabase.SaveAssetIfDirty(existingAsset);
            mf.sharedMesh = existingAsset;

            // Safely destroy the temporary copy we used to transfer data
            if (!Application.isPlaying) DestroyImmediate(tempMesh, true);
        }
        else
        {
            // 2. CREATE BRAND NEW ASSET
            Mesh newAsset = Instantiate(mf.sharedMesh);
            newAsset.name = Path.GetFileNameWithoutExtension(assetPath);

            // Save it to disk. newAsset IS NOW THE PERMANENT DISK ASSET!
            AssetDatabase.CreateAsset(newAsset, assetPath);
            AssetDatabase.SaveAssets();

            mf.sharedMesh = newAsset;

            // CRITICAL FIX: Do NOT call DestroyImmediate here! 
            // Destroying newAsset would wipe the permanent asset file we just created.
        }
    }

    private bool EnsureValidFolder()
    {
        if (!AssetDatabase.IsValidFolder(_targetFolder))
        {
            EditorUtility.DisplayDialog("Folder Not Found", $"The target folder '{_targetFolder}' does not exist. Please use the 'Browse...' button to select a valid save folder.", "OK");
            return false;
        }
        return true;
    }
}