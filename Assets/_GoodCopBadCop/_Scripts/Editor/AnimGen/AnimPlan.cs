using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoodCopBadCop.AnimGen
{
    /// <summary>
    /// Compact, LLM-authored animation plan. Key poses per control + timing/easing;
    /// the solver turns it into dense Humanoid muscle curves.
    /// View space: meters/degrees, origin at the eye (head bone + camera offset), +X right, +Y up, +Z forward.
    /// </summary>
    [Serializable]
    public class AnimPlan
    {
        public string name;
        public float duration = 1f;
        public int fps = 30;
        public bool loop;
        /// <summary>"offset" (default): hand/elbow values are deltas from the base pose. "view": absolute view-space values.</summary>
        public string space = "offset";
        public BasePose basePose;
        public CameraSettings camera;
        public PlanTracks tracks = new PlanTracks();
        public NoiseSettings noise;
        /// <summary>Full body: feet without a foot track stay locked where the base pose put them (IK) while hips/spine move.</summary>
        public bool plantFeet = true;
        /// <summary>Full body: "chest" (default) = hand targets ride with the chest (bend forward carries the hands); "character" = hands fixed in character space.</summary>
        public string handSpace = "chest";

        /// <summary>Generic bakes only: free tracks on any bone or bone chain (tentacles, jaw, extra bones, squash/stretch).</summary>
        public List<BoneTrack> bones;
        /// <summary>Generic bakes only: blendshape weight tracks (keys use <see cref="PlanKey.value"/>, 0-100).</summary>
        public List<BlendShapeTrack> blendShapes;

        public bool HandsFollowChest => !string.Equals(handSpace, "character", StringComparison.OrdinalIgnoreCase);

        public bool IsOffsetSpace => !string.Equals(space, "view", StringComparison.OrdinalIgnoreCase);
    }

    [Serializable]
    public class BasePose
    {
        public string clip;
        public float time;
        /// <summary>If true, the base clip plays along with the plan (layered on top) instead of being frozen at <see cref="time"/>.</summary>
        public bool follow;
    }

    [Serializable]
    public class CameraSettings
    {
        public float[] offset;
        public float fov = 60f;
    }

    [Serializable]
    public class NoiseSettings
    {
        public float position;
        public float rotation;
        public float frequency = 1.5f;
        public int seed = 1;
    }

    [Serializable]
    public class PlanTracks
    {
        // Shared (POV arms + full body)
        public List<PlanKey> rightHand, leftHand, rightElbow, leftElbow, rightFingers, leftFingers, spine, head;
        // Full body only
        public List<PlanKey> hips, chest, neck, rightShoulder, leftShoulder, rightFoot, leftFoot, rightKnee, leftKnee, root;

        public IEnumerable<List<PlanKey>> All()
        {
            yield return rightHand; yield return leftHand; yield return rightElbow; yield return leftElbow;
            yield return rightFingers; yield return leftFingers; yield return spine; yield return head;
            yield return hips; yield return chest; yield return neck; yield return rightShoulder; yield return leftShoulder;
            yield return rightFoot; yield return leftFoot; yield return rightKnee; yield return leftKnee; yield return root;
        }
    }

    [Serializable]
    public class PlanKey
    {
        public float t;
        public float[] pos;
        public float[] rot;
        public float[] hint;
        /// <summary>Per finger [thumb, index, middle, ring, little]: 0 open, 0.5 relaxed, 1 fist.</summary>
        public float[] curl;
        public float? spread;
        /// <summary>Generic bone tracks: local scale multiplier, [x,y,z] or [uniform].</summary>
        public float[] scale;
        /// <summary>Scalar channel: blendshape weight (0-100) or bone-track wave gain (0..1+).</summary>
        public float? value;
        /// <summary>Easing of the segment arriving at this key: linear, in, out, inOut, snap, back, anticipate, step.</summary>
        public string ease;
    }

    /// <summary>
    /// Generic-only track on a named bone (or every skinned bone under it when <see cref="chain"/> is true).
    /// <c>bone</c>: HumanBodyBones name ("Head", "RightHand"), exact transform name, a path relative to the Animator,
    /// or a '*' glob ("Tentacle*"). rot/pos are deltas; scale multiplies the bind scale.
    /// </summary>
    [Serializable]
    public class BoneTrack
    {
        public string bone;
        /// <summary>Apply rot/wave to every skinned bone under the match (accumulates into a curl).</summary>
        public bool chain;
        /// <summary>"character" (default): rot/pos axes are character axes. "local": the bone's own axes.</summary>
        public string space = "character";
        public WaveSettings wave;
        public List<PlanKey> keys;

        public bool IsLocal => string.Equals(space, "local", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Procedural sine rotation along a chain: link i gets amplitude*falloff^i*sin(2π·frequency·t + phase − i·phaseStep).</summary>
    [Serializable]
    public class WaveSettings
    {
        public float[] axis;
        public float amplitude = 15f;
        public float frequency = 1f;
        public float phase;
        public float phaseStep = 0.6f;
        public float falloff = 1f;
    }

    [Serializable]
    public class BlendShapeTrack
    {
        /// <summary>Optional SkinnedMeshRenderer name filter (empty = every renderer that has the shape).</summary>
        public string renderer;
        public string shape;
        public List<PlanKey> keys;
    }

    public static class PlanEval
    {
        public static void Normalize(AnimPlan plan)
        {
            if (plan.tracks == null) plan.tracks = new PlanTracks();
            plan.duration = Mathf.Clamp(plan.duration, 0.05f, 30f);
            plan.fps = Mathf.Clamp(plan.fps <= 0 ? 30 : plan.fps, 12, 120);
            foreach (var track in plan.tracks.All())
                track?.Sort((a, b) => a.t.CompareTo(b.t));
            if (plan.bones != null)
                foreach (var b in plan.bones) b?.keys?.Sort((a, c) => a.t.CompareTo(c.t));
            if (plan.blendShapes != null)
                foreach (var b in plan.blendShapes) b?.keys?.Sort((a, c) => a.t.CompareTo(c.t));
        }

        public static bool TryScalar(List<PlanKey> keys, float t, out float v)
        {
            v = 0f;
            if (!TryVec(keys, k => k.value.HasValue ? new[] { k.value.Value } : null, 1, t, out var r)) return false;
            v = r[0];
            return true;
        }

        public static bool TryScale(List<PlanKey> keys, float t, out Vector3 s)
        {
            s = Vector3.one;
            if (!TryVec(keys, k => k.scale == null ? null : k.scale.Length >= 3 ? k.scale : k.scale.Length == 1 ? new[] { k.scale[0], k.scale[0], k.scale[0] } : null, 3, t, out var r)) return false;
            s = new Vector3(r[0], r[1], r[2]);
            return true;
        }

        public static float Ease(string ease, float u)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            switch ((ease ?? "inOut").ToLowerInvariant())
            {
                case "linear": return u;
                case "in": return u * u * u;
                case "out": return 1f - Mathf.Pow(1f - u, 3f);
                case "snap": return u >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * u);
                case "back": { float x = u - 1f; return 1f + c3 * x * x * x + c1 * x * x; }
                case "anticipate": return c3 * u * u * u - c1 * u * u;
                case "step": return u < 1f ? 0f : 1f;
                default: return u < 0.5f ? 4f * u * u * u : 1f - Mathf.Pow(-2f * u + 2f, 3f) / 2f;
            }
        }

        static bool Segment(List<PlanKey> keys, Func<PlanKey, float[]> sel, int dim, float t,
            out float[] a, out float[] b, out float u)
        {
            a = b = null; u = 0f;
            if (keys == null) return false;
            PlanKey prev = null, next = null;
            foreach (var k in keys)
            {
                var v = sel(k);
                if (v == null || v.Length < dim) continue;
                if (k.t <= t) prev = k;
                else { next = k; break; }
            }
            if (prev == null && next == null) return false;
            if (prev == null) { a = b = sel(next); return true; }
            if (next == null) { a = b = sel(prev); return true; }
            a = sel(prev); b = sel(next);
            float span = next.t - prev.t;
            u = Ease(next.ease, span <= 1e-5f ? 1f : Mathf.Clamp01((t - prev.t) / span));
            return true;
        }

        public static bool TryVec(List<PlanKey> keys, Func<PlanKey, float[]> sel, int dim, float t, out float[] result)
        {
            result = null;
            if (!Segment(keys, sel, dim, t, out var a, out var b, out var u)) return false;
            result = new float[dim];
            for (int i = 0; i < dim; i++) result[i] = Mathf.LerpUnclamped(a[i], b[i], u);
            return true;
        }

        public static bool TryVec3(List<PlanKey> keys, Func<PlanKey, float[]> sel, float t, out Vector3 v)
        {
            v = Vector3.zero;
            if (!TryVec(keys, sel, 3, t, out var r)) return false;
            v = new Vector3(r[0], r[1], r[2]);
            return true;
        }

        public static bool TryRot(List<PlanKey> keys, float t, out Quaternion q)
        {
            q = Quaternion.identity;
            if (!Segment(keys, k => k.rot, 3, t, out var a, out var b, out var u)) return false;
            q = Quaternion.SlerpUnclamped(Quaternion.Euler(a[0], a[1], a[2]), Quaternion.Euler(b[0], b[1], b[2]), u);
            return true;
        }

        public static bool TrySpread(List<PlanKey> keys, float t, out float s)
        {
            s = 0f;
            if (!TryVec(keys, k => k.spread.HasValue ? new[] { k.spread.Value } : null, 1, t, out var r)) return false;
            s = r[0];
            return true;
        }

        public static bool Has(List<PlanKey> keys) => keys != null && keys.Count > 0;

        public static Vector3 Noise3(NoiseSettings n, float t, float amp, int channel)
        {
            if (n == null || amp <= 0f) return Vector3.zero;
            float x = n.seed * 13.37f + t * n.frequency;
            return new Vector3(
                Mathf.PerlinNoise(x, channel * 7.1f) * 2f - 1f,
                Mathf.PerlinNoise(x, channel * 7.1f + 3.3f) * 2f - 1f,
                Mathf.PerlinNoise(x, channel * 7.1f + 5.7f) * 2f - 1f) * amp;
        }
    }
}
