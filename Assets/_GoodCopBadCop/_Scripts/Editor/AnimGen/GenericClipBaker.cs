using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.AnimGen
{
    /// <summary>
    /// Bakes an <see cref="AnimPlan"/> into a Generic (Transform + blendshape) clip for one specific rig and variant.
    /// The body is posed exactly like <see cref="BodyClipBaker"/> (base pose through the Humanoid avatar, IK, planted
    /// feet), then Generic-only <see cref="AnimPlan.bones"/> and <see cref="AnimPlan.blendShapes"/> tracks are applied,
    /// and every Transform under the variant's Animator is recorded as local curves. The clip only plays correctly on an
    /// Animator WITHOUT an avatar (Humanoid avatars ignore Generic curves on mapped bones).
    /// </summary>
    public static class GenericClipBaker
    {
        /// <summary>Transforms outside the Animator that Film Studio and the baker fold under the base Animator.</summary>
        public static readonly string[] DefaultFoldRoots = { "Anomaly Container" };

        public static bool IsMutated(string variant) => string.Equals(variant, "mutated", StringComparison.OrdinalIgnoreCase);

        /// <summary>Loose rigs are folded under the BASE Animator only (mutated variants keep their own hierarchy).</summary>
        public static IEnumerable<string> FoldFor(string variant, IEnumerable<string> foldRoots = null) =>
            foldRoots ?? (IsMutated(variant) ? Array.Empty<string>() : DefaultFoldRoots);

        const float RotEpsDeg = 0.02f;
        const float PosEps = 1e-5f;
        const float ShapeEps = 0.01f;

        sealed class Channel
        {
            public Transform t;
            public Vector3 bp, bs;
            public Quaternion br;
            public Vector3[] pos, scl;
            public Quaternion[] rot;
        }

        sealed class Shape
        {
            public SkinnedMeshRenderer smr;
            public int index;
            public string name;
            public float bind;
            public float[] w;
        }

        sealed class ResolvedBoneTrack
        {
            public BoneTrack track;
            public List<List<(Transform t, int depth)>> groups = new List<List<(Transform, int)>>();
        }

        // ── Bake ──────────────────────────────────────────────────────────────────

        /// <param name="variant">"base" or "mutated" (which Humanoid Animator of the prefab to bake for).</param>
        /// <param name="baseClipOverride">Base pose clip; when null, <c>plan.basePose.clip</c> is loaded.</param>
        public static string Bake(AnimPlan plan, string outputClipPath, string rigPath, string variant,
            AnimationClip baseClipOverride = null, IEnumerable<string> foldRoots = null)
        {
            outputClipPath = HumanoidClipWriter.ValidateOutputPath(outputClipPath);
            AnimationClip baseClip = baseClipOverride != null ? baseClipOverride : HumanoidClipWriter.LoadClip(plan.basePose?.clip);
            float baseTime = plan.basePose?.time ?? 0f;
            bool follow = plan.basePose != null && plan.basePose.follow;
            int frames = Mathf.Max(2, Mathf.RoundToInt(plan.duration * plan.fps) + 1);
            float TimeOf(int f) => Mathf.Min(f / (float)plan.fps, plan.duration);

            var report = new StringBuilder();
            var stats = new Dictionary<string, EffectorStats>
            {
                { "Right hand", new EffectorStats() }, { "Left hand", new EffectorStats() },
                { "Right foot", new EffectorStats() }, { "Left foot", new EffectorStats() },
            };

            using (var rig = HumanoidRig.Create(rigPath, HumanoidRig.DefaultBodyModelPath, true, variant))
            {
                List<Transform> folded = rig.Fold(FoldFor(variant, foldRoots));
                Transform root = rig.CharacterRoot;
                var skinned = SkinnedBones(root);
                var boneTracks = ResolveBoneTracks(rig, plan, skinned, report);
                var shapeTracks = ResolveShapes(root, plan, report, out List<Shape> shapes);

                rig.ResetToBind();
                var channels = root.GetComponentsInChildren<Transform>(true).Where(t => t != root)
                    .Select(t => new Channel
                    {
                        t = t, bp = t.localPosition, br = t.localRotation, bs = t.localScale,
                        pos = new Vector3[frames], rot = new Quaternion[frames], scl = new Vector3[frames],
                    }).ToList();
                foreach (var s in shapes) { s.bind = s.smr.GetBlendShapeWeight(s.index); s.w = new float[frames]; }

                var fingerSet = FingerBones(rig);
                bool fingers = PlanEval.Has(plan.tracks.rightFingers) || PlanEval.Has(plan.tracks.leftFingers);
                var pose = new HumanPose();

                for (int fr = 0; fr < frames; fr++)
                {
                    float t = TimeOf(fr);
                    SampleBase(rig, baseClip, follow ? baseTime + t : baseTime);
                    foreach (var s in shapes) s.smr.SetBlendShapeWeight(s.index, s.bind);

                    BodyClipBaker.PoseFrame(rig, plan, t, stats);
                    if (fingers) ApplyFingers(rig, plan, t, channels, fingerSet, ref pose);
                    ApplyBones(rig, boneTracks, t);
                    foreach (var (track, targets) in shapeTracks)
                        if (PlanEval.TryScalar(track.keys, t, out float w))
                            foreach (var s in targets) s.smr.SetBlendShapeWeight(s.index, Mathf.Clamp(w, 0f, 100f));

                    foreach (var c in channels)
                    {
                        c.pos[fr] = c.t.localPosition;
                        c.rot[fr] = c.t.localRotation;
                        c.scl[fr] = c.t.localScale;
                    }
                    foreach (var s in shapes) s.w[fr] = s.smr.GetBlendShapeWeight(s.index);
                }

                bool hasRoot = PlanEval.Has(plan.tracks.root);
                if (plan.loop && !hasRoot)
                {
                    foreach (var c in channels) { c.pos[frames - 1] = c.pos[0]; c.rot[frames - 1] = c.rot[0]; c.scl[frames - 1] = c.scl[0]; }
                    foreach (var s in shapes) s.w[frames - 1] = s.w[0];
                }

                var clip = new AnimationClip { frameRate = plan.fps, name = System.IO.Path.GetFileNameWithoutExtension(outputClipPath) };
                int written = Write(clip, root, channels, shapes, frames, TimeOf, out int rotCh, out int posCh, out int sclCh, out int shapeCh);
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = plan.loop;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                HumanoidClipWriter.SaveAsset(clip, outputClipPath);

                string rigName = rig.Root.name.Replace(" (AnimGen)", "");
                var sb = new StringBuilder();
                sb.AppendLine($"Baked GENERIC {outputClipPath} on {rigName} [{(string.IsNullOrEmpty(variant) ? "base" : variant)}] animator '{root.name}': " +
                              $"{plan.duration:0.###}s @ {plan.fps}fps, {frames} frames, loop={plan.loop}, base={(baseClip ? baseClip.name : "bind pose")}{(follow ? " (follow)" : "")}, plantFeet={plan.plantFeet}");
                sb.AppendLine($"Curves: {written} ({rotCh} rotated / {posCh} moved / {sclCh} scaled transforms, {shapeCh} blendshapes); folded: {(folded.Count > 0 ? string.Join(", ", folded.Select(f => f.name)) : "none")}");
                foreach (var b in boneTracks)
                    sb.AppendLine($"bones '{b.track.bone}': {b.groups.Count} match(es), {b.groups.Sum(g => g.Count)} bone(s){(b.track.chain ? " (chain)" : "")}{(b.track.wave != null ? " +wave" : "")}");
                int clamped = 0;
                foreach (var kv in stats)
                {
                    if (kv.Value.Range != "untouched") sb.AppendLine($"{kv.Key} character-space path: {kv.Value.Range}; reach-clamped frames: {kv.Value.clamped}");
                    clamped += kv.Value.clamped;
                }
                if (clamped > 0) sb.AppendLine("WARNING: some IK targets were out of reach. Lower the hips less, or pull hand/foot targets closer.");
                sb.Append(report);
                return sb.ToString().TrimEnd();
            }
        }

        /// <summary>Retargets any clip (Humanoid or Generic) onto one rig variant as a Generic clip (same length/loop).</summary>
        public static string ConvertClip(AnimationClip src, string outputClipPath, string rigPath, string variant, IEnumerable<string> foldRoots = null)
        {
            if (src == null) throw new ArgumentException("Source clip is null.");
            var plan = new AnimPlan
            {
                name = src.name,
                duration = Mathf.Max(src.length, 1f / 30f),
                fps = Mathf.Clamp(Mathf.RoundToInt(src.frameRate <= 0f ? 30f : src.frameRate), 24, 60),
                loop = src.isLooping,
                plantFeet = false,
                basePose = new BasePose { follow = true },
            };
            PlanEval.Normalize(plan);
            return Bake(plan, outputClipPath, rigPath, variant, src, foldRoots);
        }

        // ── Base pose ─────────────────────────────────────────────────────────────

        static void SampleBase(HumanoidRig rig, AnimationClip clip, float time)
        {
            rig.ResetToBind();
            if (clip != null)
            {
                float len = Mathf.Max(clip.length, 1e-4f);
                float tt = clip.isLooping ? Mathf.Repeat(time, len) : Mathf.Clamp(time, 0f, len);
                if (clip.humanMotion)
                {
                    clip.SampleAnimation(rig.AnimatorObject, tt);
                }
                else
                {
                    // Generic base (e.g. an earlier Generic bake): Humanoid avatars ignore Generic curves on mapped bones.
                    Avatar av = rig.Animator.avatar;
                    rig.Animator.avatar = null;
                    clip.SampleAnimation(rig.AnimatorObject, tt);
                    rig.Animator.avatar = av;
                }
            }
            rig.CharacterRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        // ── Fingers (muscle-space curl, without letting the muscle round-trip touch the body) ──

        static HashSet<Transform> FingerBones(HumanoidRig rig)
        {
            var set = new HashSet<Transform>();
            for (int i = (int)HumanBodyBones.LeftThumbProximal; i <= (int)HumanBodyBones.RightLittleDistal; i++)
            {
                var t = rig.Animator.GetBoneTransform((HumanBodyBones)i);
                if (t != null) set.Add(t);
            }
            return set;
        }

        static void ApplyFingers(HumanoidRig rig, AnimPlan plan, float t, List<Channel> channels, HashSet<Transform> fingerSet, ref HumanPose pose)
        {
            var snap = new List<(Transform t, Vector3 p, Quaternion r)>(channels.Count + 1);
            foreach (var c in channels) if (!fingerSet.Contains(c.t)) snap.Add((c.t, c.t.localPosition, c.t.localRotation));
            Vector3 rootP = rig.CharacterRoot.position;
            Quaternion rootR = rig.CharacterRoot.rotation;

            rig.PoseHandler.GetHumanPose(ref pose);
            HumanoidClipWriter.ApplyFingers(plan, t, ref pose);
            rig.PoseHandler.SetHumanPose(ref pose);

            rig.CharacterRoot.SetPositionAndRotation(rootP, rootR);
            foreach (var (tr, p, r) in snap) { tr.localPosition = p; tr.localRotation = r; }
        }

        // ── Generic bone tracks ───────────────────────────────────────────────────

        static HashSet<Transform> SkinnedBones(Transform root)
        {
            var set = new HashSet<Transform>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var b in smr.bones) if (b != null) set.Add(b);
            return set;
        }

        static List<ResolvedBoneTrack> ResolveBoneTracks(HumanoidRig rig, AnimPlan plan, HashSet<Transform> skinned, StringBuilder report)
        {
            var list = new List<ResolvedBoneTrack>();
            if (plan.bones == null) return list;
            foreach (var bt in plan.bones)
            {
                if (bt == null || string.IsNullOrWhiteSpace(bt.bone)) continue;
                List<Transform> matches = MatchBones(rig, bt.bone.Trim());
                if (matches.Count == 0) throw new ArgumentException($"bones: no bone matches '{bt.bone}' on {rig.CharacterRoot.name}. Use AnimGenDescribeGenericRig / the actor's rig info for valid names.");
                var r = new ResolvedBoneTrack { track = bt };
                foreach (var m in matches)
                {
                    var group = new List<(Transform, int)>();
                    if (bt.chain)
                    {
                        foreach (var d in m.GetComponentsInChildren<Transform>(true))
                            if (skinned.Contains(d)) group.Add((d, Depth(d, m)));
                        if (group.Count == 0) group.Add((m, 0));
                        int min = group.Min(g => g.Item2);
                        for (int i = 0; i < group.Count; i++) group[i] = (group[i].Item1, group[i].Item2 - min);
                    }
                    else group.Add((m, 0));
                    r.groups.Add(group);
                }
                list.Add(r);
            }
            return list;
        }

        static int Depth(Transform t, Transform ancestor)
        {
            int d = 0;
            for (Transform p = t; p != null && p != ancestor; p = p.parent) d++;
            return d;
        }

        /// <summary>HumanBodyBones name, path relative to the Animator, '*' glob, exact name, or ':'/'_'-suffixed name (mixamorig:Head).</summary>
        public static List<Transform> MatchBones(HumanoidRig rig, string spec)
        {
            Transform root = rig.CharacterRoot;
            var result = new List<Transform>();
            if (!char.IsDigit(spec[0]) && Enum.TryParse(spec, true, out HumanBodyBones hb) && hb != HumanBodyBones.LastBone)
            {
                var t = rig.Animator.GetBoneTransform(hb);
                if (t != null) { result.Add(t); return result; }
            }
            var all = root.GetComponentsInChildren<Transform>(true).Where(t => t != root).ToList();
            if (spec.Contains('/'))
            {
                var t = root.Find(spec);
                if (t != null) result.Add(t);
                return result;
            }
            if (spec.Contains('*'))
            {
                var rx = new Regex("^" + Regex.Escape(spec).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase);
                var hits = all.Where(t => rx.IsMatch(t.name)).ToList();
                // Innermost matches: "Tentacle*" picks each 'Tentacle (n)', not the 'Tentacles' folder holding them.
                result.AddRange(hits.Where(h => !hits.Any(o => o != h && o.IsChildOf(h))));
                return result;
            }
            var exact = all.Where(t => t.name == spec).ToList();
            if (exact.Count == 0)
                exact = all.Where(t => t.name.EndsWith(":" + spec, StringComparison.OrdinalIgnoreCase) || string.Equals(t.name, spec, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count > 1)
                throw new ArgumentException($"bones: '{spec}' is ambiguous ({exact.Count} matches: {string.Join(", ", exact.Take(4).Select(e => AnimationUtility.CalculateTransformPath(e, root)))}). Use a path or a more specific name.");
            result.AddRange(exact);
            return result;
        }

        static void ApplyBones(HumanoidRig rig, List<ResolvedBoneTrack> tracks, float t)
        {
            Quaternion c = rig.CharacterRoot.rotation;
            foreach (var r in tracks)
            {
                var bt = r.track;
                bool hasRot = PlanEval.TryRot(bt.keys, t, out var q);
                bool hasPos = PlanEval.TryVec3(bt.keys, k => k.pos, t, out var p);
                bool hasScale = PlanEval.TryScale(bt.keys, t, out var s);
                var wave = bt.wave;
                float gain = 1f;
                if (wave != null && PlanEval.TryScalar(bt.keys, t, out float g)) gain = g;
                Vector3 axis = wave?.axis != null && wave.axis.Length >= 3 ? new Vector3(wave.axis[0], wave.axis[1], wave.axis[2]) : Vector3.forward;
                if (axis.sqrMagnitude < 1e-6f) axis = Vector3.forward;
                axis.Normalize();

                foreach (var group in r.groups)
                {
                    foreach (var (bone, depth) in group)
                    {
                        Quaternion delta = hasRot && (bt.chain || depth == 0) ? q : Quaternion.identity;
                        if (wave != null && Mathf.Abs(wave.amplitude * gain) > 1e-4f)
                        {
                            float ang = wave.amplitude * gain * Mathf.Pow(Mathf.Max(0f, wave.falloff), depth)
                                        * Mathf.Sin(2f * Mathf.PI * wave.frequency * t + wave.phase - depth * wave.phaseStep);
                            delta *= Quaternion.AngleAxis(ang, axis);
                        }
                        if (delta == Quaternion.identity) continue;
                        if (bt.IsLocal) bone.localRotation *= delta;
                        else HumanoidRig.RotateInFrame(bone, c, delta);
                    }
                    Transform head = group[0].t;
                    if (hasPos)
                    {
                        if (bt.IsLocal) head.localPosition += p;
                        else head.position += c * p;
                    }
                    if (hasScale) head.localScale = Vector3.Scale(head.localScale, s);
                }
            }
        }

        static List<(BlendShapeTrack, List<Shape>)> ResolveShapes(Transform root, AnimPlan plan, StringBuilder report, out List<Shape> all)
        {
            all = new List<Shape>();
            var result = new List<(BlendShapeTrack, List<Shape>)>();
            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null && s.sharedMesh.blendShapeCount > 0).ToList();
            foreach (var smr in smrs)
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                    all.Add(new Shape { smr = smr, index = i, name = smr.sharedMesh.GetBlendShapeName(i) });
            if (plan.blendShapes == null) return result;
            foreach (var bs in plan.blendShapes)
            {
                if (bs == null || string.IsNullOrWhiteSpace(bs.shape)) continue;
                var targets = all.Where(s => string.Equals(s.name, bs.shape, StringComparison.OrdinalIgnoreCase)
                                             && (string.IsNullOrEmpty(bs.renderer) || string.Equals(s.smr.name, bs.renderer, StringComparison.OrdinalIgnoreCase))).ToList();
                if (targets.Count == 0)
                    throw new ArgumentException($"blendShapes: no shape '{bs.shape}'{(string.IsNullOrEmpty(bs.renderer) ? "" : $" on '{bs.renderer}'")}. Available: {string.Join(", ", all.Select(a => a.smr.name + ":" + a.name).Distinct().Take(12))}");
                result.Add((bs, targets));
            }
            return result;
        }

        // ── Writing ───────────────────────────────────────────────────────────────

        static int Write(AnimationClip clip, Transform root, List<Channel> channels, List<Shape> shapes, int frames, Func<int, float> timeOf,
            out int rotCh, out int posCh, out int sclCh, out int shapeCh)
        {
            rotCh = posCh = sclCh = shapeCh = 0;
            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            var times = new float[frames];
            for (int f = 0; f < frames; f++) times[f] = timeOf(f);
            var buf = new float[frames];

            void Add(string path, Type type, string prop, float tol, Func<int, float> value)
            {
                for (int f = 0; f < frames; f++) buf[f] = value(f);
                bindings.Add(EditorCurveBinding.FloatCurve(path, type, prop));
                curves.Add(Reduced(times, buf, tol));
            }

            foreach (var c in channels)
            {
                string path = AnimationUtility.CalculateTransformPath(c.t, root);
                bool rotChanged = false, posChanged = false, sclChanged = false;
                for (int f = 0; f < frames; f++)
                {
                    if (f > 0 && Quaternion.Dot(c.rot[f], c.rot[f - 1]) < 0f)
                        c.rot[f] = new Quaternion(-c.rot[f].x, -c.rot[f].y, -c.rot[f].z, -c.rot[f].w);
                    rotChanged |= Quaternion.Angle(c.rot[f], c.br) > RotEpsDeg;
                    posChanged |= (c.pos[f] - c.bp).sqrMagnitude > PosEps * PosEps;
                    sclChanged |= (c.scl[f] - c.bs).sqrMagnitude > PosEps * PosEps;
                }
                // Position tolerance relative to the bone's offset (local units differ wildly between rigs, e.g. x100 armatures).
                float posTol = Mathf.Max(1e-6f, c.bp.magnitude * 2e-4f);
                if (rotChanged)
                {
                    rotCh++;
                    Add(path, typeof(Transform), "m_LocalRotation.x", RotTol, f => c.rot[f].x);
                    Add(path, typeof(Transform), "m_LocalRotation.y", RotTol, f => c.rot[f].y);
                    Add(path, typeof(Transform), "m_LocalRotation.z", RotTol, f => c.rot[f].z);
                    Add(path, typeof(Transform), "m_LocalRotation.w", RotTol, f => c.rot[f].w);
                }
                if (posChanged)
                {
                    posCh++;
                    Add(path, typeof(Transform), "m_LocalPosition.x", posTol, f => c.pos[f].x);
                    Add(path, typeof(Transform), "m_LocalPosition.y", posTol, f => c.pos[f].y);
                    Add(path, typeof(Transform), "m_LocalPosition.z", posTol, f => c.pos[f].z);
                }
                if (sclChanged)
                {
                    sclCh++;
                    float sTol = Mathf.Max(1e-6f, c.bs.magnitude * 2e-4f);
                    Add(path, typeof(Transform), "m_LocalScale.x", sTol, f => c.scl[f].x);
                    Add(path, typeof(Transform), "m_LocalScale.y", sTol, f => c.scl[f].y);
                    Add(path, typeof(Transform), "m_LocalScale.z", sTol, f => c.scl[f].z);
                }
            }
            foreach (var s in shapes)
            {
                if (!s.w.Any(w => Mathf.Abs(w - s.bind) > ShapeEps)) continue;
                shapeCh++;
                Add(AnimationUtility.CalculateTransformPath(s.smr.transform, root), typeof(SkinnedMeshRenderer), "blendShape." + s.name, 0.05f, f => s.w[f]);
            }
            if (bindings.Count > 0) AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            return bindings.Count;
        }

        /// <summary>
        /// Quaternion-component tolerance (~0.11°). Unity stores every rotation key three times in text clips
        /// (runtime, editor and generated Euler curves), so key count dominates file size; below ~0.2° is invisible.
        /// </summary>
        const float RotTol = 1e-3f;

        /// <summary>
        /// Keyframe reduction: keeps a frame only where the curve through the kept neighbours (Hermite with the dense
        /// data's own slopes) would miss it by more than <paramref name="tol"/>. Smooth channels shrink to a few keys.
        /// </summary>
        static AnimationCurve Reduced(float[] t, float[] v, float tol)
        {
            int n = t.Length;
            var slope = new float[n];
            for (int i = 0; i < n; i++)
            {
                int a = Mathf.Max(0, i - 1), b = Mathf.Min(n - 1, i + 1);
                float dt = t[b] - t[a];
                slope[i] = dt > 1e-6f ? (v[b] - v[a]) / dt : 0f;
            }
            var keep = new List<int> { 0 };
            int last = 0;
            for (int i = 1; i < n - 1; i++)
            {
                // Can the segment last -> i+1 represent every frame in between?
                int end = i + 1;
                bool ok = true;
                for (int j = last + 1; j < end && ok; j++)
                    ok = Mathf.Abs(Hermite(t[last], v[last], slope[last], t[end], v[end], slope[end], t[j]) - v[j]) <= tol;
                if (!ok) { keep.Add(i); last = i; }
            }
            if (n > 1) keep.Add(n - 1);
            var keys = new Keyframe[keep.Count];
            for (int k = 0; k < keep.Count; k++) keys[k] = new Keyframe(t[keep[k]], v[keep[k]], slope[keep[k]], slope[keep[k]]);
            return new AnimationCurve(keys);
        }

        static float Hermite(float t0, float v0, float m0, float t1, float v1, float m1, float t)
        {
            float dt = t1 - t0;
            if (dt <= 1e-6f) return v0;
            float s = (t - t0) / dt, s2 = s * s, s3 = s2 * s;
            return (2 * s3 - 3 * s2 + 1) * v0 + (s3 - 2 * s2 + s) * dt * m0 + (-2 * s3 + 3 * s2) * v1 + (s3 - s2) * dt * m1;
        }

        // ── Rig description (for planning) ────────────────────────────────────────

        /// <summary>Character-space proportions (in the given base pose), extra skinned-bone chains and blendshapes of one rig variant.</summary>
        public static string DescribeRig(string rigPath, string variant, AnimationClip baseClip, IEnumerable<string> foldRoots = null)
        {
            using (var rig = HumanoidRig.Create(rigPath, HumanoidRig.DefaultBodyModelPath, true, variant))
            {
                rig.Fold(FoldFor(variant, foldRoots));
                SampleBase(rig, baseClip, 0f);
                Transform root = rig.CharacterRoot;
                float R(float f) => Mathf.Round(f * 100f) / 100f;
                float[] P(Transform t) => t == null ? null : new[] { R(t.position.x), R(t.position.y), R(t.position.z) };

                var mapped = new HashSet<Transform>();
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    var t = rig.Animator.GetBoneTransform((HumanBodyBones)i);
                    if (t != null) mapped.Add(t);
                }
                var skinned = SkinnedBones(root);
                var extra = skinned.Where(b => !mapped.Contains(b) && b.IsChildOf(root)).ToList();
                var chainRoots = extra.Where(b => b.parent == null || !extra.Contains(b.parent)).ToList();
                var chains = new List<object>();
                int hiddenChains = 0;
                foreach (var cr in chainRoots)
                {
                    int links = cr.GetComponentsInChildren<Transform>(true).Count(extra.Contains);
                    if (links < 2) continue;
                    if (!cr.gameObject.activeInHierarchy) { hiddenChains++; continue; }
                    Transform label = cr;
                    for (Transform p = cr.parent; p != null && p != root && !mapped.Contains(p); p = p.parent)
                        if (!string.Equals(p.name, "Armature", StringComparison.OrdinalIgnoreCase)) { label = p; break; }
                    string name = root.GetComponentsInChildren<Transform>(true).Count(t => t.name == label.name) == 1
                        ? label.name : AnimationUtility.CalculateTransformPath(label, root);
                    Transform attach = cr.parent;
                    while (attach != null && attach != root && !mapped.Contains(attach)) attach = attach.parent;
                    chains.Add(new
                    {
                        bone = name, links, attachedTo = attach != null && attach != root ? HumanBoneName(rig, attach) : "root",
                        tip = P(cr.GetComponentsInChildren<Transform>(true).Last(extra.Contains)),
                    });
                }
                var shapes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(s => s.sharedMesh != null && s.sharedMesh.blendShapeCount > 0)
                    .SelectMany(s => Enumerable.Range(0, s.sharedMesh.blendShapeCount).Select(i => s.name + ":" + s.sharedMesh.GetBlendShapeName(i)))
                    .Take(24).ToList();
                var bounds = rig.BoneBounds();
                var named = new[] { "jaw", "tongue", "tail", "eye" };
                var keyword = root.GetComponentsInChildren<Transform>(true).Where(t => named.Any(n => t.name.ToLowerInvariant().Contains(n)))
                    .Select(t => t.name).Distinct().Take(10).ToList();

                return JsonConvert.SerializeObject(new
                {
                    variant = string.IsNullOrEmpty(variant) ? "base" : variant,
                    animator = root.name,
                    basePose = baseClip != null ? baseClip.name : "bind pose",
                    approxHeight = R(bounds.max.y + 0.1f),
                    hips = P(rig.Hips), chest = P(rig.ChestBone), head = P(rig.Head),
                    rightHand = P(rig.RightArm.end), leftHand = P(rig.LeftArm.end),
                    armReach = R(rig.RightArm.Reach), legReach = R(rig.RightLeg.Reach),
                    scale = R(rig.Hips.position.y / 1.0f),
                    extraChains = chains,
                    hiddenChains,
                    blendShapes = shapes,
                    keywordBones = keyword,
                });
            }
        }

        static string HumanBoneName(HumanoidRig rig, Transform t)
        {
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                if (rig.Animator.GetBoneTransform((HumanBodyBones)i) == t) return ((HumanBodyBones)i).ToString();
            return t.name;
        }
    }
}
