using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP Renderer Feature that applies a full-screen smudge / dreamy-smear effect.
/// Add this to your URP Renderer asset, then assign the smudge material.
/// </summary>
public class SmudgeFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;
        public Material material = null;

        [Tooltip("Direction of the smudge offset in UV space (e.g. X=1 smears horizontally).")]
        public Vector2 direction = new Vector2(1f, 0f);

        [Tooltip("How far the ghost copies are shifted in UV space.")]
        [Range(0f, 0.05f)] public float offset = 0.01f;

        [Tooltip("How strongly the ghost copies blend into the final image.")]
        [Range(0f, 1f)] public float blend = 0.5f;
    }

    // -------------------------------------------------------------------------
    private class SmudgePass : ScriptableRenderPass
    {
        private static readonly int SmudgeOffsetID = Shader.PropertyToID("_SmudgeOffset");
        private static readonly int SmudgeBlendID  = Shader.PropertyToID("_SmudgeBlend");
        private static readonly Vector4 ScaleBias  = new Vector4(1f, 1f, 0f, 0f);

        private readonly Settings settings;

        // Persistent RTHandles — allocated once and resized as needed each frame.
        private RTHandle tempRead;
        private RTHandle tempWrite;

        private class PassData
        {
            public TextureHandle source;
            public TextureHandle destination;
            public Material material;
        }

        public SmudgePass(Settings settings)
        {
            this.settings = settings;

            // Ensures URP never resolves activeColorTexture to the raw backbuffer
            // before this pass runs, which is what triggers the RTHandle assertion
            // in the legacy Execute path.
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings.material == null) return;

            var cameraData   = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            var desc = cameraData.cameraTargetDescriptor;
            desc.msaaSamples        = 1;
            desc.depthBufferBits    = 0;
            desc.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;

            // Allocate persistent RTHandles and import them into the render graph.
            // ReAllocateHandleIfNeeded resizes automatically when resolution changes.
            RenderingUtils.ReAllocateHandleIfNeeded(
                ref tempRead, desc, FilterMode.Bilinear, TextureWrapMode.Clamp,
                name: "_SmudgeRead");

            RenderingUtils.ReAllocateHandleIfNeeded(
                ref tempWrite, desc, FilterMode.Bilinear, TextureWrapMode.Clamp,
                name: "_SmudgeWrite");

            TextureHandle hRead  = renderGraph.ImportTexture(tempRead);
            TextureHandle hWrite = renderGraph.ImportTexture(tempWrite);
            TextureHandle hColor = resourceData.activeColorTexture;

            Vector2 normalizedDir = settings.direction.sqrMagnitude > 0f
                ? settings.direction.normalized
                : Vector2.right;

            settings.material.SetVector(SmudgeOffsetID, normalizedDir * settings.offset);
            settings.material.SetFloat(SmudgeBlendID, settings.blend);

            // Pass 1 — copy active color into tempRead (no material).
            RecordBlitPass(renderGraph, "Smudge_CopyToTemp", hColor, hRead, null);

            // Pass 2 — apply smudge from tempRead into tempWrite.
            RecordBlitPass(renderGraph, "Smudge_Apply", hRead, hWrite, settings.material);

            // Pass 3 — copy result back to the camera color target.
            RecordBlitPass(renderGraph, "Smudge_CopyBack", hWrite, hColor, null);
        }

        private static void RecordBlitPass(
            RenderGraph renderGraph,
            string passName,
            TextureHandle source,
            TextureHandle destination,
            Material material)
        {
            using var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData);

            passData.source      = source;
            passData.destination = destination;
            passData.material    = material;

            builder.UseTexture(source);
            builder.SetRenderAttachment(destination, 0);

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                if (data.material == null)
                    Blitter.BlitTexture(ctx.cmd, data.source, ScaleBias, 0, false);
                else
                    Blitter.BlitTexture(ctx.cmd, data.source, ScaleBias, data.material, 0);
            });
        }

        public void Dispose()
        {
            tempRead?.Release();
            tempWrite?.Release();
        }
    }

    // -------------------------------------------------------------------------
    public Settings settings = new Settings();
    private SmudgePass pass;

    public override void Create()
    {
        pass = new SmudgePass(settings)
        {
            renderPassEvent = settings.passEvent
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.material == null) return;
        pass.renderPassEvent = settings.passEvent;
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        pass?.Dispose();
    }
}
