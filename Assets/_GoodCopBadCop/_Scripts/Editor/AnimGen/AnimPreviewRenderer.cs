using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.AnimGen
{
    /// <summary>Renders a Humanoid clip on an isolated rig copy into a contact sheet PNG (rows = camera views, columns = time).</summary>
    public static class AnimPreviewRenderer
    {
        delegate void CameraForFrame(HumanoidRig rig, int view, out Vector3 pos, out Quaternion rot, out float fov);

        /// <summary>POV arms: row 1 eye camera, row 2 side view.</summary>
        public static string RenderArmsSheet(string clipPath, string rigPrefabPath, int frameCount, int tileSize,
            bool includeSideView, Vector3 camOffset, float fov)
        {
            return Render(clipPath, rigPrefabPath, HumanoidRig.DefaultArmsPrefabPath, false, frameCount, tileSize,
                includeSideView ? 2 : 1, null,
                (HumanoidRig rig, int view, out Vector3 pos, out Quaternion rot, out float f) =>
                {
                    rig.ViewFrame(camOffset, out var vPos, out var vRot);
                    if (view == 0) { pos = vPos; rot = vRot; f = fov; return; }
                    Vector3 focus = vPos + vRot * new Vector3(0f, -0.2f, 0.35f);
                    pos = focus + vRot * new Vector3(1.3f, 0.15f, 0.1f);
                    rot = Quaternion.LookRotation(focus - pos, Vector3.up);
                    f = 40f;
                });
        }

        public static readonly string[] BodyViewNames = { "front", "side", "threeQuarter", "back", "top" };

        /// <summary>Full body: fixed cameras framed on the union of bone bounds across all sampled frames.
        /// Generic clips are played on an avatar-less copy of the given variant (as Film Studio does).</summary>
        public static string RenderBodySheet(string clipPath, string rigPath, int frameCount, int tileSize, string[] views, string variant = null)
        {
            if (views == null || views.Length == 0) views = new[] { "front", "side" };
            var dirs = new List<Vector3>();
            foreach (var v in views)
            {
                switch (v.Trim().ToLowerInvariant())
                {
                    case "front": dirs.Add(new Vector3(0f, 0.1f, 1f)); break;
                    case "side": dirs.Add(new Vector3(1f, 0.1f, 0f)); break;
                    case "threequarter": dirs.Add(new Vector3(0.75f, 0.25f, 0.75f)); break;
                    case "back": dirs.Add(new Vector3(0f, 0.1f, -1f)); break;
                    case "top": dirs.Add(new Vector3(0f, 1f, 0.15f)); break;
                    default: throw new ArgumentException($"Unknown view '{v}'. Use: {string.Join(", ", BodyViewNames)}");
                }
            }
            if (dirs.Count > 4) throw new ArgumentException("At most 4 views.");

            Bounds union = default;
            const float fov = 30f;
            return Render(clipPath, rigPath, HumanoidRig.DefaultBodyModelPath, true, frameCount, tileSize, dirs.Count,
                (rig, bounds) => union = bounds,
                (HumanoidRig rig, int view, out Vector3 pos, out Quaternion rot, out float f) =>
                {
                    Vector3 dir = dirs[view].normalized;
                    float radius = Mathf.Max(union.extents.magnitude, 0.5f) + 0.2f;
                    float dist = radius / Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad);
                    pos = union.center + dir * dist;
                    rot = Quaternion.LookRotation(union.center - pos, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up);
                    f = fov;
                }, variant);
        }

        static string Render(string clipPath, string rigPath, string fallbackRig, bool requireLegs, int frameCount, int tileSize,
            int rows, Action<HumanoidRig, Bounds> onBounds, CameraForFrame cameraFor, string variant = null)
        {
            var clip = HumanoidClipWriter.LoadClip(clipPath) ?? throw new ArgumentException("clipPath is required.");
            frameCount = Mathf.Clamp(frameCount <= 0 ? 6 : frameCount, 1, 12);
            tileSize = Mathf.Clamp(tileSize <= 0 ? 256 : tileSize, 128, 512);

            var sheet = new Texture2D(tileSize * frameCount, tileSize * rows, TextureFormat.RGB24, false);
            var rt = RenderTexture.GetTemporary(tileSize, tileSize, 24, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            var pru = new PreviewRenderUtility();
            HumanoidRig rig = null;
            try
            {
                rig = HumanoidRig.Create(rigPath, fallbackRig, requireLegs, variant);
                if (!clip.humanMotion)
                {
                    rig.Fold(GenericClipBaker.FoldFor(variant));
                    rig.Animator.avatar = null;
                }
                if (!string.IsNullOrEmpty(variant))
                    foreach (var r in rig.Root.GetComponentsInChildren<Renderer>(true))
                        if (!r.transform.IsChildOf(rig.CharacterRoot)) r.enabled = false;
                pru.AddSingleGO(rig.Root);

                var times = new float[frameCount];
                for (int i = 0; i < frameCount; i++)
                    times[i] = frameCount == 1 ? 0f : Mathf.Min(clip.length * i / (frameCount - 1), clip.length - 1e-4f);

                if (onBounds != null)
                {
                    Bounds b = default;
                    for (int i = 0; i < frameCount; i++)
                    {
                        rig.ApplyBase(clip, times[i]);
                        var fb = rig.BoneBounds();
                        if (i == 0) b = fb; else b.Encapsulate(fb);
                    }
                    b.Expand(new Vector3(0.3f, 0.3f, 0.3f)); // bones -> skin + head top
                    onBounds(rig, b);
                }

                var cam = pru.camera;
                cam.nearClipPlane = 0.02f;
                cam.farClipPlane = 100f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.22f, 0.24f, 0.27f, 1f);
                pru.ambientColor = new Color(0.4f, 0.4f, 0.45f, 1f);
                pru.lights[0].intensity = 1.2f;
                pru.lights[0].transform.rotation = Quaternion.Euler(35f, 155f, 0f);
                pru.lights[1].intensity = 0.6f;
                pru.lights[1].transform.rotation = Quaternion.Euler(340f, 20f, 0f);

                // Skinned meshes don't re-skin in an edit-mode preview scene: bake them per frame and draw the result instead.
                // Custom/third-party shaders (e.g. Toony Colors Pro) often don't draw in preview contexts: swap them for a URP Lit stand-in.
                var skinned = new List<(SkinnedMeshRenderer smr, Mesh baked, Material[] mats)>();
                var standIns = new Dictionary<Material, Material>();
                foreach (var smr in rig.Root.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    if (!smr.enabled || smr.sharedMesh == null) continue;
                    smr.forceRenderingOff = true;
                    var mats = smr.sharedMaterials;
                    for (int m = 0; m < mats.Length; m++) mats[m] = PreviewMaterial(mats[m], standIns);
                    skinned.Add((smr, new Mesh { hideFlags = HideFlags.HideAndDontSave }, mats));
                }

                try
                {
                    var scaleFix = new Dictionary<SkinnedMeshRenderer, float>();
                    for (int i = 0; i < frameCount; i++)
                    {
                        rig.ApplyBase(clip, times[i]);
                        foreach (var s in skinned) s.smr.BakeMesh(s.baked, true);
                        if (i == 0)
                            foreach (var s in skinned) scaleFix[s.smr] = BakeScaleFix(s.smr, s.baked);
                        for (int view = 0; view < rows; view++)
                        {
                            cameraFor(rig, view, out var pos, out var rot, out var fov);
                            RenderTile(pru, sheet, rt, tileSize, i, rows - 1 - view, pos, rot, fov, skinned, scaleFix);
                        }
                    }
                }
                finally
                {
                    foreach (var s in skinned) UnityEngine.Object.DestroyImmediate(s.baked);
                    foreach (var m in standIns.Values) if (m != null) UnityEngine.Object.DestroyImmediate(m);
                }
                sheet.Apply();

                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "AnimGenPreviews"));
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"{clip.name}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                File.WriteAllBytes(file, sheet.EncodeToPNG());
                return file.Replace('\\', '/');
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(sheet);
                pru.Cleanup();
                rig?.Dispose();
            }
        }

        static bool IsPreviewSafe(Shader s) =>
            s != null && (s.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal) ||
                          s.name.StartsWith("Shader Graphs/", StringComparison.Ordinal));

        /// <summary>Returns the material itself if its shader draws in previews, else a cached URP Lit stand-in keeping texture and color.</summary>
        static Material PreviewMaterial(Material src, Dictionary<Material, Material> cache)
        {
            if (src == null || IsPreviewSafe(src.shader)) return src;
            if (cache.TryGetValue(src, out var standIn)) return standIn;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return src;
            standIn = new Material(lit) { hideFlags = HideFlags.HideAndDontSave, name = src.name + " (AnimGen preview)" };
            Texture tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            if (tex != null) standIn.SetTexture("_BaseMap", tex);
            Color col = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
            col.a = 1f;
            standIn.SetColor("_BaseColor", col);
            standIn.SetFloat("_Cull", 0f);
            standIn.SetFloat("_Smoothness", 0.2f);
            cache[src] = standIn;
            return standIn;
        }

        /// <summary>
        /// Meshes on heavily scaled transforms (e.g. Blender exports at x100) can bake without that scale. Compares the baked
        /// size with the bind mesh size times the transform scale and returns the power-of-ten correction (1 when consistent).
        /// </summary>
        static float BakeScaleFix(SkinnedMeshRenderer smr, Mesh baked)
        {
            Vector3 ls = smr.transform.lossyScale;
            float expected = smr.sharedMesh.bounds.size.magnitude * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            float got = baked.bounds.size.magnitude;
            if (expected <= 1e-6f || got <= 1e-6f) return 1f;
            float exp10 = Mathf.Round(Mathf.Log10(expected / got));
            return Mathf.Abs(exp10) >= 1f ? Mathf.Pow(10f, exp10) : 1f;
        }

        static void RenderTile(PreviewRenderUtility pru, Texture2D sheet, RenderTexture rt, int tile, int col, int row,
            Vector3 pos, Quaternion rot, float fov, List<(SkinnedMeshRenderer smr, Mesh baked, Material[] mats)> skinned,
            Dictionary<SkinnedMeshRenderer, float> scaleFix)
        {
            pru.BeginStaticPreview(new Rect(0, 0, tile, tile));
            pru.cameraFieldOfView = fov;
            pru.camera.fieldOfView = fov;
            pru.camera.transform.SetPositionAndRotation(pos, rot);
            foreach (var (smr, baked, mats) in skinned)
            {
                float k = scaleFix != null && scaleFix.TryGetValue(smr, out float f) ? f : 1f;
                var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one * k);
                for (int s = 0; s < baked.subMeshCount && s < mats.Length; s++)
                    if (mats[s] != null) pru.DrawMesh(baked, m, mats[s], s);
            }
            pru.Render(true, false);
            var tex = pru.EndStaticPreview();
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            sheet.ReadPixels(new Rect(0, 0, tile, tile), col * tile, row * tile);
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
