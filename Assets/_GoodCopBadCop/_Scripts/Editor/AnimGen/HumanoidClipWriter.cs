using System;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.AnimGen
{
    /// <summary>Per-side hand/foot travel stats reported after a bake.</summary>
    public sealed class EffectorStats
    {
        public int clamped;
        public Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        public void Add(Vector3 p) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        public string Range => float.IsInfinity(min.x) ? "untouched" : $"x[{min.x:F3},{max.x:F3}] y[{min.y:F3},{max.y:F3}] z[{min.z:F3},{max.z:F3}]";
    }

    /// <summary>Collects HumanPose frames and writes them as a Humanoid AnimationClip (RootT/RootQ + muscle curves).</summary>
    public sealed class HumanoidClipWriter
    {
        static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
        static readonly string[] RootProps = { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
        static int[,,] _curlIdx;
        static int[,] _spreadIdx;

        readonly AnimPlan _plan;
        readonly float[,] _values;
        public int FrameCount { get; }

        public HumanoidClipWriter(AnimPlan plan)
        {
            _plan = plan;
            FrameCount = Mathf.Max(2, Mathf.RoundToInt(plan.duration * plan.fps) + 1);
            _values = new float[FrameCount, RootProps.Length + HumanTrait.MuscleCount];
        }

        public float TimeOf(int frame) => Mathf.Min(frame / (float)_plan.fps, _plan.duration);

        // ---------- shared helpers ----------

        /// <summary>HumanTrait muscle name -> AnimationClip binding name ("Left Index 1 Stretched" -> "LeftHand.Index.1 Stretched").</summary>
        public static string MuscleToProperty(string muscle)
        {
            var parts = muscle.Split(' ');
            if (parts.Length >= 3 && (parts[0] == "Left" || parts[0] == "Right") && Array.IndexOf(Fingers, parts[1]) >= 0)
                return $"{parts[0]}Hand.{parts[1]}.{string.Join(" ", parts, 2, parts.Length - 2)}";
            return muscle;
        }

        public static AnimPlan Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("planJson is empty.");
            var plan = JsonConvert.DeserializeObject<AnimPlan>(json) ?? throw new ArgumentException("planJson did not parse.");
            PlanEval.Normalize(plan);
            return plan;
        }

        /// <summary>Loads "Assets/...anim" or a clip inside a model as "Assets/...fbx :: ClipName".</summary>
        public static AnimationClip LoadClip(string path)
        {
            path = HumanoidRig.ToAssetPath(path);
            if (string.IsNullOrEmpty(path)) return null;
            string clipName = null;
            int sep = path.IndexOf("::", StringComparison.Ordinal);
            if (sep >= 0)
            {
                clipName = path.Substring(sep + 2).Trim();
                path = path.Substring(0, sep).Trim();
            }
            AnimationClip clip = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(o is AnimationClip c) || c.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                if (clipName == null || c.name == clipName) { clip = c; break; }
            }
            if (clip == null) throw new ArgumentException($"AnimationClip not found: {path}{(clipName != null ? " :: " + clipName : "")}");
            return clip;
        }

        public static string ValidateOutputPath(string outputClipPath)
        {
            outputClipPath = HumanoidRig.ToAssetPath(outputClipPath);
            if (string.IsNullOrEmpty(outputClipPath) || !outputClipPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("outputClipPath must be an Assets/... .anim path.");
            return outputClipPath;
        }

        static void EnsureFingerTables()
        {
            if (_curlIdx != null) return;
            _curlIdx = new int[2, 5, 3];
            _spreadIdx = new int[2, 5];
            for (int s = 0; s < 2; s++)
            for (int f = 0; f < 5; f++)
            {
                string side = s == 0 ? "Right" : "Left";
                for (int seg = 0; seg < 3; seg++) _curlIdx[s, f, seg] = Array.IndexOf(HumanTrait.MuscleName, $"{side} {Fingers[f]} {seg + 1} Stretched");
                _spreadIdx[s, f] = Array.IndexOf(HumanTrait.MuscleName, $"{side} {Fingers[f]} Spread");
            }
        }

        /// <summary>Overrides finger muscles from the plan's finger tracks (curl 0 open .. 1 fist; spread -1..1).</summary>
        public static void ApplyFingers(AnimPlan plan, float t, ref HumanPose pose)
        {
            EnsureFingerTables();
            for (int s = 0; s < 2; s++)
            {
                var track = s == 0 ? plan.tracks.rightFingers : plan.tracks.leftFingers;
                if (PlanEval.TryVec(track, k => k.curl, 5, t, out var curl))
                    for (int f = 0; f < 5; f++)
                    for (int seg = 0; seg < 3; seg++)
                    {
                        int mi = _curlIdx[s, f, seg];
                        if (mi >= 0) pose.muscles[mi] = Mathf.Clamp(1f - 2f * curl[f], -1f, 1f);
                    }
                if (PlanEval.TrySpread(track, t, out var spread))
                    for (int f = 0; f < 5; f++)
                        if (_spreadIdx[s, f] >= 0) pose.muscles[_spreadIdx[s, f]] = Mathf.Clamp(spread, -1f, 1f);
            }
        }

        // ---------- recording / writing ----------

        public void Record(int frame, HumanPose pose)
        {
            _values[frame, 0] = pose.bodyPosition.x; _values[frame, 1] = pose.bodyPosition.y; _values[frame, 2] = pose.bodyPosition.z;
            _values[frame, 3] = pose.bodyRotation.x; _values[frame, 4] = pose.bodyRotation.y; _values[frame, 5] = pose.bodyRotation.z; _values[frame, 6] = pose.bodyRotation.w;
            for (int m = 0; m < HumanTrait.MuscleCount; m++) _values[frame, RootProps.Length + m] = pose.muscles[m];
        }

        /// <param name="rootMotionXZ">Keep horizontal root translation as root motion (not baked into pose).</param>
        /// <param name="rootMotionYaw">Keep root yaw as root motion.</param>
        public void Save(string outputClipPath, bool rootMotionXZ, bool rootMotionYaw)
        {
            int frames = FrameCount, cols = _values.GetLength(1);
            if (_plan.loop)
            {
                // Pose channels loop exactly; root translation/yaw keeps its travel when it's real root motion.
                for (int c = 0; c < cols; c++)
                {
                    bool isRootT = c < 3, isRootQ = c >= 3 && c < 7;
                    if ((isRootT && rootMotionXZ) || (isRootQ && rootMotionYaw)) continue;
                    _values[frames - 1, c] = _values[0, c];
                }
            }

            var clip = new AnimationClip { frameRate = _plan.fps, name = Path.GetFileNameWithoutExtension(outputClipPath) };
            for (int c = 0; c < cols; c++)
            {
                string prop = c < RootProps.Length ? RootProps[c] : MuscleToProperty(HumanTrait.MuscleName[c - RootProps.Length]);
                var keys = new Keyframe[frames];
                for (int fr = 0; fr < frames; fr++) keys[fr] = new Keyframe(TimeOf(fr), _values[fr, c]);
                var curve = new AnimationCurve(keys);
                for (int k = 0; k < frames; k++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto);
                }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), prop), curve);
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = _plan.loop;
            settings.loopBlendPositionY = true;          // bake vertical into pose
            settings.keepOriginalPositionY = true;
            settings.loopBlendPositionXZ = !rootMotionXZ;
            settings.keepOriginalPositionXZ = true;
            settings.loopBlendOrientation = !rootMotionYaw;
            settings.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            SaveAsset(clip, outputClipPath);
        }

        internal static void SaveAsset(AnimationClip clip, string path)
        {
            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            AnimationClip saved;
            if (existing != null)
            {
                Undo.RecordObject(existing, "AnimGen Bake");
                EditorUtility.CopySerialized(clip, existing);
                existing.name = Path.GetFileNameWithoutExtension(path);
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(clip);
                saved = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(clip, path);
                saved = clip;
            }
            // Save only this clip: a project-wide SaveAssets() re-serialises every dirty asset (and legacy plugin
            // metas) on each bake, which made multi-clip film builds take minutes.
            AssetDatabase.SaveAssetIfDirty(saved);
        }

        static void EnsureFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir) || AssetDatabase.IsValidFolder(dir)) return;
            string parent = Path.GetDirectoryName(dir)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
        }
    }
}
