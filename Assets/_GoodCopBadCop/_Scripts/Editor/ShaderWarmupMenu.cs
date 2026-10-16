using System.Linq;
using GoodCopBadCop.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GoodCopBadCop.Editor
{
    /// <summary>
    /// Editor workflow for the shader warmup: toggle Play Mode tracing and keep the Loading
    /// scene's <see cref="ShaderWarmupRunner"/> pointing at every traced collection.
    /// After a traced Play session ends, collections are re-assigned automatically.
    /// </summary>
    [InitializeOnLoad]
    public static class ShaderWarmupMenu
    {
        private const string TraceMenu = "Tools/Shader Warmup/Trace Graphics States In Play Mode";
        private const string AssignMenu = "Tools/Shader Warmup/Assign Collections To Loading Screen";
        private const string LoadingScenePath = "Assets/_GoodCopBadCop/_Scenes/Loading.unity";
        private const string PendingAssignKey = "GoodCopBadCop.ShaderWarmup.PendingAssign";

        static ShaderWarmupMenu()
        {
            GraphicsStateTracer.TraceSaved += _ => SessionState.SetBool(PendingAssignKey, true);
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(PendingAssignKey, false))
                {
                    SessionState.SetBool(PendingAssignKey, false);
                    AssetDatabase.Refresh();
                    EditorApplication.delayCall += AssignCollections;
                }
            };
        }

        [MenuItem(TraceMenu, priority = 0)]
        private static void ToggleTracing()
        {
            bool enabled = !EditorPrefs.GetBool(GraphicsStateTracer.EditorPrefKey, false);
            EditorPrefs.SetBool(GraphicsStateTracer.EditorPrefKey, enabled);
            Debug.Log(enabled
                ? "[ShaderWarmup] Play Mode tracing ON. Play through the game (days, anomalies, effects); states are saved when Play Mode ends."
                : "[ShaderWarmup] Play Mode tracing OFF.");
        }

        [MenuItem(TraceMenu, true)]
        private static bool ToggleTracingValidate()
        {
            Menu.SetChecked(TraceMenu, EditorPrefs.GetBool(GraphicsStateTracer.EditorPrefKey, false));
            return true;
        }

        [MenuItem(AssignMenu, priority = 1)]
        public static void AssignCollections()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[ShaderWarmup] Exit Play Mode before assigning collections.");
                return;
            }

            GraphicsStateCollection[] found = AssetDatabase
                .FindAssets("t:GraphicsStateCollection", new[] { GraphicsStateTracer.CollectionFolder })
                .Select(guid => AssetDatabase.LoadAssetAtPath<GraphicsStateCollection>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(c => c != null)
                .ToArray();

            Scene scene = SceneManager.GetSceneByPath(LoadingScenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere)
            {
                scene = EditorSceneManager.OpenScene(LoadingScenePath, OpenSceneMode.Additive);
            }

            ShaderWarmupRunner runner = scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<ShaderWarmupRunner>(true))
                .FirstOrDefault();

            if (runner == null)
            {
                Debug.LogError($"[ShaderWarmup] No ShaderWarmupRunner found in {LoadingScenePath}.");
            }
            else
            {
                var so = new SerializedObject(runner);
                SerializedProperty list = so.FindProperty("collections");
                list.arraySize = found.Length;
                for (int i = 0; i < found.Length; i++)
                {
                    list.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
                }

                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[ShaderWarmup] Assigned {found.Length} collection(s) to the Loading screen: " +
                          string.Join(", ", found.Select(c => $"{c.name} ({c.totalGraphicsStateCount} states)")));
            }

            if (openedHere)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
