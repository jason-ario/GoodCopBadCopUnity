using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PCTerminalEmulatorSceneBuilder
{
    private const string PcPrefabPath = "Assets/_GoodCopBadCop/_Prefabs/Interactables/PC.prefab";
    private const string SuspectSetPath = "Assets/_GoodCopBadCop/_Data/Suspects/Suspect Database/All Suspects.asset";
    private const string EmulatorCameraName = "Terminal Emulator Camera";
    private const float EmulatorCameraFieldOfView = 64f;
    private static readonly Vector3 EmulatorCameraPosition = new Vector3(-0.47f, 0.527f, 0f);
    private static readonly Quaternion EmulatorCameraRotation = new Quaternion(0f, 0.7071068f, 0f, 0.7071068f);

    [MenuItem("Good Cop Bad Cop/PC Terminal Emulator", false, -997)]
    public static void CreateScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject pcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PcPrefabPath);
        if (pcPrefab == null)
            throw new FileNotFoundException($"PC prefab not found at {PcPrefabPath}");

        GameObject pcObject = (GameObject)PrefabUtility.InstantiatePrefab(pcPrefab, scene);
        pcObject.name = "PC Terminal Emulator";
        pcObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        SuspectSet allSuspects = AssetDatabase.LoadAssetAtPath<SuspectSet>(SuspectSetPath);
        if (allSuspects == null)
            throw new FileNotFoundException($"Suspect set not found at {SuspectSetPath}");

        GameObject recordsObject = new GameObject("Suspect Run Records (Seeded)");
        SuspectRunRecords records = recordsObject.AddComponent<SuspectRunRecords>();
        records.allSuspects = allSuspects;

        GameObject bootstrapObject = new GameObject("PC Terminal Emulator Bootstrapper");
        PCTerminalEmulatorBootstrapper bootstrapper = bootstrapObject.AddComponent<PCTerminalEmulatorBootstrapper>();
        SerializedObject serializedBootstrapper = new SerializedObject(bootstrapper);
        serializedBootstrapper.FindProperty("pc").objectReferenceValue = pcObject.GetComponent<PC>();
        serializedBootstrapper.FindProperty("runRecords").objectReferenceValue = records;
        serializedBootstrapper.FindProperty("allSuspects").objectReferenceValue = allSuspects;
        serializedBootstrapper.FindProperty("currentDay").intValue = 6;
        serializedBootstrapper.FindProperty("killedCount").intValue = 2;
        serializedBootstrapper.FindProperty("quarantinedCount").intValue = 2;
        serializedBootstrapper.ApplyModifiedPropertiesWithoutUndo();

        CreateCamera();
        CreateLight();

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.35f, 0.37f, 0.40f);

        scene.name = "PC Terminal Emulator";
        Selection.activeGameObject = pcObject;
        Debug.Log("[PCTerminalEmulatorSceneBuilder] Created unsaved PC Terminal Emulator scene for Day 6. Entering Play Mode. Hotkeys: 1 Residents, 2 All, 3 Deceased, 4 Quarantine, 5 News.");

        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.isPlaying = true;
    }

    private static void CreateCamera()
    {
        GameObject cameraObject = new GameObject(EmulatorCameraName);
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.orthographic = false;
        camera.fieldOfView = EmulatorCameraFieldOfView;
        camera.nearClipPlane = 0.03f;
        cameraObject.AddComponent<AudioListener>();

        cameraObject.transform.SetPositionAndRotation(EmulatorCameraPosition, EmulatorCameraRotation);
    }

    private static void CreateLight()
    {
        GameObject lightObject = new GameObject("Terminal Emulator Key Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
    }

}
