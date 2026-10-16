using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoodCopBadCop.Rendering
{
    /// <summary>
    /// Records every graphics state (shader variant + PSO) the game renders with, so the Loading
    /// screen can pre-build them via <see cref="ShaderWarmupRunner"/> instead of compiling them
    /// mid-gameplay (the cause of large Semaphore.WaitForSignal hitches).
    ///
    /// Enable tracing:
    ///  - Editor: Tools > Shader Warmup > Trace Graphics States In Play Mode, then play normally.
    ///  - Development build: launch with -traceGraphicsStates.
    ///
    /// Each session is merged into one file per platform + graphics API
    /// (e.g. OSXEditor_Metal.graphicsstate) under <see cref="CollectionFolder"/>. In builds the
    /// result is sent to a connected Editor and also written to persistentDataPath.
    /// </summary>
    public sealed class GraphicsStateTracer : MonoBehaviour
    {
        public const string EditorPrefKey = "GoodCopBadCop.ShaderWarmup.TraceInPlayMode";
        public const string CommandLineArg = "-traceGraphicsStates";
        public const string CollectionFolder = "Assets/_GoodCopBadCop/_Settings/ShaderWarmup";

        private static GraphicsStateTracer instance;
        private GraphicsStateCollection collection;

        public static bool IsTracing => instance != null && instance.collection != null && instance.collection.isTracing;

        public static string FileNameForCurrentDevice =>
            $"{Application.platform}_{SystemInfo.graphicsDeviceType}.graphicsstate";

        /// <summary>Raised in the Editor after a trace has been saved to disk (path is project-relative).</summary>
        public static event Action<string> TraceSaved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null || !ShouldTrace())
            {
                return;
            }

            var go = new GameObject("[GraphicsStateTracer]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GraphicsStateTracer>();
        }

        private static bool ShouldTrace()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorPrefs.GetBool(EditorPrefKey, false);
#else
            return Debug.isDebugBuild && Environment.GetCommandLineArgs().Contains(CommandLineArg);
#endif
        }

        private void Awake()
        {
            collection = new GraphicsStateCollection();
            if (collection.BeginTrace())
            {
                Debug.Log($"[GraphicsStateTracer] Tracing graphics states -> {FileNameForCurrentDevice}");
            }
            else
            {
                Debug.LogWarning("[GraphicsStateTracer] BeginTrace failed; graphics states will not be recorded.");
                collection = null;
            }
        }

        private void OnApplicationQuit()
        {
            Finish();
        }

        private void OnDestroy()
        {
            Finish();
            if (instance == this)
            {
                instance = null;
            }
        }

        private void Finish()
        {
            if (collection == null || !collection.isTracing)
            {
                return;
            }

            collection.EndTrace();
            string fileName = FileNameForCurrentDevice;

#if UNITY_EDITOR
            string relativePath = $"{CollectionFolder}/{fileName}";
            string fullPath = Path.GetFullPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

            GraphicsStateCollection toSave = collection;
            if (File.Exists(fullPath))
            {
                var existing = new GraphicsStateCollection();
                if (existing.LoadFromFile(fullPath) && existing.Append(collection))
                {
                    toSave = existing;
                }
            }

            if (toSave.SaveToFile(fullPath))
            {
                Debug.Log($"[GraphicsStateTracer] Saved {toSave.totalGraphicsStateCount} graphics states " +
                          $"({toSave.variantCount} variants) to {relativePath}");
                TraceSaved?.Invoke(relativePath);
            }
            else
            {
                Debug.LogError($"[GraphicsStateTracer] Failed to save graphics states to {relativePath}");
            }
#else
            collection.SendToEditor(fileName);
            string localPath = Path.Combine(Application.persistentDataPath, fileName);
            collection.SaveToFile(localPath);
            Debug.Log($"[GraphicsStateTracer] Sent {collection.totalGraphicsStateCount} graphics states to the Editor " +
                      $"and saved a copy to {localPath}");
#endif
            collection = null;
        }
    }
}
