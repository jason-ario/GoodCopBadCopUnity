using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP Renderer Feature that applies a full-screen smudge / dreamy-smear effect.
/// Add this to your URP Renderer asset, then assign the Hidden/Smudge material.
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

        private readonly Settings settings;
        private RTHandle tempTex;

        public SmudgePass(Settings settings)
        {
            this.settings = settings;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref tempTex, desc, name: "_SmudgeTemp");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings.material == null) return;

            var cmd = CommandBufferPool.Get("SmudgePass");

            Vector2 normalizedDir = settings.direction.sqrMagnitude > 0f
                ? settings.direction.normalized
                : Vector2.right;

            settings.material.SetVector(SmudgeOffsetID, normalizedDir * settings.offset);
            settings.material.SetFloat(SmudgeBlendID, settings.blend);

            var source = renderingData.cameraData.renderer.cameraColorTargetHandle;

            Blitter.BlitCameraTexture(cmd, source, tempTex, settings.material, 0);
            Blitter.BlitCameraTexture(cmd, tempTex, source);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) { }

        public void Dispose()
        {
            tempTex?.Release();
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
