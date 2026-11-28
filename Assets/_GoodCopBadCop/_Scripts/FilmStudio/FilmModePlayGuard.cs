#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Film Mode play (editor only): when Play Mode starts with the Film Studio overlay loaded on top of Main.unity,
    /// switch the game off in the play-mode copy so only the environment, lighting and the film run.
    /// Gameplay behaviours are disabled before their Start (Awake has already run), VContainer scopes are destroyed
    /// so entry points stop ticking, game cameras/listeners/audio/HUD canvases go quiet and physics props freeze.
    /// Main.unity is never modified: every change happens on the play-mode instances and is discarded on exit.
    /// </summary>
    public static class FilmModePlayGuard
    {
        private static bool s_Active;
        private static string s_OverlayPath;
        private static int s_Disabled, s_Scopes, s_Canvases;

        public static bool IsActive => s_Active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            s_Active = false;
            s_OverlayPath = null;
            s_Disabled = s_Scopes = s_Canvases = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Arm()
        {
            s_Active = SessionState.GetBool(FilmModeShared.KeyFilmModePlay, false);
            if (!s_Active) return;
            s_OverlayPath = SessionState.GetString(FilmModeShared.KeyOverlayPath, "");
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterLoad()
        {
            if (!s_Active) return;
            SuppressAll();
            Debug.Log($"[FilmStudio] Film Mode play: gameplay suppressed ({s_Disabled} behaviours disabled, " +
                      $"{s_Scopes} DI scopes destroyed, {s_Canvases} screen canvases hidden). Main.unity is untouched.");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SuppressAll();

        /// <summary>Idempotent: safe to run after every scene load.</summary>
        public static void SuppressAll()
        {
            if (!s_Active) return;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                bool overlay = s.path == s_OverlayPath;
                foreach (GameObject root in s.GetRootGameObjects())
                {
                    if (overlay && FilmModeShared.OverlayRootNames.Contains(root.name)) continue;
                    Suppress(root);
                }
            }
            foreach (GameObject root in DontDestroyOnLoadRoots()) Suppress(root);
        }

        private static void Suppress(GameObject root)
        {
            foreach (Component c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null || FilmModeShared.IsVisual(c, keepColliders: true)) continue;

                if (c.GetType().Name.EndsWith("LifetimeScope"))
                {
                    Object.Destroy(c); // disposes the VContainer scope so its ITickable / IStartable entry points stop
                    s_Scopes++;
                    continue;
                }
                switch (c)
                {
                    case PlayableDirector d:
                        d.Stop();
                        break;
                    case AudioSource a:
                        a.Stop();
                        break;
                    case Rigidbody rb:
                        rb.isKinematic = true;
                        continue;
                }
                if (c is Behaviour b && b.enabled)
                {
                    b.enabled = false;
                    s_Disabled++;
                }
            }

            // Screen-space canvases (HUD, menus, prompts) draw over the film camera; world-space UI is set dressing.
            foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace || !canvas.gameObject.activeSelf) continue;
                canvas.gameObject.SetActive(false);
                s_Canvases++;
            }
        }

        private static List<GameObject> DontDestroyOnLoadRoots()
        {
            var probe = new GameObject("__FilmModeProbe");
            Object.DontDestroyOnLoad(probe);
            var roots = new List<GameObject>(probe.scene.GetRootGameObjects());
            roots.Remove(probe);
            Object.DestroyImmediate(probe);
            return roots;
        }
    }
}
#endif
