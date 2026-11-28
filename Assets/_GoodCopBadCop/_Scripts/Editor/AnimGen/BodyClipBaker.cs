using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GoodCopBadCop.AnimGen
{
    /// <summary>
    /// Full-body humanoid: evaluates an <see cref="AnimPlan"/> in character space (origin at the character root on the ground,
    /// +X right, +Y up, +Z forward) and bakes Humanoid muscle curves, with optional root motion.
    /// Per frame: base pose -> hips -> spine/chest/neck/head/shoulders -> leg IK (planted or tracked feet) -> arm IK -> root motion.
    /// </summary>
    public static class BodyClipBaker
    {
        // Default pole offsets (character space) added to the base elbow/knee so straight limbs still bend the natural way.
        static readonly Vector3 KneePole = new Vector3(0f, 0f, 0.5f);
        static readonly Vector3 ElbowPole = new Vector3(0f, -0.1f, -0.3f);

        struct BaseLimb
        {
            public Vector3 charPos, chestPos, charMid, chestMid;
            public Quaternion charRot, chestRot;
        }

        public static string Bake(AnimPlan plan, string outputClipPath, string rigPath)
        {
            outputClipPath = HumanoidClipWriter.ValidateOutputPath(outputClipPath);
            AnimationClip baseClip = HumanoidClipWriter.LoadClip(plan.basePose?.clip);
            float baseTime = plan.basePose?.time ?? 0f;
            bool follow = plan.basePose != null && plan.basePose.follow;
            bool rootXZ = PlanEval.Has(plan.tracks.root) && plan.tracks.root.Exists(k => k.pos != null);
            bool rootYaw = PlanEval.Has(plan.tracks.root) && plan.tracks.root.Exists(k => k.rot != null);

            var writer = new HumanoidClipWriter(plan);
            var stats = new Dictionary<string, EffectorStats>
            {
                { "Right hand", new EffectorStats() }, { "Left hand", new EffectorStats() },
                { "Right foot", new EffectorStats() }, { "Left foot", new EffectorStats() },
            };
            var pose = new HumanPose();
            string rigName;
            using (var rig = HumanoidRig.Create(rigPath, HumanoidRig.DefaultBodyModelPath, true))
            {
                rigName = rig.Root.name.Replace(" (AnimGen)", "");
                for (int fr = 0; fr < writer.FrameCount; fr++)
                {
                    float t = writer.TimeOf(fr);
                    rig.ApplyBase(baseClip, follow ? baseTime + t : baseTime);
                    PoseFrame(rig, plan, t, stats);
                    rig.PoseHandler.GetHumanPose(ref pose);
                    HumanoidClipWriter.ApplyFingers(plan, t, ref pose);
                    writer.Record(fr, pose);
                }
            }
            writer.Save(outputClipPath, rootXZ, rootYaw);

            var sb = new StringBuilder();
            sb.AppendLine($"Baked {outputClipPath} on {rigName}: {plan.duration:0.###}s @ {plan.fps}fps, {writer.FrameCount} frames, loop={plan.loop}, base={(baseClip ? baseClip.name : "bind pose")}{(follow ? " (follow)" : "")}, plantFeet={plan.plantFeet}, handSpace={plan.handSpace}, rootMotion XZ={rootXZ} yaw={rootYaw}");
            int clampedTotal = 0;
            foreach (var kv in stats)
            {
                sb.AppendLine($"{kv.Key} character-space path: {kv.Value.Range}; reach-clamped frames: {kv.Value.clamped}");
                clampedTotal += kv.Value.clamped;
            }
            if (clampedTotal > 0)
                sb.AppendLine("WARNING: some targets were out of reach (limb fully extended or folded). Lower the hips less, or pull hand/foot targets closer.");
            return sb.ToString().TrimEnd();
        }

        static BaseLimb Capture(LimbChain limb, Transform chest, Vector3 o, Quaternion invC)
        {
            return new BaseLimb
            {
                charPos = invC * (limb.end.position - o),
                charRot = invC * limb.end.rotation,
                charMid = invC * (limb.lower.position - o),
                chestPos = chest.InverseTransformPoint(limb.end.position),
                chestRot = Quaternion.Inverse(chest.rotation) * limb.end.rotation,
                chestMid = chest.InverseTransformPoint(limb.lower.position),
            };
        }

        internal static void PoseFrame(HumanoidRig rig, AnimPlan plan, float t, Dictionary<string, EffectorStats> stats)
        {
            var tr = plan.tracks;
            bool offset = plan.IsOffsetSpace;
            Vector3 o = rig.CharacterRoot.position;
            Quaternion c = rig.CharacterRoot.rotation, invC = Quaternion.Inverse(c);
            Transform chest = rig.ChestBone;

            var baseArm = new Dictionary<bool, BaseLimb>();
            var baseLeg = new Dictionary<bool, BaseLimb>();
            foreach (var right in new[] { true, false })
            {
                baseArm[right] = Capture(rig.Arm(right), chest, o, invC);
                baseLeg[right] = Capture(rig.Leg(right), chest, o, invC);
            }

            // Hips (always deltas) + breathing/idle noise.
            Vector3 hipsDelta = PlanEval.TryVec3(tr.hips, k => k.pos, t, out var hp) ? hp : Vector3.zero;
            hipsDelta += PlanEval.Noise3(plan.noise, t, plan.noise?.position ?? 0f, 4);
            rig.Hips.position += c * hipsDelta;
            if (PlanEval.TryRot(tr.hips, t, out var hq)) HumanoidRig.RotateInFrame(rig.Hips, c, hq);

            // Spine chain (always deltas, character axes).
            if (PlanEval.TryRot(tr.spine, t, out var sq)) HumanoidRig.RotateInFrame(rig.Spine, c, sq);
            if (PlanEval.TryRot(tr.chest, t, out var cq) && chest != rig.Spine) HumanoidRig.RotateInFrame(chest, c, cq);
            float rotNoise = plan.noise?.rotation ?? 0f;
            if (rotNoise > 0f && chest != null) HumanoidRig.RotateInFrame(chest, c, Quaternion.Euler(PlanEval.Noise3(plan.noise, t, rotNoise, 5)));
            if (PlanEval.TryRot(tr.neck, t, out var nq)) HumanoidRig.RotateInFrame(rig.Neck, c, nq);
            if (PlanEval.TryRot(tr.head, t, out var headQ)) HumanoidRig.RotateInFrame(rig.Head, c, headQ);
            if (PlanEval.TryRot(tr.rightShoulder, t, out var rsq)) HumanoidRig.RotateInFrame(rig.RightShoulder, c, rsq);
            if (PlanEval.TryRot(tr.leftShoulder, t, out var lsq)) HumanoidRig.RotateInFrame(rig.LeftShoulder, c, lsq);

            // Legs: tracked feet, or planted at the base pose.
            foreach (var right in new[] { true, false })
            {
                var footTrack = right ? tr.rightFoot : tr.leftFoot;
                var kneeTrack = right ? tr.rightKnee : tr.leftKnee;
                bool hasPos = PlanEval.TryVec3(footTrack, k => k.pos, t, out var pos);
                bool hasRot = PlanEval.TryRot(footTrack, t, out var rq);
                bool hasHint = PlanEval.TryVec3(kneeTrack, k => k.hint, t, out var hint);
                var leg = rig.Leg(right);
                var b = baseLeg[right];
                var st = stats[right ? "Right foot" : "Left foot"];

                if (hasPos || hasRot || hasHint || plan.plantFeet)
                {
                    Vector3 p = offset || !hasPos ? b.charPos + (hasPos && offset ? pos : Vector3.zero) : pos;
                    Quaternion r = offset || !hasRot ? (hasRot && offset ? rq : Quaternion.identity) * b.charRot : rq;
                    Vector3 h = hasHint && !offset ? hint : b.charMid + KneePole + (hasHint ? hint : Vector3.zero);
                    if (HumanoidRig.SolveTwoBone(leg, o + c * p, o + c * h)) st.clamped++;
                    leg.end.rotation = c * r;
                }
                st.Add(invC * (leg.end.position - o));
            }

            // Arms: targets ride with the chest by default.
            foreach (var right in new[] { true, false })
            {
                var handTrack = right ? tr.rightHand : tr.leftHand;
                var elbowTrack = right ? tr.rightElbow : tr.leftElbow;
                bool hasPos = PlanEval.TryVec3(handTrack, k => k.pos, t, out var pos);
                bool hasRot = PlanEval.TryRot(handTrack, t, out var rq);
                bool hasHint = PlanEval.TryVec3(elbowTrack, k => k.hint, t, out var hint);
                var arm = rig.Arm(right);
                var b = baseArm[right];
                var st = stats[right ? "Right hand" : "Left hand"];

                if (hasPos || hasRot || hasHint)
                {
                    bool ride = plan.HandsFollowChest;
                    Vector3 basePos = ride ? chest.TransformPoint(b.chestPos) : o + c * b.charPos;
                    Quaternion baseRot = ride ? chest.rotation * b.chestRot : c * b.charRot;
                    Vector3 baseMid = ride ? chest.TransformPoint(b.chestMid) : o + c * b.charMid;

                    Vector3 target = offset || !hasPos ? basePos + (hasPos && offset ? c * pos : Vector3.zero) : o + c * pos;
                    Quaternion rot = offset || !hasRot ? (hasRot && offset ? c * rq * invC : Quaternion.identity) * baseRot : c * rq;
                    Vector3 pole = hasHint && !offset ? o + c * hint : baseMid + c * (ElbowPole + (hasHint ? hint : Vector3.zero));
                    if (HumanoidRig.SolveTwoBone(arm, target, pole)) st.clamped++;
                    arm.end.rotation = rot;
                }
                st.Add(invC * (arm.end.position - o));
            }

            // Root motion: move/turn the whole skeleton about the character origin.
            bool hasRootPos = PlanEval.TryVec3(tr.root, k => k.pos, t, out var rootPos);
            bool hasRootRot = PlanEval.TryRot(tr.root, t, out var rootRot);
            if (hasRootPos || hasRootRot)
            {
                Vector3 local = invC * (rig.Hips.position - o);
                rig.Hips.position = o + c * (rootRot * local + rootPos);
                rig.Hips.rotation = c * rootRot * invC * rig.Hips.rotation;
            }
        }
    }
}
