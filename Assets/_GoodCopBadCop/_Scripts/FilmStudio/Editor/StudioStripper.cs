using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Strips copied sets / spawned actors down to visual-only components so they can enter Play Mode
    /// in FilmStudio.unity without NGO, gameplay managers, or static Instances.
    /// Only ever run this on unpacked instances — never on source prefabs.
    /// </summary>
    public static class StudioStripper
    {
        /// <summary>Colliders are kept on sets (harmless, useful for ground raycasts) but removed from actors.</summary>
        public static int Strip(GameObject root, bool keepColliders)
        {
            int removedMissing = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                removedMissing += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

            int removed = 0;
            // Several passes: RequireComponent dependencies block removal until dependents are gone.
            for (int pass = 0; pass < 6; pass++)
            {
                var toRemove = new List<Component>();
                foreach (Component c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || IsAllowed(c, keepColliders)) continue;
                    toRemove.Add(c);
                }
                if (toRemove.Count == 0) break;

                // Gameplay behaviours (incl. NetworkBehaviours) first, NetworkObject and engine components last.
                toRemove.Sort((a, b) => Priority(a).CompareTo(Priority(b)));
                int removedThisPass = 0;
                foreach (Component c in toRemove)
                {
                    if (c == null) continue;
                    if (IsRequiredByOther(c)) continue; // retried next pass once its dependents are gone
                    try
                    {
                        UnityEngine.Object.DestroyImmediate(c);
                        removedThisPass++;
                    }
                    catch (Exception)
                    {
                        // Blocked by a dependency this pass; retried next pass.
                    }
                }
                removed += removedThisPass;
                if (removedThisPass == 0) break;
            }

            foreach (Animator anim in root.GetComponentsInChildren<Animator>(true))
            {
                anim.applyRootMotion = false; // project rule: suspects never use root motion
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            // Screen-space canvases (computer screens, HUDs) draw over every camera — only world-space UI is set dressing.
            foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas.isRootCanvas && canvas.renderMode != RenderMode.WorldSpace)
                    canvas.gameObject.SetActive(false);
            }

            return removed + removedMissing;
        }

        private static bool IsAllowed(Component c, bool keepColliders) => FilmModeShared.IsVisual(c, keepColliders);

        /// <summary>True when another component on the same GameObject declares [RequireComponent] for c's type.</summary>
        private static bool IsRequiredByOther(Component c)
        {
            Type cType = c.GetType();
            foreach (Component other in c.GetComponents<Component>())
            {
                if (other == null || other == c) continue;
                foreach (RequireComponent req in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                {
                    if ((req.m_Type0 != null && req.m_Type0.IsAssignableFrom(cType)) ||
                        (req.m_Type1 != null && req.m_Type1.IsAssignableFrom(cType)) ||
                        (req.m_Type2 != null && req.m_Type2.IsAssignableFrom(cType)))
                        return true;
                }
            }
            return false;
        }

        private static int Priority(Component c)
        {
            string n = c.GetType().FullName ?? "";
            if (n == "Unity.Netcode.NetworkObject") return 2;
            if (c is MonoBehaviour) return 0;
            if (c is Joint) return 0;
            if (c is Rigidbody) return 1;
            return 1;
        }
    }
}
