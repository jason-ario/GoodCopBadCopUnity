using System;
using System.Linq;
using Bezi;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.AnimGen
{
    /// <summary>Bezi-callable entry points for AI-authored Humanoid animations (POV arms and full body).</summary>
    public static class AnimGenBeziActions
    {
        static float[] V(Vector3 v) => new[] { R(v.x), R(v.y), R(v.z) };
        static float R(float f) => Mathf.Round(f * 1000f) / 1000f;

        // ---------------- POV arms ----------------

        [BeziAction("AnimGen: profile a Humanoid arms rig for POV animation planning. Samples the optional base clip at baseTime and returns JSON with view-space (eye origin, +X right, +Y up, +Z forward, meters/degrees) shoulder/elbow/hand positions, hand euler rotations, arm segment lengths, and max reach per side. Empty rigPrefabPath uses 'Player Arms For Animations.prefab'.", IsReadOnly = true)]
        public static string AnimGenProfileArmsRig(string rigPrefabPath, string baseClipPath, float baseTime)
        {
            var clip = HumanoidClipWriter.LoadClip(baseClipPath);
            using (var rig = HumanoidRig.Create(rigPrefabPath, HumanoidRig.DefaultArmsPrefabPath, false))
            {
                rig.ApplyBase(clip, baseTime);
                rig.ViewFrame(HumanoidRig.DefaultCameraOffset, out var vPos, out var vRot);
                var inv = Quaternion.Inverse(vRot);
                object Side(bool right)
                {
                    var a = rig.Arm(right);
                    return new
                    {
                        shoulder = V(inv * (a.upper.position - vPos)),
                        elbow = V(inv * (a.lower.position - vPos)),
                        hand = V(inv * (a.end.position - vPos)),
                        handEuler = V((inv * a.end.rotation).eulerAngles),
                        upperLength = R(a.UpperLength),
                        lowerLength = R(a.LowerLength),
                        maxReach = R(a.Reach),
                    };
                }
                return JsonConvert.SerializeObject(new
                {
                    rig = rig.Root.name.Replace(" (AnimGen)", ""),
                    avatar = rig.Animator.avatar.name,
                    baseClip = clip != null ? clip.name : "bind pose",
                    baseClipLength = clip != null ? R(clip.length) : 0f,
                    cameraOffsetFromHead = V(HumanoidRig.DefaultCameraOffset),
                    hasUpperChest = rig.UpperChest != null,
                    right = Side(true),
                    left = Side(false),
                }, Formatting.Indented);
            }
        }

        [BeziAction("AnimGen: bake a POV arms Humanoid AnimationClip (.anim) from an AnimPlan JSON (view space; rightHand/leftHand pos+rot, rightElbow/leftElbow hint, rightFingers/leftFingers curl[5]+spread, spine/head rot, per-key easing). Overwrites outputClipPath in place (keeps GUID). Returns a bake report with hand travel ranges and out-of-reach warnings. Empty rigPrefabPath uses 'Player Arms For Animations.prefab'.")]
        public static string AnimGenBakeArmsClip(string planJson, string outputClipPath, string rigPrefabPath)
        {
            return ArmsClipBaker.Bake(HumanoidClipWriter.Parse(planJson), outputClipPath, rigPrefabPath);
        }

        [BeziAction("AnimGen: render a contact-sheet PNG of a Humanoid clip on the POV arms rig (top row: eye camera, bottom row: side view; columns evenly spaced in time). Returns the absolute PNG path for logAttachmentFile. frameCount 1-12 (default 6), tileSize 128-512 (default 256), fov <= 0 uses 60.", IsReadOnly = true)]
        public static string AnimGenRenderArmsPreview(string clipPath, string rigPrefabPath, int frameCount, int tileSize, bool includeSideView, float fov)
        {
            return AnimPreviewRenderer.RenderArmsSheet(clipPath, rigPrefabPath, frameCount, tileSize, includeSideView,
                HumanoidRig.DefaultCameraOffset, fov <= 0f ? 60f : fov);
        }

        // ---------------- Full body ----------------

        [BeziAction("AnimGen: profile a full-body Humanoid character (prefab or model) for animation planning. Samples the optional base clip at baseTime and returns JSON in character space (origin at the character root on the ground, +X right, +Y up, +Z forward, meters/degrees): hips/chest/head positions, per-side shoulder/elbow/hand and hip/knee/foot positions, hand/foot euler rotations, limb lengths and reach, and character height. Empty rigPath uses the PSX cop model.", IsReadOnly = true)]
        public static string AnimGenProfileBodyRig(string rigPath, string baseClipPath, float baseTime)
        {
            var clip = HumanoidClipWriter.LoadClip(baseClipPath);
            using (var rig = HumanoidRig.Create(rigPath, HumanoidRig.DefaultBodyModelPath, true))
            {
                rig.ApplyBase(clip, baseTime);
                Vector3 o = rig.CharacterRoot.position;
                var inv = Quaternion.Inverse(rig.CharacterRoot.rotation);
                float[] P(Transform t) => t != null ? V(inv * (t.position - o)) : null;
                object Limb(LimbChain l, string root, string mid, string end) => new System.Collections.Generic.Dictionary<string, object>
                {
                    { root, P(l.upper) }, { mid, P(l.lower) }, { end, P(l.end) },
                    { end + "Euler", V((inv * l.end.rotation).eulerAngles) },
                    { "upperLength", R(l.UpperLength) }, { "lowerLength", R(l.LowerLength) }, { "maxReach", R(l.Reach) },
                };
                var bounds = rig.BoneBounds();
                return JsonConvert.SerializeObject(new
                {
                    rig = rig.Root.name.Replace(" (AnimGen)", ""),
                    avatar = rig.Animator.avatar.name,
                    baseClip = clip != null ? clip.name : "bind pose",
                    baseClipLength = clip != null ? R(clip.length) : 0f,
                    approxHeight = R(bounds.max.y - o.y + 0.1f),
                    hips = P(rig.Hips),
                    chest = P(rig.ChestBone),
                    neck = P(rig.Neck),
                    head = P(rig.Head),
                    hasUpperChest = rig.UpperChest != null,
                    rightArm = Limb(rig.RightArm, "shoulder", "elbow", "hand"),
                    leftArm = Limb(rig.LeftArm, "shoulder", "elbow", "hand"),
                    rightLeg = Limb(rig.RightLeg, "hip", "knee", "foot"),
                    leftLeg = Limb(rig.LeftLeg, "hip", "knee", "foot"),
                }, Formatting.Indented);
            }
        }

        [BeziAction("AnimGen: bake a full-body Humanoid AnimationClip (.anim) from an AnimPlan JSON in character space. Tracks: hips pos+rot, spine/chest/neck/head/rightShoulder/leftShoulder rot, rightHand/leftHand pos+rot (+rightElbow/leftElbow hint), rightFoot/leftFoot pos+rot (+rightKnee/leftKnee hint), rightFingers/leftFingers curl[5]+spread, root pos+rot (root motion). Options: plantFeet (default true), handSpace 'chest'|'character'. Overwrites outputClipPath in place (keeps GUID). Returns a bake report with effector ranges and reach warnings. Empty rigPath uses the PSX cop model.")]
        public static string AnimGenBakeBodyClip(string planJson, string outputClipPath, string rigPath)
        {
            return BodyClipBaker.Bake(HumanoidClipWriter.Parse(planJson), outputClipPath, rigPath);
        }

        [BeziAction("AnimGen: render a contact-sheet PNG of a Humanoid clip on a full-body character. views is a comma-separated list of up to 4 of: front, side, threeQuarter, back, top (default 'front,side'); one row per view, columns evenly spaced in time, cameras fixed and framed on the whole motion. Returns the absolute PNG path for logAttachmentFile. frameCount 1-12 (default 6), tileSize 128-512 (default 256). Empty rigPath uses the PSX cop model.", IsReadOnly = true)]
        public static string AnimGenRenderBodyPreview(string clipPath, string rigPath, int frameCount, int tileSize, string views)
        {
            var list = string.IsNullOrWhiteSpace(views) ? null : views.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            return AnimPreviewRenderer.RenderBodySheet(clipPath, rigPath, frameCount, tileSize, list);
        }

        // ---------------- Generic (per-rig, avatar-less playback) ----------------

        [BeziAction("AnimGen: bake a GENERIC (Transform + blendshape) AnimationClip from a full-body AnimPlan for one rig variant ('base' or 'mutated' = the Humanoid Animator under 'Mutated Version'). Same body tracks/IK as AnimGenBakeBodyClip, plus Generic-only plan.bones (any bone or '*' glob, chain curl, wave, pos, scale) and plan.blendShapes (value 0-100). Every Transform under the variant's Animator is recorded, so the clip plays on an Animator WITHOUT an avatar (Film Studio Generic mode). Overwrites outputClipPath in place.")]
        public static string AnimGenBakeGenericClip(string planJson, string outputClipPath, string rigPath, string variant)
        {
            return GenericClipBaker.Bake(HumanoidClipWriter.Parse(planJson), outputClipPath, rigPath, string.IsNullOrWhiteSpace(variant) ? "base" : variant.Trim());
        }

        [BeziAction("AnimGen: describe one rig variant for Generic planning: character-space hips/chest/head/hands in the base pose, arm/leg reach, scale (hips height / 1 m), extra (non-humanoid) skinned bone chains usable in plan.bones (name, link count, attached humanoid bone, active), blendshapes and jaw/tail/eye-like bones. baseClipPath optional (e.g. an idle).", IsReadOnly = true)]
        public static string AnimGenDescribeGenericRig(string rigPath, string variant, string baseClipPath)
        {
            return GenericClipBaker.DescribeRig(rigPath, string.IsNullOrWhiteSpace(variant) ? "base" : variant.Trim(), HumanoidClipWriter.LoadClip(baseClipPath));
        }

        [BeziAction("AnimGen: render a contact-sheet PNG of a GENERIC clip on an avatar-less copy of one rig variant (only that variant's renderers drawn). views: up to 4 of front, side, threeQuarter, back, top. Returns the absolute PNG path for logAttachmentFile.", IsReadOnly = true)]
        public static string AnimGenRenderGenericPreview(string clipPath, string rigPath, string variant, int frameCount, int tileSize, string views)
        {
            var list = string.IsNullOrWhiteSpace(views) ? null : views.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            return AnimPreviewRenderer.RenderBodySheet(clipPath, rigPath, frameCount, tileSize, list, string.IsNullOrWhiteSpace(variant) ? "base" : variant.Trim());
        }

        // ---------------- Shared ----------------

        [BeziAction("AnimGen: diagnose a rig's renderers as the preview sees them (script-free copy). Lists each active Renderer with enabled/forceRenderingOff/layer/shadow mode, world bounds, mesh vertex count and (for skinned) baked vertex count, plus the humanoid Animator path and bone bounds. Use when a preview renders empty.", IsReadOnly = true)]
        public static string AnimGenDiagnoseRig(string rigPath, bool fullBody)
        {
            using (var rig = HumanoidRig.Create(rigPath, fullBody ? HumanoidRig.DefaultBodyModelPath : HumanoidRig.DefaultArmsPrefabPath, fullBody))
            {
                var sb = new System.Text.StringBuilder();
                string PathOf(Transform t) => t == null ? "" : (t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name);
                var bb = rig.BoneBounds();
                sb.AppendLine($"Animator: {PathOf(rig.Animator.transform)} lossyScale={rig.Animator.transform.lossyScale} boneBounds center={bb.center} size={bb.size}");
                var baked = new Mesh();
                foreach (var r in rig.Root.GetComponentsInChildren<Renderer>(false))
                {
                    string extra = "";
                    if (r is SkinnedMeshRenderer smr)
                    {
                        smr.BakeMesh(baked, true);
                        extra = $" mesh={(smr.sharedMesh ? smr.sharedMesh.name : "null")} verts={(smr.sharedMesh ? smr.sharedMesh.vertexCount : 0)} baked={baked.vertexCount} rootBone={(smr.rootBone ? smr.rootBone.name : "null")}";
                    }
                    else if (r.TryGetComponent<MeshFilter>(out var mf)) extra = $" mesh={(mf.sharedMesh ? mf.sharedMesh.name : "null")}";
                    sb.AppendLine($"{r.GetType().Name} {r.name} enabled={r.enabled} forceOff={r.forceRenderingOff} layer={LayerMask.LayerToName(r.gameObject.layer)} shadows={r.shadowCastingMode} bounds={r.bounds.center}/{r.bounds.size} mats={r.sharedMaterials.Length}{extra}");
                }
                UnityEngine.Object.DestroyImmediate(baked);
                return sb.ToString().TrimEnd();
            }
        }

        [BeziAction("AnimGen: list Humanoid AnimationClips under a folder (default Assets/_GoodCopBadCop) whose name contains the filter (e.g. 'Arms', 'Idle'), with length, loop flag and frame rate. Includes clips inside FBX models. Useful for choosing a base pose.", IsReadOnly = true)]
        public static string AnimGenListClips(string folder, string nameFilter)
        {
            folder = HumanoidRig.ToAssetPath(folder) ?? "Assets/_GoodCopBadCop";
            var lines = AssetDatabase.FindAssets("t:AnimationClip", new[] { folder })
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Distinct()
                .SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                    .Select(c => (p, c)))
                .Where(x => x.c.humanMotion && (string.IsNullOrEmpty(nameFilter) || x.c.name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0 || x.p.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(x => $"{x.p}{(x.p.EndsWith(".anim") ? "" : " :: " + x.c.name)} | {x.c.length:0.##}s | loop={x.c.isLooping} | {x.c.frameRate}fps")
                .Take(100);
            return string.Join("\n", lines);
        }
    }
}
