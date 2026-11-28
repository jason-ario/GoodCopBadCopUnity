using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bezi;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Spike: can Film Studio actors play Generic (Transform/blendshape) clips authored for a short?
    /// Inspects rigs and runs an A/B playback test (avatar kept vs stripped, base vs mutated).
    /// </summary>
    public static class GenericAnimLab
    {
        private const string LabRootName = "--GENERIC_LAB";
        private const string LabFolder = "Assets/_GoodCopBadCop/FilmStudio/Lab";
        private static readonly Vector3 LabOrigin = new Vector3(30f, 0f, 0f);
        private static readonly string[] ExtraKeywords = { "jaw", "tongue", "tail", "tentacle", "eye", "brow", "lid", "lip", "teeth", "mouth", "wing", "spike", "extra", "_end" };

        private class BoneMap
        {
            public string source;
            public Transform hips, chest, neck, head, jaw, rUpper, rLower, rHand, tip;
            public SkinnedMeshRenderer blendSmr;
            public string blendName;
        }

        // ── Rig inspection ────────────────────────────────────────────────────────

        [BeziAction("Film Studio lab: inspect an actor's base and mutated rigs for generic animation — avatar type, transform/skinned bone counts, humanoid-unmapped (extra) skinned bones, jaw/tail-like bones and blendshapes.", IsReadOnly = true)]
        public static string FilmStudioLabInspectRig(string actorId)
        {
            StudioCatalog.ActorEntry entry = StudioCatalogBuilder.LoadOrCreate().FindActor(actorId);
            if (entry == null || entry.prefab == null) return $"actor '{actorId}' not found";
            string path = AssetDatabase.GetAssetPath(entry.prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var sb = new StringBuilder($"{actorId} ({path})\n");
                Transform baseT = FindChild(root.transform, "Base Version");
                Transform mutT = FindChild(root.transform, "Mutated Version");
                DescribeRig(sb, "BASE", (baseT != null ? baseT : root.transform).GetComponentInChildren<Animator>(true));
                if (mutT != null) DescribeRig(sb, "MUTATED", mutT.GetComponentInChildren<Animator>(true));
                return sb.ToString();
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void DescribeRig(StringBuilder sb, string label, Animator a)
        {
            if (a == null) { sb.AppendLine($"[{label}] no Animator"); return; }
            Avatar av = a.avatar;
            var mapped = new HashSet<Transform>();
            if (av != null && av.isHuman)
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    Transform t = a.GetBoneTransform((HumanBodyBones)i);
                    if (t != null) mapped.Add(t);
                }

            var smrs = a.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var skinBones = new HashSet<Transform>(smrs.SelectMany(s => s.bones).Where(b => b != null));
            var extras = skinBones.Where(b => !mapped.Contains(b)).Select(b => b.name).OrderBy(n => n).ToList();
            var interesting = a.GetComponentsInChildren<Transform>(true)
                .Where(t => ExtraKeywords.Any(k => t.name.ToLowerInvariant().Contains(k))).Select(t => t.name).Distinct().ToList();
            var blend = smrs.Where(s => s.sharedMesh != null && s.sharedMesh.blendShapeCount > 0)
                .Select(s => $"{s.name}({s.sharedMesh.blendShapeCount}: {string.Join(",", Enumerable.Range(0, Mathf.Min(8, s.sharedMesh.blendShapeCount)).Select(i => s.sharedMesh.GetBlendShapeName(i)))})").ToList();

            sb.AppendLine($"[{label}] animator '{a.name}', avatar '{(av != null ? av.name : "none")}' human={av != null && av.isHuman} valid={av != null && av.isValid}");
            sb.AppendLine($"  transforms={a.GetComponentsInChildren<Transform>(true).Length} smrs={smrs.Length} skinnedBones={skinBones.Count} humanoidMapped={mapped.Count} unmappedSkinned={extras.Count}");
            sb.AppendLine($"  unmapped skinned: {string.Join(", ", extras.Take(40))}{(extras.Count > 40 ? " …" : "")}");
            sb.AppendLine($"  keyword bones: {string.Join(", ", interesting.Take(30))}");
            sb.AppendLine($"  blendshapes: {(blend.Count > 0 ? string.Join(" | ", blend) : "none")}");
        }

        // ── Playback test ─────────────────────────────────────────────────────────

        [BeziAction("Film Studio lab: spawn an actor 4x in FilmStudio.unity (base/mutated x humanoid avatar kept/stripped) under --GENERIC_LAB, author a Generic test clip per variant (neck stretch, head scale, jaw, arm raise, blendshape), play it via a Timeline and compare Timeline evaluation against direct clip sampling. Leaves the lab posed at previewTime for capture.")]
        public static string FilmStudioLabRunGenericTest(string actorId, float previewTime)
        {
            StudioCatalog.ActorEntry entry = StudioCatalogBuilder.LoadOrCreate().FindActor(actorId);
            if (entry == null || entry.prefab == null) return $"actor '{actorId}' not found";
            if (!StudioSceneUtility.EnsureOpen(out Scene scene)) return "cancelled";

            GameObject old = StudioSceneUtility.FindRoot(scene, LabRootName);
            if (old != null) Object.DestroyImmediate(old);
            var lab = new GameObject(LabRootName);
            SceneManager.MoveGameObjectToScene(lab, scene);
            lab.transform.position = LabOrigin;
            EnsureFolder(LabFolder);

            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            string tlPath = $"{LabFolder}/{actorId}_GenericLab.playable";
            AssetDatabase.DeleteAsset(tlPath);
            AssetDatabase.CreateAsset(timeline, tlPath);
            var director = lab.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.Hold;
            director.playableAsset = timeline;

            var configs = new List<(bool keep, bool mut)> { (true, false), (false, false) };
            if (entry.hasMutatedVariant) { configs.Add((true, true)); configs.Add((false, true)); }

            var runs = new List<Run>();
            var clips = new Dictionary<bool, AnimationClip>();
            for (int i = 0; i < configs.Count; i++)
            {
                (bool keep, bool mut) = configs[i];
                Vector3 pos = LabOrigin + new Vector3((i - (configs.Count - 1) * 0.5f) * 3.5f, 0f, 0f);
                StudioActor actor = StudioActorFactory.Spawn(entry, pos, 180f, lab.transform, mut);
                if (actor == null) return "spawn failed";
                actor.name = $"{actorId}_{(mut ? "MUT" : "BASE")}_{(keep ? "avatarKept" : "avatarStripped")}";
                Animator anim = mut ? actor.MutatedAnimator : actor.BaseAnimator;
                if (anim == null) { runs.Add(new Run { label = actor.name, note = "no animator" }); continue; }

                BoneMap map = MapBones(anim);
                bool wasHuman = anim.avatar != null && anim.avatar.isHuman;
                if (!keep) anim.avatar = null;
                anim.applyRootMotion = false;

                if (!clips.TryGetValue(mut, out AnimationClip clip))
                {
                    clip = BuildTestClip(anim, map, actor.transform, $"{actorId}_{(mut ? "mutated" : "base")}_GenericTest");
                    string clipPath = $"{LabFolder}/{clip.name}.anim";
                    AssetDatabase.DeleteAsset(clipPath);
                    AssetDatabase.CreateAsset(clip, clipPath);
                    clips[mut] = clip;
                }

                var track = timeline.CreateTrack<AnimationTrack>(null, actor.name);
                track.trackOffset = TrackOffset.ApplySceneOffsets;
                TimelineClip tc = track.CreateClip(clip);
                tc.start = 0; tc.duration = clip.length;
                director.SetGenericBinding(track, anim);

                runs.Add(new Run { label = actor.name, anim = anim, map = map, wasHuman = wasHuman, keep = keep, clip = clip, rest = Capture(actor.transform, anim), restSnap = Snap(map), tentacleChains = mut ? tentacleChainsLast : 0 });
            }

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            director.RebuildGraph();

            // Compare Timeline vs direct sampling at key times.
            float[] times = { 0.5f, 1.0f, 1.5f, 2.0f };
            foreach (float t in times)
            {
                StudioSceneUtility.EvaluatePreview(director, t);
                foreach (Run r in runs.Where(r => r.anim != null)) r.tl[t] = Snap(r.map);
                foreach (Run r in runs.Where(r => r.anim != null))
                {
                    Restore(r.rest);
                    r.clip.SampleAnimation(r.anim.gameObject, t);
                    r.sm[t] = Snap(r.map);
                    Restore(r.rest);
                }
            }

            StudioSceneUtility.EvaluatePreview(director, Mathf.Clamp(previewTime, 0f, (float)timeline.duration));
            StudioSceneUtility.SaveStudio();

            var sb = new StringBuilder($"timeline {tlPath} ({timeline.duration:0.0}s), lab root '{LabRootName}' at {LabOrigin}\n");
            foreach (Run r in runs) sb.AppendLine(r.Report(times));
            return sb.ToString();
        }

        [BeziAction("Film Studio lab: evaluate the --GENERIC_LAB Timeline at a time (edit-mode scrub) for capture.")]
        public static string FilmStudioLabPreviewAt(float time)
        {
            GameObject lab = StudioSceneUtility.FindRoot(SceneManager.GetActiveScene(), LabRootName);
            var d = lab != null ? lab.GetComponent<PlayableDirector>() : null;
            if (d == null) return "no lab";
            StudioSceneUtility.EvaluatePreview(d, time);
            return $"lab at {d.time:0.00}s";
        }

        [BeziAction("Film Studio lab: evaluate the lab at a time and report, per lab actor, key bone world positions plus every SkinnedMeshRenderer's enabled state and whether its bones live under the bound Animator.", IsReadOnly = true)]
        public static string FilmStudioLabProbe(float time)
        {
            GameObject lab = StudioSceneUtility.FindRoot(SceneManager.GetActiveScene(), LabRootName);
            var d = lab != null ? lab.GetComponent<PlayableDirector>() : null;
            if (d == null) return "no lab";
            if (time >= 0f) StudioSceneUtility.EvaluatePreview(d, time);
            var sb = new StringBuilder($"director time {d.time:0.00} graphValid={d.playableGraph.IsValid()}\n");
            foreach (StudioActor actor in lab.GetComponentsInChildren<StudioActor>(true))
            {
                bool mut = actor.name.Contains("_MUT_");
                Animator anim = mut ? actor.MutatedAnimator : actor.BaseAnimator;
                if (anim == null) continue;
                Transform rh = Find(anim.transform, "righthand"), hd = Find(anim.transform, "head");
                SkinnedMeshRenderer big = anim.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s => s.bones.Length).FirstOrDefault();
                string baked = "-";
                if (big != null)
                {
                    Transform sb2 = big.bones.FirstOrDefault(b => b != null && b.name.EndsWith("RightHand"));
                    baked = $"smr {big.name} bones={big.bones.Length} smrRightHand={(sb2 != null ? AnimationUtility.CalculateTransformPath(sb2, anim.transform) + " pos " + sb2.position.ToString("F2") + " same=" + (sb2 == rh) : "none")} " +
                            $"animatedRightHand={(rh != null ? AnimationUtility.CalculateTransformPath(rh, anim.transform) : "-")} smrEnabled={big.enabled} updateOffscreen={big.updateWhenOffscreen} " +
                            $"animatorEnabled={anim.enabled} cull={anim.cullingMode} hasController={anim.runtimeAnimatorController != null}";
                }
                sb.AppendLine($"{actor.name}: rightHand {(rh != null ? rh.position.ToString("F2") : "-")} head {(hd != null ? hd.position.ToString("F2") : "-")} headLocalScale {(hd != null ? hd.localScale.ToString("F2") : "-")} | {baked}");
            }
            return sb.ToString();
        }

        [BeziAction("Film Studio lab: evaluate the lab at each time (comma-separated) and render the named lab actor from the front with a temporary camera into one horizontal contact-sheet PNG. Returns the absolute PNG path.", IsReadOnly = true)]
        public static string FilmStudioLabRender(string actorName, string timesCsv)
        {
            GameObject lab = StudioSceneUtility.FindRoot(SceneManager.GetActiveScene(), LabRootName);
            var d = lab != null ? lab.GetComponent<PlayableDirector>() : null;
            Transform actor = lab != null ? lab.transform.Find(actorName) : null;
            if (d == null || actor == null) return "no lab / actor";
            float[] times = (timesCsv ?? "0").Split(',').Select(s => float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 0f).ToArray();

            const int w = 384, h = 576;
            var camGo = new GameObject("__LabCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f; cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.25f, 0.25f, 0.28f);
            Vector3 fwd = actor.forward;
            Vector3 focus = actor.position + Vector3.up * 1.6f;
            camGo.transform.position = focus + fwd * 6.5f + actor.right * 1.2f;
            camGo.transform.LookAt(focus);
            var rt = new RenderTexture(w, h, 24);
            var sheet = new Texture2D(w * times.Length, h, TextureFormat.RGB24, false);
            try
            {
                cam.targetTexture = rt;
                for (int i = 0; i < times.Length; i++)
                {
                    StudioSceneUtility.EvaluatePreview(d, times[i]);
                    cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, w, h), i * w, 0);
                }
                sheet.Apply();
                string path = System.IO.Path.GetFullPath($"Library/FilmStudio/lab_{actorName}.png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
                return path;
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = null;
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(sheet);
            }
        }

        [BeziAction("Film Studio lab: delete the --GENERIC_LAB root and the Lab asset folder.")]
        public static string FilmStudioLabCleanup()
        {
            GameObject lab = StudioSceneUtility.FindRoot(SceneManager.GetActiveScene(), LabRootName);
            if (lab != null) Object.DestroyImmediate(lab);
            AssetDatabase.DeleteAsset(LabFolder);
            StudioSceneUtility.SaveStudio();
            return "cleaned";
        }

        // ── Clip authoring (character-space deltas → local Transform curves) ──────

        private static AnimationClip BuildTestClip(Animator anim, BoneMap m, Transform actorRoot, string name)
        {
            var clip = new AnimationClip { name = name, frameRate = 30f };
            Quaternion cs = actorRoot.rotation;

            void Rot(Transform b, params (float t, Vector3 e)[] keys)
            {
                if (b == null) return;
                Quaternion parent = b.parent != null ? b.parent.rotation : Quaternion.identity;
                var q = keys.Select(k => (k.t, Quaternion.Inverse(parent) * (cs * Quaternion.Euler(k.e) * Quaternion.Inverse(cs) * b.rotation))).ToArray();
                string p = AnimationUtility.CalculateTransformPath(b, anim.transform);
                Set(clip, p, typeof(Transform), "m_LocalRotation.x", q.Select(k => (k.t, k.Item2.x)));
                Set(clip, p, typeof(Transform), "m_LocalRotation.y", q.Select(k => (k.t, k.Item2.y)));
                Set(clip, p, typeof(Transform), "m_LocalRotation.z", q.Select(k => (k.t, k.Item2.z)));
                Set(clip, p, typeof(Transform), "m_LocalRotation.w", q.Select(k => (k.t, k.Item2.w)));
            }
            void Vec(Transform b, string prop, Vector3 rest, params (float t, float s)[] keys)
            {
                if (b == null) return;
                string p = AnimationUtility.CalculateTransformPath(b, anim.transform);
                Set(clip, p, typeof(Transform), prop + ".x", keys.Select(k => (k.t, rest.x * k.s)));
                Set(clip, p, typeof(Transform), prop + ".y", keys.Select(k => (k.t, rest.y * k.s)));
                Set(clip, p, typeof(Transform), prop + ".z", keys.Select(k => (k.t, rest.z * k.s)));
            }

            Vector3 z = Vector3.zero;
            Rot(m.chest, (0f, z), (0.5f, new Vector3(12, 0, 0)), (2.0f, new Vector3(12, 0, 0)), (2.5f, z));
            Rot(m.head, (0f, z), (0.5f, new Vector3(0, 20, 25)), (1.0f, new Vector3(-15, 0, -10)), (2.0f, new Vector3(-15, 0, -10)), (2.5f, z));
            Vec(m.head, "m_LocalPosition", m.head != null ? m.head.localPosition : z, (0f, 1f), (0.5f, 1f), (1.0f, 1.8f), (2.0f, 1.8f), (2.5f, 1f));
            Vec(m.head, "m_LocalScale", m.head != null ? m.head.localScale : z, (0f, 1f), (1.0f, 1f), (1.5f, 1.3f), (2.0f, 1.3f), (2.5f, 1f));
            Rot(m.rUpper, (0f, z), (1.0f, new Vector3(0, 0, 60)), (2.0f, new Vector3(0, 0, 60)), (2.5f, z));
            Rot(m.rLower, (0f, z), (1.0f, new Vector3(0, 0, 40)), (2.0f, new Vector3(0, 0, 40)), (2.5f, z));
            Rot(m.jaw, (0f, z), (1.0f, z), (1.5f, new Vector3(25, 0, 0)), (2.0f, new Vector3(25, 0, 0)), (2.5f, z));
            if (m.blendSmr != null)
            {
                string p = AnimationUtility.CalculateTransformPath(m.blendSmr.transform, anim.transform);
                Set(clip, p, typeof(SkinnedMeshRenderer), "blendShape." + m.blendName, new[] { (0f, 0f), (1.0f, 0f), (1.5f, 100f), (2.0f, 100f), (2.5f, 0f) });
            }
            // Extra (non-humanoid) chains: travelling wave down every "Tentacle*" chain.
            int chain = 0;
            foreach (Transform root in anim.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Tentacle")))
            {
                Transform[] bones = TentacleBones(root).ToArray();
                for (int i = 0; i < bones.Length; i++)
                {
                    Transform b = bones[i];
                    float phase = chain * 1.3f + i * 0.6f;
                    var keys = Enumerable.Range(0, 26).Select(k => k * 0.1f)
                        .Select(t => (t, b.localRotation * Quaternion.Euler(Mathf.Sin(t * 6f - phase) * 25f, 0f, Mathf.Cos(t * 5f - phase) * 18f))).ToArray();
                    string p = AnimationUtility.CalculateTransformPath(b, anim.transform);
                    Set(clip, p, typeof(Transform), "m_LocalRotation.x", keys.Select(k => (k.t, k.Item2.x)));
                    Set(clip, p, typeof(Transform), "m_LocalRotation.y", keys.Select(k => (k.t, k.Item2.y)));
                    Set(clip, p, typeof(Transform), "m_LocalRotation.z", keys.Select(k => (k.t, k.Item2.z)));
                    Set(clip, p, typeof(Transform), "m_LocalRotation.w", keys.Select(k => (k.t, k.Item2.w)));
                }
                chain++;
            }
            tentacleChainsLast = chain;

            clip.EnsureQuaternionContinuity();
            return clip;
        }

        private static int tentacleChainsLast;

        private static IEnumerable<Transform> TentacleBones(Transform root) =>
            root.GetComponentsInChildren<Transform>(true).Where(b => b.name.StartsWith("Bone") && !b.name.EndsWith("_end"));

        private static int Depth(Transform t) { int d = 0; while (t.parent != null) { d++; t = t.parent; } return d; }

        private static void Set(AnimationClip clip, string path, System.Type type, string prop, IEnumerable<(float t, float v)> keys)
        {
            var curve = new AnimationCurve(keys.Select(k => new Keyframe(k.t, k.v)).ToArray());
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, prop), curve);
        }

        // ── Bone mapping ──────────────────────────────────────────────────────────

        private static BoneMap MapBones(Animator a)
        {
            var m = new BoneMap();
            if (a.avatar != null && a.avatar.isHuman)
            {
                m.source = "avatar";
                m.hips = a.GetBoneTransform(HumanBodyBones.Hips);
                m.chest = a.GetBoneTransform(HumanBodyBones.UpperChest) ?? a.GetBoneTransform(HumanBodyBones.Chest);
                m.neck = a.GetBoneTransform(HumanBodyBones.Neck);
                m.head = a.GetBoneTransform(HumanBodyBones.Head);
                m.jaw = a.GetBoneTransform(HumanBodyBones.Jaw);
                m.rUpper = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
                m.rLower = a.GetBoneTransform(HumanBodyBones.RightLowerArm);
                m.rHand = a.GetBoneTransform(HumanBodyBones.RightHand);
            }
            else m.source = "names";

            Transform r = a.transform;
            m.hips ??= Find(r, "hips", "pelvis");
            m.chest ??= Find(r, "spine2", "chest", "spine1");
            m.neck ??= Find(r, "neck");
            m.head ??= Find(r, "head");
            m.jaw ??= m.head != null ? Find(m.head, "jaw") : Find(r, "jaw");
            m.rUpper ??= Find(r, "rightarm", "upperarm_r", "r_upperarm", "arm_r");
            m.rLower ??= Find(r, "rightforearm", "lowerarm_r", "forearm_r");
            m.rHand ??= Find(r, "righthand", "hand_r");
            Transform tent = a.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Tentacle"));
            m.tip = tent != null ? TentacleBones(tent).OrderBy(Depth).LastOrDefault() : null;

            foreach (SkinnedMeshRenderer s in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = s.sharedMesh;
                if (mesh == null || mesh.blendShapeCount == 0) continue;
                int best = 0;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string n = mesh.GetBlendShapeName(i).ToLowerInvariant();
                    if (n.Contains("mouth") || n.Contains("jaw") || n.Contains("open")) { best = i; break; }
                }
                m.blendSmr = s; m.blendName = mesh.GetBlendShapeName(best);
                break;
            }
            return m;
        }

        private static Transform Find(Transform root, params string[] tokens)
        {
            foreach (string tok in tokens)
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name.Replace(" ", "").Replace(":", "").ToLowerInvariant().EndsWith(tok)) return t;
            return null;
        }

        private static Transform FindChild(Transform t, string n)
        {
            for (int i = 0; i < t.childCount; i++)
                if (string.Equals(t.GetChild(i).name.Trim(), n, System.StringComparison.OrdinalIgnoreCase)) return t.GetChild(i);
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        // ── Measurement ───────────────────────────────────────────────────────────

        private class RestState
        {
            public List<(Transform t, Vector3 p, Quaternion r, Vector3 s)> xf = new();
            public List<(SkinnedMeshRenderer smr, float[] w)> blends = new();
        }

        private static RestState Capture(Transform actorRoot, Animator anim)
        {
            var st = new RestState();
            foreach (Transform t in anim.GetComponentsInChildren<Transform>(true)) st.xf.Add((t, t.localPosition, t.localRotation, t.localScale));
            foreach (SkinnedMeshRenderer s in anim.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (s.sharedMesh != null && s.sharedMesh.blendShapeCount > 0)
                    st.blends.Add((s, Enumerable.Range(0, s.sharedMesh.blendShapeCount).Select(s.GetBlendShapeWeight).ToArray()));
            return st;
        }

        private static void Restore(RestState st)
        {
            foreach (var x in st.xf) { x.t.localPosition = x.p; x.t.localRotation = x.r; x.t.localScale = x.s; }
            foreach (var b in st.blends) for (int i = 0; i < b.w.Length; i++) b.smr.SetBlendShapeWeight(i, b.w[i]);
        }

        private struct Snapshot
        {
            public Vector3 head, hand, hips, tip; public Quaternion headRot, jawRot; public float headScale, blend;
        }

        private static Snapshot Snap(BoneMap m)
        {
            int bi = m.blendSmr != null ? m.blendSmr.sharedMesh.GetBlendShapeIndex(m.blendName) : -1;
            return new Snapshot
            {
                head = m.head != null ? m.head.position : Vector3.zero,
                headRot = m.head != null ? m.head.rotation : Quaternion.identity,
                headScale = m.head != null ? m.head.lossyScale.y : 0f,
                hand = m.rHand != null ? m.rHand.position : Vector3.zero,
                hips = m.hips != null ? m.hips.position : Vector3.zero,
                tip = m.tip != null ? m.tip.position : Vector3.zero,
                jawRot = m.jaw != null ? m.jaw.rotation : Quaternion.identity,
                blend = bi >= 0 ? m.blendSmr.GetBlendShapeWeight(bi) : -1f,
            };
        }

        private class Run
        {
            public string label, note;
            public Animator anim; public BoneMap map; public bool wasHuman, keep;
            public AnimationClip clip; public RestState rest; public Snapshot restSnap; public int tentacleChains;
            public Dictionary<float, Snapshot> tl = new(), sm = new();

            public string Report(float[] times)
            {
                if (anim == null) return $"{label}: {note}";
                var sb = new StringBuilder();
                sb.AppendLine($"{label}: humanAvatar={wasHuman} avatarNow={(anim.avatar != null ? anim.avatar.name : "none")} map={map.source} " +
                              $"bones[hips={N(map.hips)} chest={N(map.chest)} head={N(map.head)} jaw={N(map.jaw)} rUpper={N(map.rUpper)} rHand={N(map.rHand)}] blend={(map.blendSmr != null ? map.blendSmr.name + ":" + map.blendName : "none")} tentacleChains={tentacleChains} curves={AnimationUtility.GetCurveBindings(clip).Length}");
                foreach (float t in times)
                {
                    Snapshot a = tl[t], b = sm[t];
                    sb.AppendLine($"  t={t:0.0} TL headMove={Cm(a.head, restSnap.head)} handMove={Cm(a.hand, restSnap.hand)} hipsDrift={Cm(a.hips, restSnap.hips)} headScale={a.headScale:0.00} jawDeg={Quaternion.Angle(a.jawRot, restSnap.jawRot):0} blend={a.blend:0} tipMove={Cm(a.tip, restSnap.tip)} | " +
                                  $"SAMPLE headMove={Cm(b.head, restSnap.head)} handMove={Cm(b.hand, restSnap.hand)} headScale={b.headScale:0.00} tipMove={Cm(b.tip, restSnap.tip)} | " +
                                  $"ERR head={Cm(a.head, b.head)} headRot={Quaternion.Angle(a.headRot, b.headRot):0.0}deg hand={Cm(a.hand, b.hand)} tip={Cm(a.tip, b.tip)}");
                }
                return sb.ToString().TrimEnd();
            }

            private static string N(Transform t) => t != null ? t.name : "-";
            private static string Cm(Vector3 a, Vector3 b) => $"{Vector3.Distance(a, b) * 100f:0.0}cm";
        }
    }
}
