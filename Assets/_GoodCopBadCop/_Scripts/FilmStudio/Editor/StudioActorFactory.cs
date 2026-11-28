using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>Spawns visual-only <see cref="StudioActor"/>s from the suspect prefabs.</summary>
    public static class StudioActorFactory
    {
        private const float DefaultHeadHeight = 1.65f;

        public static StudioActor Spawn(StudioCatalog.ActorEntry entry, Vector3 position, float yaw, Transform parent, bool startMutated, bool generic = false)
        {
            if (entry == null || entry.prefab == null)
            {
                Debug.LogError($"[FilmStudio] Cannot spawn actor '{entry?.actorId}': prefab missing.");
                return null;
            }

            var root = new GameObject($"Actor_{entry.actorId}");
            Undo.RegisterCreatedObjectUndo(root, "Spawn Studio Actor");
            root.transform.SetParent(parent, false);

            // Instantiate under an inactive staging object so no Awake/OnEnable runs before stripping.
            var staging = new GameObject("__staging");
            staging.SetActive(false);
            staging.transform.SetParent(root.transform, false);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab, staging.transform);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.name = "Art";
            StudioStripper.Strip(instance, keepColliders: false);

            instance.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(staging);

            // Film-only size (e.g. a "small creepy man" whose game prefab is full adult size). Applied before anchors are
            // measured so framing uses the scaled height. Scaling the root keeps the feet on the mark.
            float filmScale = entry.filmScale > 0.01f ? entry.filmScale : 1f;
            root.transform.localScale = Vector3.one * filmScale;

            var actor = root.AddComponent<StudioActor>();
            actor.actorId = entry.actorId;
            actor.displayName = entry.displayName;

            Transform baseT = FindDirectChild(instance.transform, "Base Version");
            Transform mutT = FindDirectChild(instance.transform, "Mutated Version");
            actor.baseArt = baseT != null ? baseT.gameObject : instance;
            actor.mutatedArt = mutT != null && mutT.GetComponentInChildren<Animator>(true) != null ? mutT.gameObject : null;
            if (mutT != null && actor.mutatedArt == null) mutT.gameObject.SetActive(false);

            // Both variants stay active so each Animator binds to the Timeline; swaps toggle renderers only.
            if (actor.mutatedArt != null)
            {
                actor.baseArt.SetActive(true);
                actor.mutatedArt.SetActive(true);
                CollectEnabledRenderers(actor.baseArt, actor.baseRenderers);
                CollectEnabledRenderers(actor.mutatedArt, actor.mutatedRenderers);
            }

            ResolveAnchors(actor);
            if (generic) MakeGeneric(actor, instance.transform);
            actor.SetVariant(startMutated);

            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return actor;
        }

        /// <summary>
        /// Generic mode (anchors are resolved first, while the avatars still map bones): folds loose rigs such as
        /// 'Anomaly Container' under the base Animator (same layout the Generic baker uses), then strips avatars and
        /// controllers so Timeline Generic curves drive every bone.
        /// </summary>
        private static void MakeGeneric(StudioActor actor, Transform art)
        {
            Animator baseAnim = actor.BaseAnimator;
            if (baseAnim != null && baseAnim.avatar != null && baseAnim.avatar.isHuman)
            {
                foreach (string n in AnimGen.GenericClipBaker.DefaultFoldRoots)
                {
                    Transform t = AnimGen.HumanoidRig.FindOutside(art, n, baseAnim.transform);
                    if (t != null) t.SetParent(baseAnim.transform, true);
                }
            }
            foreach (Animator a in actor.GetComponentsInChildren<Animator>(true))
            {
                a.avatar = null;
                a.runtimeAnimatorController = null;
                a.applyRootMotion = false;
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            actor.genericRig = true;
        }

        private static Transform FindDirectChild(Transform t, string childName)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (string.Equals(c.name.Trim(), childName, System.StringComparison.OrdinalIgnoreCase)) return c;
            }
            return null;
        }

        private static void CollectEnabledRenderers(GameObject art, List<Renderer> into)
        {
            into.Clear();
            foreach (Renderer r in art.GetComponentsInChildren<Renderer>(true))
                if (r.enabled && r.gameObject.activeSelf) into.Add(r);
        }

        private static void ResolveAnchors(StudioActor actor)
        {
            Animator anim = actor.BaseAnimator != null ? actor.BaseAnimator : actor.GetComponentInChildren<Animator>(true);
            Transform head = null, chest = null;

            if (anim != null && anim.avatar != null && anim.avatar.isHuman)
            {
                head = anim.GetBoneTransform(HumanBodyBones.Head);
                chest = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
            }

            Transform searchRoot = actor.baseArt != null ? actor.baseArt.transform : actor.transform;
            head ??= FindBoneByName(searchRoot, "head");
            chest ??= FindBoneByName(searchRoot, "spine2") ?? FindBoneByName(searchRoot, "chest") ?? FindBoneByName(searchRoot, "spine1");

            float height = EstimateHeight(actor);
            actor.headAnchor = head != null ? head : CreateAnchor(actor.transform, "HeadAnchor", height * 0.93f);
            actor.chestAnchor = chest != null ? chest : CreateAnchor(actor.transform, "ChestAnchor", height * 0.68f);
            actor.height = height;
            actor.faceAnchor = CreateFaceAnchor(actor.headAnchor, actor.transform, height);

            Animator mutAnim = actor.MutatedAnimator;
            if (mutAnim != null)
            {
                Transform mHead = null, mChest = null;
                if (mutAnim.avatar != null && mutAnim.avatar.isHuman)
                {
                    mHead = mutAnim.GetBoneTransform(HumanBodyBones.Head);
                    mChest = mutAnim.GetBoneTransform(HumanBodyBones.UpperChest) ?? mutAnim.GetBoneTransform(HumanBodyBones.Chest);
                }
                mHead ??= FindBoneByName(actor.mutatedArt.transform, "head");
                mChest ??= FindBoneByName(actor.mutatedArt.transform, "spine2") ?? FindBoneByName(actor.mutatedArt.transform, "spine1");
                actor.mutatedHeadAnchor = mHead;
                actor.mutatedChestAnchor = mChest;
                actor.mutatedHeight = MeasureTop(actor.mutatedArt, actor.transform, height);
                if (mHead != null) actor.mutatedFaceAnchor = CreateFaceAnchor(mHead, actor.transform, actor.mutatedHeight);
            }
        }

        /// <summary>
        /// Face centre parented to the head bone: head bones sit at the skull base, so the face is lifted towards the top
        /// of the head (the mesh top), by up to 35% of body height for big-headed rigs.
        /// </summary>
        private static Transform CreateFaceAnchor(Transform head, Transform root, float height)
        {
            if (head == null) return null;
            float headY = head.position.y - root.position.y;
            float lift = Mathf.Clamp((height - headY) * 0.45f, height * 0.02f, height * 0.35f);
            if (height <= headY) lift = height * 0.04f;
            var go = new GameObject("FaceAnchor");
            go.transform.SetParent(head, true);
            go.transform.position = head.position + Vector3.up * lift;
            return go.transform;
        }

        private static float MeasureTop(GameObject art, Transform root, float fallback)
        {
            if (art == null) return fallback;
            Bounds b = default;
            bool any = false;
            foreach (Renderer r in art.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return fallback;
            float h = b.max.y - root.position.y;
            return h > 0.5f && h < 6f ? h : fallback;
        }

        private static Transform FindBoneByName(Transform root, string token)
        {
            Transform best = null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("end") || n.Contains("top") || n.Contains("headset") || n.Contains("hair")) continue;
                bool match = n == token || n.EndsWith(":" + token) || n.EndsWith("_" + token) || n.EndsWith(" " + token) || n.EndsWith("." + token);
                if (match) { best = t; break; }
            }
            return best;
        }

        private static float EstimateHeight(StudioActor actor)
        {
            var renderers = (actor.baseArt != null ? actor.baseArt : actor.gameObject).GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return DefaultHeadHeight / 0.93f;
            Bounds b = default;
            bool any = false;
            foreach (Renderer r in renderers)
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return DefaultHeadHeight / 0.93f;
            float h = b.max.y - actor.transform.position.y;
            return h > 0.5f && h < 6f ? h : DefaultHeadHeight / 0.93f;
        }

        private static Transform CreateAnchor(Transform parent, string anchorName, float height)
        {
            var go = new GameObject(anchorName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            return go.transform;
        }

        /// <summary>Head height above the actor root in its current pose (used to scale camera presets).</summary>
        public static float HeadHeight(StudioActor actor)
        {
            if (actor == null || actor.headAnchor == null) return DefaultHeadHeight;
            float h = actor.headAnchor.position.y - actor.transform.position.y;
            return h > 0.4f && h < 4f ? h : DefaultHeadHeight;
        }
    }
}
