using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GoodCopBadCop.AnimGen
{
    /// <summary>POV arms: evaluates an <see cref="AnimPlan"/> in eye-centered view space and bakes Humanoid muscle curves.</summary>
    public static class ArmsClipBaker
    {
        public static Vector3 CamOffset(AnimPlan plan) =>
            plan?.camera?.offset != null && plan.camera.offset.Length >= 3
                ? new Vector3(plan.camera.offset[0], plan.camera.offset[1], plan.camera.offset[2])
                : HumanoidRig.DefaultCameraOffset;

        public static string Bake(AnimPlan plan, string outputClipPath, string rigPrefabPath)
        {
            outputClipPath = HumanoidClipWriter.ValidateOutputPath(outputClipPath);
            AnimationClip baseClip = HumanoidClipWriter.LoadClip(plan.basePose?.clip);
            float baseTime = plan.basePose?.time ?? 0f;
            bool follow = plan.basePose != null && plan.basePose.follow;
            Vector3 camOffset = CamOffset(plan);

            var writer = new HumanoidClipWriter(plan);
            var stats = new Dictionary<bool, EffectorStats> { { true, new EffectorStats() }, { false, new EffectorStats() } };
            var pose = new HumanPose();
            using (var rig = HumanoidRig.Create(rigPrefabPath, HumanoidRig.DefaultArmsPrefabPath, false))
            {
                for (int fr = 0; fr < writer.FrameCount; fr++)
                {
                    float t = writer.TimeOf(fr);
                    rig.ApplyBase(baseClip, follow ? baseTime + t : baseTime);
                    PoseFrame(rig, plan, t, camOffset, stats);
                    rig.PoseHandler.GetHumanPose(ref pose);
                    HumanoidClipWriter.ApplyFingers(plan, t, ref pose);
                    writer.Record(fr, pose);
                }
            }
            writer.Save(outputClipPath, false, false);

            var sb = new StringBuilder();
            sb.AppendLine($"Baked {outputClipPath}: {plan.duration:0.###}s @ {plan.fps}fps, {writer.FrameCount} frames, loop={plan.loop}, base={(baseClip ? baseClip.name : "bind pose")}{(follow ? " (follow)" : "")}");
            foreach (var right in new[] { true, false })
                sb.AppendLine($"{(right ? "Right" : "Left")} hand view-space path: {stats[right].Range}; reach-clamped frames: {stats[right].clamped}");
            if (stats[true].clamped + stats[false].clamped > 0)
                sb.AppendLine("WARNING: some hand targets were out of reach (arm fully extended or folded); pull targets closer to the shoulder.");
            return sb.ToString().TrimEnd();
        }

        static void PoseFrame(HumanoidRig rig, AnimPlan plan, float t, Vector3 camOffset, Dictionary<bool, EffectorStats> stats)
        {
            var tr = plan.tracks;
            bool offset = plan.IsOffsetSpace;

            rig.ViewFrame(camOffset, out var v0Pos, out var v0Rot);
            var inv0 = Quaternion.Inverse(v0Rot);
            var baseHand = new Dictionary<bool, (Vector3 pos, Quaternion rot, Vector3 elbow)>();
            foreach (var right in new[] { true, false })
            {
                var arm = rig.Arm(right);
                baseHand[right] = (inv0 * (arm.end.position - v0Pos), inv0 * arm.end.rotation, inv0 * (arm.lower.position - v0Pos));
            }

            if (PlanEval.TryRot(tr.spine, t, out var spineQ)) HumanoidRig.RotateInFrame(rig.ChestBone, v0Rot, spineQ);
            if (PlanEval.TryRot(tr.head, t, out var headQ)) HumanoidRig.RotateInFrame(rig.Head, v0Rot, headQ);

            rig.ViewFrame(camOffset, out var vPos, out var vRot);
            var invV = Quaternion.Inverse(vRot);
            float rotNoise = plan.noise?.rotation ?? 0f, posNoise = plan.noise?.position ?? 0f;

            foreach (var right in new[] { true, false })
            {
                var handTrack = right ? tr.rightHand : tr.leftHand;
                var elbowTrack = right ? tr.rightElbow : tr.leftElbow;
                bool hasPos = PlanEval.TryVec3(handTrack, k => k.pos, t, out var pos);
                bool hasRot = PlanEval.TryRot(handTrack, t, out var rotQ);
                bool hasHint = PlanEval.TryVec3(elbowTrack, k => k.hint, t, out var hint);
                var arm = rig.Arm(right);
                var b = baseHand[right];

                if (!(hasPos || hasRot || hasHint || posNoise > 0f || rotNoise > 0f))
                {
                    stats[right].Add(invV * (arm.end.position - vPos));
                    continue;
                }

                Vector3 localPos = offset ? b.pos + (hasPos ? pos : Vector3.zero) : (hasPos ? pos : b.pos);
                Quaternion localRot = offset ? (hasRot ? rotQ : Quaternion.identity) * b.rot : (hasRot ? rotQ : b.rot);
                Vector3 localHint = offset ? b.elbow + (hasHint ? hint : Vector3.zero) : (hasHint ? hint : b.elbow);

                int ch = right ? 0 : 1;
                localPos += PlanEval.Noise3(plan.noise, t, posNoise, ch);
                localRot = Quaternion.Euler(PlanEval.Noise3(plan.noise, t, rotNoise, ch + 2)) * localRot;

                if (HumanoidRig.SolveTwoBone(arm, vPos + vRot * localPos, vPos + vRot * localHint)) stats[right].clamped++;
                arm.end.rotation = vRot * localRot;
                stats[right].Add(invV * (arm.end.position - vPos));
            }
        }
    }
}
