using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GoodCopBadCop.AnimGen
{
    /// <summary>Three-bone limb (upper arm/thigh, forearm/shin, hand/foot).</summary>
    public sealed class LimbChain
    {
        public Transform upper, lower, end;
        public bool Valid => upper != null && lower != null && end != null;
        public float UpperLength => Vector3.Distance(upper.position, lower.position);
        public float LowerLength => Vector3.Distance(lower.position, end.position);
        public float Reach => UpperLength + LowerLength;
    }

    /// <summary>
    /// Script-free copy of a Humanoid prefab or model inside a private preview scene, used for posing and muscle extraction.
    /// </summary>
    public sealed class HumanoidRig : IDisposable
    {
        public const string DefaultArmsPrefabPath = "Assets/_GoodCopBadCop/_Prefabs/Player Arms For Animations.prefab";
        public const string DefaultBodyModelPath = "Assets/_GoodCopBadCop/_Prefabs/Characters/NPCs/Suspect_Ivan.prefab";
        public static readonly Vector3 DefaultCameraOffset = new Vector3(0f, 0.1f, 0.09f);

        public GameObject Root { get; private set; }
        public Animator Animator { get; private set; }
        public GameObject AnimatorObject => Animator.gameObject;
        public Transform CharacterRoot => Animator.transform;
        public Transform Hips, Spine, Chest, UpperChest, Neck, Head, LeftShoulder, RightShoulder;
        public LimbChain LeftArm, RightArm, LeftLeg, RightLeg;
        public HumanPoseHandler PoseHandler { get; private set; }

        Scene _scene;
        readonly List<(Transform t, Vector3 p, Quaternion r, Vector3 s)> _bind = new List<(Transform, Vector3, Quaternion, Vector3)>();
        readonly List<Transform> _humanBones = new List<Transform>();

        public static string ToAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = path.Replace('\\', '/').Trim();
            int i = path.IndexOf("Assets/", StringComparison.Ordinal);
            return i >= 0 ? path.Substring(i) : path;
        }

        public const string MutatedContainerName = "Mutated Version";

        /// <param name="requireLegs">Full-body work needs valid leg chains; POV arms only need arms and head.</param>
        /// <param name="variant">"mutated" picks the Humanoid Animator under a 'Mutated Version' child; anything else picks one outside it.</param>
        public static HumanoidRig Create(string prefabPath, string fallbackPath, bool requireLegs, string variant = null)
        {
            prefabPath = ToAssetPath(prefabPath) ?? fallbackPath;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new ArgumentException($"Rig prefab/model not found: {prefabPath}");

            var rig = new HumanoidRig();
            rig._scene = EditorSceneManager.NewPreviewScene();
            var holder = new GameObject("AnimGenHolder");
            holder.SetActive(false); // keeps Awake/OnEnable from running on the copy's scripts
            SceneManager.MoveGameObjectToScene(holder, rig._scene);

            var inst = Object.Instantiate(prefab, holder.transform);
            inst.name = prefab.name + " (AnimGen)";
            StripScripts(inst);
            inst.transform.SetParent(null, false);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Object.DestroyImmediate(holder);
            inst.SetActive(true);
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            rig.Root = inst;

            var humans = inst.GetComponentsInChildren<Animator>(true).Where(a => a.avatar != null && a.avatar.isHuman).ToList();
            if (!string.IsNullOrEmpty(variant))
            {
                bool wantMutated = string.Equals(variant, "mutated", StringComparison.OrdinalIgnoreCase);
                humans = humans.Where(a => UnderNamed(a.transform, MutatedContainerName) == wantMutated).ToList();
            }
            rig.Animator = humans.FirstOrDefault();
            if (rig.Animator == null)
            {
                rig.Dispose();
                throw new InvalidOperationException($"No Animator with a Humanoid avatar{(string.IsNullOrEmpty(variant) ? "" : $" for variant '{variant}'")} found in {prefabPath}.");
            }
            // The chosen variant may sit under an inactive container (e.g. 'Mutated Version'): activate its chain.
            for (Transform p = rig.Animator.transform; p != null; p = p.parent) p.gameObject.SetActive(true);
            rig.PrefabAsset = prefab;
            rig.Animator.runtimeAnimatorController = null;
            rig.Animator.applyRootMotion = false;
            rig.CharacterRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            Transform B(HumanBodyBones b) => rig.Animator.GetBoneTransform(b);
            rig.Hips = B(HumanBodyBones.Hips);
            rig.Spine = B(HumanBodyBones.Spine);
            rig.Chest = B(HumanBodyBones.Chest);
            rig.UpperChest = B(HumanBodyBones.UpperChest);
            rig.Neck = B(HumanBodyBones.Neck);
            rig.Head = B(HumanBodyBones.Head);
            rig.LeftShoulder = B(HumanBodyBones.LeftShoulder);
            rig.RightShoulder = B(HumanBodyBones.RightShoulder);
            rig.LeftArm = new LimbChain { upper = B(HumanBodyBones.LeftUpperArm), lower = B(HumanBodyBones.LeftLowerArm), end = B(HumanBodyBones.LeftHand) };
            rig.RightArm = new LimbChain { upper = B(HumanBodyBones.RightUpperArm), lower = B(HumanBodyBones.RightLowerArm), end = B(HumanBodyBones.RightHand) };
            rig.LeftLeg = new LimbChain { upper = B(HumanBodyBones.LeftUpperLeg), lower = B(HumanBodyBones.LeftLowerLeg), end = B(HumanBodyBones.LeftFoot) };
            rig.RightLeg = new LimbChain { upper = B(HumanBodyBones.RightUpperLeg), lower = B(HumanBodyBones.RightLowerLeg), end = B(HumanBodyBones.RightFoot) };
            if (rig.Head == null || rig.Hips == null || !rig.LeftArm.Valid || !rig.RightArm.Valid || (requireLegs && (!rig.LeftLeg.Valid || !rig.RightLeg.Valid)))
            {
                rig.Dispose();
                throw new InvalidOperationException("Avatar is missing required Hips/Head/arm/leg bones.");
            }

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var t = B((HumanBodyBones)i);
                if (t != null) rig._humanBones.Add(t);
            }
            foreach (var t in rig.Animator.GetComponentsInChildren<Transform>(true))
                rig._bind.Add((t, t.localPosition, t.localRotation, t.localScale));
            rig.PoseHandler = new HumanPoseHandler(rig.Animator.avatar, rig.CharacterRoot);
            return rig;
        }

        /// <summary>The source prefab/model asset this rig was instantiated from.</summary>
        public GameObject PrefabAsset { get; private set; }

        static bool UnderNamed(Transform t, string containerName)
        {
            for (Transform p = t; p != null; p = p.parent)
                if (string.Equals(p.name.Trim(), containerName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Generic bakes: reparents named transforms that live outside the Animator (e.g. 'Anomaly Container') under it,
        /// keeping their prefab-relative pose, so Generic clips can address them. Film Studio folds the same names on
        /// its actor copies. Returns the folded transforms.
        /// </summary>
        public List<Transform> Fold(IEnumerable<string> names)
        {
            var folded = new List<Transform>();
            if (names == null) return folded;
            foreach (string n in names)
            {
                Transform t = FindOutside(Root.transform, n, CharacterRoot);
                Transform srcAnim = PrefabAsset != null ? FindByPath(PrefabAsset.transform, RelativePath(Root.transform, CharacterRoot)) : null;
                Transform src = srcAnim != null ? FindOutside(PrefabAsset.transform, n, srcAnim) : null;
                if (t == null) continue;
                t.SetParent(CharacterRoot, false);
                if (src != null && srcAnim != null)
                {
                    t.localPosition = srcAnim.InverseTransformPoint(src.position);
                    t.localRotation = Quaternion.Inverse(srcAnim.rotation) * src.rotation;
                    t.localScale = src.localScale;
                }
                foreach (var c in t.GetComponentsInChildren<Transform>(true))
                    _bind.Add((c, c.localPosition, c.localRotation, c.localScale));
                folded.Add(t);
            }
            return folded;
        }

        /// <summary>First transform named <paramref name="n"/> under <paramref name="root"/> that is not inside <paramref name="animatorT"/>.</summary>
        public static Transform FindOutside(Transform root, string n, Transform animatorT)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == n && !t.IsChildOf(animatorT) && !animatorT.IsChildOf(t)) return t;
            return null;
        }

        static string RelativePath(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (Transform p = t; p != null && p != root; p = p.parent) parts.Insert(0, p.name);
            return string.Join("/", parts);
        }

        static Transform FindByPath(Transform root, string path) => string.IsNullOrEmpty(path) ? root : root.Find(path);

        static void StripScripts(GameObject go)
        {
            // Remove in dependency order ([RequireComponent]) so Unity doesn't log "Can't remove X because Y depends on it".
            for (int pass = 0; pass < 8; pass++)
            {
                var behaviours = go.GetComponentsInChildren<MonoBehaviour>(true).Where(b => b != null).ToArray();
                if (behaviours.Length == 0) break;
                bool removedAny = false;
                foreach (var b in behaviours)
                {
                    if (b == null || IsRequiredByOthers(b)) continue;
                    Object.DestroyImmediate(b);
                    removedAny = true;
                }
                if (!removedAny) break;
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        }

        static bool IsRequiredByOthers(Component c)
        {
            var type = c.GetType();
            foreach (var other in c.GetComponents<MonoBehaviour>())
            {
                if (other == null || other == c) continue;
                foreach (RequireComponent req in Attribute.GetCustomAttributes(other.GetType(), typeof(RequireComponent), true))
                {
                    if ((req.m_Type0 != null && req.m_Type0.IsAssignableFrom(type)) ||
                        (req.m_Type1 != null && req.m_Type1.IsAssignableFrom(type)) ||
                        (req.m_Type2 != null && req.m_Type2.IsAssignableFrom(type)))
                        return true;
                }
            }
            return false;
        }

        public void ResetToBind()
        {
            foreach (var (t, p, r, s) in _bind)
            {
                if (t == null) continue;
                t.localPosition = p; t.localRotation = r; t.localScale = s;
            }
        }

        /// <summary>Applies the base pose: a sampled clip, or the bind pose.</summary>
        public void ApplyBase(AnimationClip clip, float time)
        {
            ResetToBind();
            if (clip != null) clip.SampleAnimation(AnimatorObject, Mathf.Repeat(time, Mathf.Max(clip.length, 1e-4f)));
            CharacterRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        public LimbChain Arm(bool right) => right ? RightArm : LeftArm;
        public LimbChain Leg(bool right) => right ? RightLeg : LeftLeg;
        public Transform Shoulder(bool right) => right ? RightShoulder : LeftShoulder;
        public Transform ChestBone => UpperChest != null ? UpperChest : Chest != null ? Chest : Spine;

        /// <summary>Eye frame: head bone position + offset, oriented with the character body.</summary>
        public void ViewFrame(Vector3 camOffset, out Vector3 pos, out Quaternion rot)
        {
            rot = CharacterRoot.rotation;
            pos = Head.position + rot * camOffset;
        }

        /// <summary>World-space bounds of all mapped human bones (skinned bounds don't update in edit mode).</summary>
        public Bounds BoneBounds()
        {
            var b = new Bounds(Hips.position, Vector3.zero);
            foreach (var t in _humanBones) b.Encapsulate(t.position);
            return b;
        }

        /// <summary>Analytic two-bone IK. Returns true if the target had to be clamped to reach.</summary>
        public static bool SolveTwoBone(LimbChain c, Vector3 target, Vector3 hint)
        {
            Vector3 pa = c.upper.position, pb = c.lower.position, pc = c.end.position;
            float la = (pb - pa).magnitude, lb = (pc - pb).magnitude;
            Vector3 toT = target - pa;
            float d = toT.magnitude;
            float maxR = (la + lb) * 0.999f, minR = Mathf.Abs(la - lb) * 1.001f + 1e-4f;
            bool clamped = d > maxR || d < minR;
            d = Mathf.Clamp(d, minR, maxR);

            Vector3 dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : (pc - pa).normalized;
            Vector3 bend = Vector3.ProjectOnPlane(hint - pa, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(pb - pa, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.forward, dir);
            bend.Normalize();

            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 elbow = pa + dir * (la * cosA) + bend * (la * sinA);

            c.upper.rotation = Quaternion.FromToRotation(pb - pa, elbow - pa) * c.upper.rotation;
            pb = c.lower.position; pc = c.end.position;
            Vector3 reach = pa + dir * d;
            c.lower.rotation = Quaternion.FromToRotation(pc - pb, reach - pb) * c.lower.rotation;
            return clamped;
        }

        /// <summary>Rotates a bone by a delta expressed in the given frame's axes (e.g. character space).</summary>
        public static void RotateInFrame(Transform bone, Quaternion frame, Quaternion delta)
        {
            if (bone == null) return;
            bone.rotation = frame * delta * Quaternion.Inverse(frame) * bone.rotation;
        }

        public void Dispose()
        {
            PoseHandler?.Dispose();
            PoseHandler = null;
            if (Root != null) Object.DestroyImmediate(Root);
            if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
        }
    }
}
