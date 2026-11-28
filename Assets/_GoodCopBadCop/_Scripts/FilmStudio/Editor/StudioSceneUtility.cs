using System.IO;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Film Mode: the Film Studio overlay scene (sets = marks + camera occluders, films, film camera) is loaded
    /// additively on top of Main.unity, which supplies the real environment and lighting. Film Mode is "on" while
    /// the overlay is loaded; pressing Play then runs the film with gameplay suppressed (<see cref="FilmModePlayGuard"/>).
    /// Exiting Film Mode unloads the overlay so Play runs the normal game. Main.unity is never saved by these tools.
    /// </summary>
    [InitializeOnLoad]
    public static class StudioSceneUtility
    {
        public const string SetsRootName = FilmModeShared.SetsRootName;
        public const string FilmsRootName = FilmModeShared.FilmsRootName;
        public const string CameraName = FilmModeShared.CameraName;
        private const int FilmCameraDepth = 100;

        public static string ScenePath => FilmStudioSettings.instance.studioScenePath;
        public static string MainScenePath => FilmStudioSettings.instance.mainScenePath;

        static StudioSceneUtility()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode) return;
            // Read by the runtime FilmModePlayGuard before any scene loads.
            SessionState.SetBool(FilmModeShared.KeyFilmModePlay, IsFilmMode);
            SessionState.SetString(FilmModeShared.KeyOverlayPath, ScenePath);
        }

        private static bool Loaded(Scene s) => s.IsValid() && s.isLoaded;
        public static Scene OverlayScene => SceneManager.GetSceneByPath(ScenePath);
        public static Scene MainScene => SceneManager.GetSceneByPath(MainScenePath);

        /// <summary>True while the film overlay and Main.unity are both loaded (in edit or play mode).</summary>
        public static bool IsFilmMode => Loaded(OverlayScene) && Loaded(MainScene);

        /// <summary>Back-compat alias.</summary>
        public static bool IsStudioActive => IsFilmMode;

        /// <summary>
        /// Enters Film Mode: Main.unity loaded (opened Single after the save prompt if needed) plus the overlay
        /// additively, overlay active so new film objects land in it. Creates the overlay if missing.
        /// Returns false if the user cancelled the save prompt or Play Mode is running.
        /// </summary>
        public static bool EnsureOpen(out Scene scene) => EnterFilmMode(out scene);

        public static bool EnterFilmMode(out Scene overlay)
        {
            overlay = OverlayScene;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (IsFilmMode) return true;
                Debug.LogError("[FilmStudio] Enter Film Mode from Edit Mode (stop Play first).");
                return false;
            }

            Scene main = MainScene;
            if (!Loaded(main))
            {
                if (Loaded(overlay))
                {
                    main = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
                }
                else
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
                    main = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
                }
            }

            overlay = OverlayScene;
            if (!Loaded(overlay))
            {
                if (File.Exists(ScenePath))
                {
                    overlay = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets");
                    overlay = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    EditorSceneManager.SaveScene(overlay, ScenePath);
                    Debug.Log($"[FilmStudio] Created film overlay scene at {ScenePath}");
                }
            }

            EditorSceneManager.MoveSceneBefore(main, overlay); // Main loads first in Play Mode
            SceneManager.SetActiveScene(overlay);
            EnsureRoots(overlay);
            if (overlay.isDirty) EditorSceneManager.SaveScene(overlay);
            return true;
        }

        /// <summary>Unloads the overlay (saving it) and makes Main.unity active, so Play runs the normal game.</summary>
        public static bool ExitFilmMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[FilmStudio] Exit Film Mode from Edit Mode (stop Play first).");
                return false;
            }
            Scene overlay = OverlayScene;
            if (!Loaded(overlay)) return true;

            Scene main = MainScene;
            if (!Loaded(main)) main = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
            if (overlay.isDirty) EditorSceneManager.SaveScene(overlay);
            SceneManager.SetActiveScene(main);
            EditorSceneManager.CloseScene(overlay, true);
            return true;
        }

        public static bool SetFilmMode(bool on) => on ? EnterFilmMode(out _) : ExitFilmMode();

        public static void EnsureRoots(Scene scene)
        {
            FindOrCreateRoot(scene, SetsRootName);
            FindOrCreateRoot(scene, FilmsRootName);

            GameObject cam = FindRoot(scene, CameraName);
            if (cam == null)
            {
                cam = new GameObject(CameraName) { tag = "MainCamera" };
                SceneManager.MoveGameObjectToScene(cam, scene);
                var camera = cam.AddComponent<Camera>();
                camera.nearClipPlane = 0.05f;
                camera.fieldOfView = 40f;
                var urp = cam.AddComponent<UniversalAdditionalCameraData>();
                urp.renderPostProcessing = true;
                urp.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                cam.AddComponent<AudioListener>();
                var brain = cam.AddComponent<CinemachineBrain>();
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
                cam.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, -4f), Quaternion.identity);
            }
            else if (cam.GetComponent<CinemachineBrain>() == null)
            {
                cam.AddComponent<CinemachineBrain>().DefaultBlend =
                    new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            }
            // Draw over any camera Main.unity leaves enabled in edit mode (Game view preview).
            var filmCam = cam.GetComponent<Camera>();
            if (filmCam != null && filmCam.depth < FilmCameraDepth) filmCam.depth = FilmCameraDepth;

            // Main.unity supplies the sun; the overlay's own light only fills in when Main has none.
            bool mainHasSun = MainHasDirectionalLight();
            GameObject lightGo = FindRoot(scene, "Directional Light");
            if (lightGo == null && !mainHasSun)
            {
                lightGo = new GameObject("Directional Light");
                SceneManager.MoveGameObjectToScene(lightGo, scene);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                light.shadows = LightShadows.Soft;
                lightGo.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            }
            if (lightGo != null && lightGo.activeSelf == mainHasSun) lightGo.SetActive(!mainHasSun);
        }

        private static bool MainHasDirectionalLight()
        {
            Scene main = MainScene;
            if (!Loaded(main)) return false;
            foreach (GameObject root in main.GetRootGameObjects())
                foreach (Light l in root.GetComponentsInChildren<Light>(false))
                    if (l.enabled && l.type == LightType.Directional) return true;
            return false;
        }

        public static GameObject FindRoot(Scene scene, string rootName)
        {
            if (!Loaded(scene)) return null;
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == rootName) return go;
            return null;
        }

        public static GameObject FindOrCreateRoot(Scene scene, string rootName)
        {
            GameObject go = FindRoot(scene, rootName);
            if (go != null) return go;
            go = new GameObject(rootName);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        public static CinemachineBrain FindBrain(Scene scene)
        {
            GameObject cam = FindRoot(scene, CameraName);
            return cam != null ? cam.GetComponent<CinemachineBrain>() : null;
        }

        /// <summary>
        /// Edit-mode scrub: evaluates the director, then forces the CinemachineBrain to snap the Main Camera
        /// immediately (in edit mode the brain otherwise waits for the next editor frame, leaving a stale view).
        /// </summary>
        public static void EvaluatePreview(PlayableDirector director, double time)
        {
            if (director == null) return;
            director.time = time;
            director.Evaluate();
            if (!Application.isPlaying) RefreshSkinning(director.gameObject);
            CinemachineBrain brain = FindBrain(director.gameObject.scene);
            if (brain != null)
            {
                CinemachineBrain.UpdateMethods previous = brain.UpdateMethod;
                brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
                brain.ResetState();
                brain.ManualUpdate();
                brain.UpdateMethod = previous;
            }
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>
        /// Edit mode: a Timeline evaluation on a Generic (avatar-less) Animator moves the bones but the
        /// SkinnedMeshRenderers keep drawing the old pose until re-enabled. Toggling forces a re-skin.
        /// </summary>
        public static void RefreshSkinning(GameObject root)
        {
            foreach (SkinnedMeshRenderer s in root.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!s.enabled) continue;
                s.enabled = false;
                s.enabled = true;
            }
        }

        public static StudioSet FindSet(Scene scene, string setId)
        {
            GameObject sets = FindRoot(scene, SetsRootName);
            if (sets == null) return null;
            foreach (StudioSet s in sets.GetComponentsInChildren<StudioSet>(true))
                if (s.setId == setId) return s;
            return null;
        }

        /// <summary>Saves the film overlay only. Main.unity is never saved by Film Studio.</summary>
        public static void SaveStudio()
        {
            Scene scene = OverlayScene;
            if (!scene.IsValid()) return;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        [MenuItem("Tools/Film Studio/Enter Film Mode", priority = 1)]
        private static void MenuEnter() => EnterFilmMode(out _);

        [MenuItem("Tools/Film Studio/Enter Film Mode", true)]
        private static bool MenuEnterValidate() => !IsFilmMode && !EditorApplication.isPlaying;

        [MenuItem("Tools/Film Studio/Exit Film Mode (Play Game)", priority = 2)]
        private static void MenuExit() => ExitFilmMode();

        [MenuItem("Tools/Film Studio/Exit Film Mode (Play Game)", true)]
        private static bool MenuExitValidate() => Loaded(OverlayScene) && !EditorApplication.isPlaying;
    }
}
