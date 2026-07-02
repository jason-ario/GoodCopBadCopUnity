using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.Editor
{
    /// <summary>
    /// Re-parents all selected GameObjects to the active (last-clicked) GameObject,
    /// preserving world position and scale for each child.
    /// Shortcut: Ctrl+Alt+P.
    /// Usage: select the child(ren), then Ctrl+click the intended parent, then press Ctrl+Alt+P.
    /// </summary>
    [InitializeOnLoad]
    public static class ParentGameObjectShortcut
    {
        private const string MenuPath = "Tools/Parent GameObject %#t";

        static ParentGameObjectShortcut() { }

        [MenuItem(MenuPath)]
        private static void Execute()
        {
            GameObject newParent = Selection.activeGameObject;
            GameObject[] children = Selection.gameObjects
                .Where(go => go != newParent)
                .ToArray();

            if (children.Length == 0)
            {
                Debug.LogWarning("[ParentGameObjectShortcut] Select at least two GameObjects — " +
                                 "the last-clicked one becomes the parent.");
                return;
            }

            foreach (var child in children)
            {
                if (IsAncestorOf(child.transform, newParent.transform))
                {
                    Debug.LogError($"[ParentGameObjectShortcut] Skipping '{child.name}' — " +
                                   $"'{newParent.name}' is a descendant of it and cannot be its parent.");
                    continue;
                }

                // Snapshot world scale before re-parenting since SetParent may alter it
                // when the parent has non-uniform scale.
                Vector3 worldScale = child.transform.lossyScale;

                Undo.SetTransformParent(child.transform, newParent.transform, "Re-Parent GameObject");
                child.transform.SetParent(newParent.transform, worldPositionStays: true);

                ApplyWorldScale(child.transform, worldScale);

                EditorUtility.SetDirty(child);

                Debug.Log($"[ParentGameObjectShortcut] '{child.name}' → parented to '{newParent.name}' " +
                          $"(world position and scale preserved).");
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateExecute() => Selection.gameObjects.Length >= 2;

        // =========================
        // HELPERS
        // =========================

        /// <summary>
        /// Restores the target transform's lossy scale to the given world-space scale
        /// by computing the required local scale relative to the current parent.
        /// </summary>
        private static void ApplyWorldScale(Transform target, Vector3 desiredWorldScale)
        {
            Vector3 parentScale = target.parent != null
                ? target.parent.lossyScale
                : Vector3.one;

            target.localScale = new Vector3(
                parentScale.x != 0f ? desiredWorldScale.x / parentScale.x : target.localScale.x,
                parentScale.y != 0f ? desiredWorldScale.y / parentScale.y : target.localScale.y,
                parentScale.z != 0f ? desiredWorldScale.z / parentScale.z : target.localScale.z);
        }

        /// <summary>
        /// Returns true if <paramref name="ancestor"/> is a direct or indirect parent of <paramref name="target"/>.
        /// </summary>
        private static bool IsAncestorOf(Transform ancestor, Transform target)
        {
            Transform current = target.parent;
            while (current != null)
            {
                if (current == ancestor) return true;
                current = current.parent;
            }
            return false;
        }
    }
}
