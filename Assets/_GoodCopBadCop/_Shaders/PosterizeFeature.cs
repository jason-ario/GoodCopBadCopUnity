using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PosterizeFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;
        public Material material = null;

        [Range(2, 256)] public int steps = 16;
        [Range(0f, 1f)] public float ditherStrength = 0.12f;
        public bool lumaOnly = true;
    }

    class PosterizePass : ScriptableRenderPass
    {
        static readonly int StepsID = Shader.PropertyToID("_PosterSteps");
        static readonly int DitherID = Shader.PropertyToID("_DitherStrength");
        static readonly int LumaOnlyID = Shader.PropertyToID("_LumaOnly");

        readonly Settings settings;
        RTHandle tempTex;

        public PosterizePass(Settings settings)
        {
            this.settings = settings;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref tempTex, desc, name: "_PosterizeTemp");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings.material == null) return;

            var cmd = CommandBufferPool.Get("PosterizePass");

            settings.material.SetFloat(StepsID, Mathf.Max(2, settings.steps));
            settings.material.SetFloat(DitherID, settings.ditherStrength);
            settings.material.SetFloat(LumaOnlyID, settings.lumaOnly ? 1f : 0f);

            // Source: actual camera color target (includes transparents)
            var source = renderingData.cameraData.renderer.cameraColorTargetHandle;

            // Blit source -> temp (with effect), then temp -> source
            Blitter.BlitCameraTexture(cmd, source, tempTex, settings.material, 0);
            Blitter.BlitCameraTexture(cmd, tempTex, source);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // RTHandle is managed; no manual Release needed here
        }
    }

    public Settings settings = new Settings();
    PosterizePass pass;

    public override void Create()
    {
        pass = new PosterizePass(settings)
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
}