using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP renderer feature (lives on XRay_Renderer only). For a camera with an <see cref="XRayCamera"/>,
/// draws what that camera collected, regardless of the renderers' layers or original materials,
/// using the current scan mode's materials, in this order:
///   1. Organs   — usually opaque + depth-writing, tinted per organ via the global _XRayPartColor.
///   2. Bodies   — additive glow (falls back to <see cref="_bodyMaterial"/>).
///   3. Skeleton — additive bone glow, on top.
/// A null material in the mode skips that layer.
/// </summary>
public class XRayBodyFeature : ScriptableRendererFeature
{
    [Tooltip("Default material for suspect bodies (GoodCopBadCop/XRay Body) when the mode has none.")]
    [SerializeField] private Material _bodyMaterial;

    [SerializeField] private RenderPassEvent _renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

    private XRayBodyPass _pass;

    public override void Create()
    {
        _pass = new XRayBodyPass { renderPassEvent = _renderPassEvent };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        _pass.DefaultBodyMaterial = _bodyMaterial;
        renderer.EnqueuePass(_pass);
    }

    private class XRayBodyPass : ScriptableRenderPass
    {
        private static readonly int PartColorId = Shader.PropertyToID("_XRayPartColor");

        public Material DefaultBodyMaterial;

        private class PassData
        {
            public IReadOnlyList<XRayCamera.Target> Bodies;
            public IReadOnlyList<XRayCamera.Target> Skeleton;
            public IReadOnlyList<XRayCamera.Target> Organs;
            public Material BodyMaterial;
            public Material SkeletonMaterial;
            public Material OrganMaterial;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.camera == null || !cameraData.camera.TryGetComponent(out XRayCamera xRay)) return;

            Material bodyMat = xRay.CurrentBodyMaterial != null ? xRay.CurrentBodyMaterial : DefaultBodyMaterial;
            Material skeletonMat = xRay.CurrentSkeletonMaterial;
            Material organMat = xRay.CurrentOrganMaterial;

            bool anyBodies = bodyMat != null && xRay.BodyTargets.Count > 0;
            bool anySkeleton = skeletonMat != null && xRay.SkeletonTargets.Count > 0;
            bool anyOrgans = organMat != null && xRay.OrganTargets.Count > 0;
            if (!anyBodies && !anySkeleton && !anyOrgans) return;

            var resources = frameData.Get<UniversalResourceData>();

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("XRay Body", out var data))
            {
                data.Bodies = xRay.BodyTargets;
                data.Skeleton = xRay.SkeletonTargets;
                data.Organs = xRay.OrganTargets;
                data.BodyMaterial = anyBodies ? bodyMat : null;
                data.SkeletonMaterial = anySkeleton ? skeletonMat : null;
                data.OrganMaterial = anyOrgans ? organMat : null;

                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true); // per-organ _XRayPartColor
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) => Execute(d, ctx));
            }
        }

        private static void Execute(PassData data, RasterGraphContext ctx)
        {
            if (data.OrganMaterial != null) Draw(data.Organs, data.OrganMaterial, ctx, true);
            if (data.BodyMaterial != null) Draw(data.Bodies, data.BodyMaterial, ctx, false);
            if (data.SkeletonMaterial != null) Draw(data.Skeleton, data.SkeletonMaterial, ctx, false);
        }

        private static void Draw(IReadOnlyList<XRayCamera.Target> targets, Material material, RasterGraphContext ctx, bool setColor)
        {
            for (int t = 0; t < targets.Count; t++)
            {
                var target = targets[t];
                var r = target.Renderer;
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;

                if (setColor) ctx.cmd.SetGlobalColor(PartColorId, target.Color);
                for (int s = 0; s < target.SubMeshCount; s++)
                    ctx.cmd.DrawRenderer(r, material, s, 0);
            }
        }
    }
}
