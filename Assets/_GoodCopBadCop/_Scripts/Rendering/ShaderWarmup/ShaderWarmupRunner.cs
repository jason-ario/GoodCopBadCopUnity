using System.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoodCopBadCop.Rendering
{
    /// <summary>
    /// Pre-builds traced graphics states (see <see cref="GraphicsStateTracer"/>) on the Loading
    /// screen so shader variants / PSOs aren't compiled the first time they appear in gameplay.
    /// Picks the collection matching the running platform + graphics API and warms it up a few
    /// states per frame so the loading UI stays responsive. Driven by LoadingScreenController.
    /// </summary>
    public sealed class ShaderWarmupRunner : MonoBehaviour
    {
        [Tooltip("Traced collections, one per platform/graphics API. Auto-filled by Tools > Shader Warmup > Assign Collections To Loading Screen.")]
        [SerializeField] private GraphicsStateCollection[] collections = new GraphicsStateCollection[0];

        [Tooltip("Graphics states created per frame. Higher = faster warmup, choppier loading bar.")]
        [SerializeField, Min(1)] private int statesPerFrame = 16;

        [Tooltip("Also warm up in Editor Play Mode (the Editor caches compiled shaders, so this is mainly for testing).")]
        [SerializeField] private bool runInEditor = false;

        [Tooltip("Give up after this many seconds so a bad collection can never block loading.")]
        [SerializeField, Min(1f)] private float timeoutSeconds = 30f;

        /// <summary>True once a warmup finished this session; later Loading screen visits skip it.</summary>
        public static bool HasWarmedUp { get; private set; }

        /// <summary>0..1 warmup progress (1 when there is nothing to do).</summary>
        public float Progress { get; private set; } = 1f;

        public bool IsDone { get; private set; }

        /// <summary>True if this run will actually warm something up (used to weight the loading bar).</summary>
        public bool HasWork => !HasWarmedUp && (!Application.isEditor || runInEditor) && FindMatchingCollection() != null;

        public IEnumerator Run()
        {
            IsDone = false;
            Progress = 0f;

            GraphicsStateCollection match = HasWork ? FindMatchingCollection() : null;
            if (match == null)
            {
                if (!HasWarmedUp && !Application.isEditor)
                {
                    Debug.LogWarning($"[ShaderWarmupRunner] No graphics state collection for {Application.platform}/" +
                                     $"{SystemInfo.graphicsDeviceType}; shaders will compile on first use.");
                }

                Complete();
                yield break;
            }

            float startTime = Time.realtimeSinceStartup;
            int total = Mathf.Max(1, match.totalGraphicsStateCount);
            JobHandle handle = default;

            while (!match.isWarmedUp)
            {
                if (handle.IsCompleted)
                {
                    handle.Complete();
                    handle = match.WarmUpProgressively(statesPerFrame, default);
                }

                Progress = Mathf.Clamp01(match.completedWarmupCount / (float)total);

                if (Time.realtimeSinceStartup - startTime > timeoutSeconds)
                {
                    Debug.LogWarning($"[ShaderWarmupRunner] Warmup timed out after {timeoutSeconds:0}s " +
                                     $"({match.completedWarmupCount}/{total} states).");
                    break;
                }

                yield return null;
            }

            handle.Complete();
            Debug.Log($"[ShaderWarmupRunner] Warmed up {match.completedWarmupCount}/{total} graphics states " +
                      $"from {match.name} in {Time.realtimeSinceStartup - startTime:0.00}s.");
            HasWarmedUp = true;
            Complete();
        }

        private void Complete()
        {
            Progress = 1f;
            IsDone = true;
        }

        private GraphicsStateCollection FindMatchingCollection()
        {
            GraphicsDeviceType api = SystemInfo.graphicsDeviceType;
            RuntimePlatform platform = Application.platform;
            GraphicsStateCollection familyMatch = null;

            foreach (GraphicsStateCollection c in collections)
            {
                if (c == null || c.graphicsDeviceType != api)
                {
                    continue;
                }

                if (c.runtimePlatform == platform)
                {
                    return c;
                }

                if (familyMatch == null && PlatformFamily(c.runtimePlatform) == PlatformFamily(platform))
                {
                    familyMatch = c;
                }
            }

            return familyMatch;
        }

        // Editor traces (e.g. OSXEditor) are usable by the matching player as a fallback.
        private static RuntimePlatform PlatformFamily(RuntimePlatform p)
        {
            switch (p)
            {
                case RuntimePlatform.OSXEditor: return RuntimePlatform.OSXPlayer;
                case RuntimePlatform.WindowsEditor: return RuntimePlatform.WindowsPlayer;
                case RuntimePlatform.LinuxEditor: return RuntimePlatform.LinuxPlayer;
                default: return p;
            }
        }
    }
}
