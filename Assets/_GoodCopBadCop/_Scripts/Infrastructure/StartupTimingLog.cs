using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GoodCopBadCop.Infrastructure
{
    /// <summary>
    /// Player-only startup profiler. Writes one Player.log line per startup phase, with the time
    /// since the OS launched the process. That shows where launch time goes: OS/runtime/engine
    /// init (before SubsystemRegistration), script assembly loading, first scene load, and the
    /// first rendered frame. Search Player.log for "[StartupTiming]".
    /// </summary>
    public static class StartupTimingLog
    {
        private static double _previous;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            _previous = 0d;
            Mark("SubsystemRegistration (engine initialized)");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void OnAfterAssembliesLoaded() => Mark("AfterAssembliesLoaded (scripts loaded)");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void OnBeforeSplashScreen() => Mark("BeforeSplashScreen");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnBeforeSceneLoad() => Mark("BeforeSceneLoad (first scene loading)");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnAfterSceneLoad()
        {
            Mark("AfterSceneLoad (first scene awake)");
            if (Application.isEditor)
            {
                return;
            }

            var go = new GameObject("[StartupTimingLog]") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<FirstFrameProbe>();
        }

        private static void Mark(string phase)
        {
            if (Application.isEditor)
            {
                return;
            }

            double now = SecondsSinceProcessStart();
            Debug.Log($"[StartupTiming] {phase}: {now:0.000}s since launch (+{now - _previous:0.000}s)");
            _previous = now;
        }

        private static double SecondsSinceProcessStart()
        {
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    return (DateTime.Now - process.StartTime).TotalSeconds;
                }
            }
            catch (Exception)
            {
                // Fallback: engine clock, which misses the pre-engine part of launch.
                return Time.realtimeSinceStartupAsDouble;
            }
        }

        private sealed class FirstFrameProbe : MonoBehaviour
        {
            private IEnumerator Start()
            {
                yield return new WaitForEndOfFrame();
                Mark("First frame rendered");
                Destroy(gameObject);
            }
        }
    }
}
