using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VolumetricFogAndMist2;

namespace GoodCopBadCop.Editor
{
    /// <summary>
    /// Applies <see cref="EditorViewSettings"/> in Edit Mode without touching serialized scene state.
    /// Everything it disables is restored before entering Play Mode, before scene saves,
    /// before domain reloads and when scenes close, then re-applied afterwards.
    /// </summary>
    [InitializeOnLoad]
    public static class EditorViewSettingsApplier
    {
        private const string MenuRoot = "Tools/Editor View Settings/";
        private const string FogMenuPath = MenuRoot + "Hide Volumetric Fog In Edit Mode";

        // Fog volumes this tool disabled, with their MeshRenderer's previous enabled state.
        private static readonly Dictionary<VolumetricFog, bool> DisabledFogs = new Dictionary<VolumetricFog, bool>();

        private static bool _refreshQueued;
        private static bool _suspended;

        static EditorViewSettingsApplier()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.hierarchyChanged += RequestRefresh;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreAll;
            EditorApplication.quitting += RestoreAll;

            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosing += OnSceneClosing;

            RequestRefresh();
        }

        public static void RequestRefresh()
        {
            if (_refreshQueued) return;
            _refreshQueued = true;
            EditorApplication.delayCall += () =>
            {
                _refreshQueued = false;
                Refresh();
            };
        }

        private static bool IsEditMode => !EditorApplication.isPlayingOrWillChangePlaymode;

        private static void Refresh()
        {
            if (!IsEditMode || _suspended) return;

            var settings = EditorViewSettings.Instance;
            if (settings != null && settings.ShouldHideVolumetricFog) HideFogs();
            else RestoreFogs();
        }

        private static void HideFogs()
        {
            // Copy first: disabling a fog removes it from VolumetricFog.volumetricFogs.
            var active = new List<VolumetricFog>(VolumetricFog.volumetricFogs);
            foreach (var fog in active)
            {
                if (fog == null || !fog.enabled || DisabledFogs.ContainsKey(fog)) continue;
                if (EditorUtility.IsPersistent(fog)) continue;
                if (PrefabStageUtility.GetPrefabStage(fog.gameObject) != null) continue;

                var meshRenderer = fog.meshRenderer != null ? fog.meshRenderer : fog.GetComponent<MeshRenderer>();
                DisabledFogs[fog] = meshRenderer != null && meshRenderer.enabled;

                fog.enabled = false;
                if (meshRenderer != null) meshRenderer.enabled = false;
            }
        }

        private static void RestoreFogs(Scene? onlyScene = null)
        {
            if (DisabledFogs.Count == 0) return;

            var restored = new List<VolumetricFog>();
            foreach (var pair in DisabledFogs)
            {
                var fog = pair.Key;
                if (fog == null)
                {
                    restored.Add(fog);
                    continue;
                }
                if (onlyScene.HasValue && fog.gameObject.scene != onlyScene.Value) continue;

                var meshRenderer = fog.meshRenderer != null ? fog.meshRenderer : fog.GetComponent<MeshRenderer>();
                if (meshRenderer != null) meshRenderer.enabled = pair.Value;
                fog.enabled = true;
                restored.Add(fog);
            }

            foreach (var fog in restored) DisabledFogs.Remove(fog);
        }

        private static void RestoreAll() => RestoreFogs();

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    RestoreAll();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    RequestRefresh();
                    break;
            }
        }

        private static void OnSceneSaving(Scene scene, string path)
        {
            // Make sure the authored (enabled) state is what gets written to disk.
            _suspended = true;
            RestoreFogs(scene);
        }

        private static void OnSceneSaved(Scene scene)
        {
            _suspended = false;
            RequestRefresh();
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode) => RequestRefresh();

        private static void OnSceneClosing(Scene scene, bool removingScene) => RestoreFogs(scene);

        [MenuItem(FogMenuPath, priority = 0)]
        private static void ToggleFog()
        {
            var settings = EditorViewSettings.Instance;
            Undo.RecordObject(settings, "Toggle Hide Volumetric Fog");
            settings.hideVolumetricFog = !settings.hideVolumetricFog;
            if (settings.hideVolumetricFog) settings.applyInEditMode = true;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            Refresh();
            SceneView.RepaintAll();
        }

        [MenuItem(FogMenuPath, true)]
        private static bool ToggleFogValidate()
        {
            var settings = EditorViewSettings.Instance;
            Menu.SetChecked(FogMenuPath, settings != null && settings.ShouldHideVolumetricFog);
            return IsEditMode;
        }

        [MenuItem(MenuRoot + "Select Settings Asset", priority = 20)]
        private static void SelectSettings()
        {
            Selection.activeObject = EditorViewSettings.Instance;
            EditorGUIUtility.PingObject(Selection.activeObject);
        }
    }
}
