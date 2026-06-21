using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP Renderer Feature that applies a low-resolution pixelation effect driven by
/// a <see cref="LowResolutionVolume"/> override. Add this to your URP Renderer asset
/// and assign the GoodCopBadCop/LowResolution material.
/// </summary>
public class LowResolutionFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [Tooltip("Material using the GoodCopBadCop/LowResolution shader.")]
        public Material material = null;
    }

    // -------------------------------------------------------------------------
    private class LowResolutionPass : ScriptableRenderPass
    {
        private static readonly int PixelSizeID = Shader.PropertyToID("_PixelSize");

        private readonly Settings settings;

        /// <summary>Pixel block size resolved from the volume stack in AddRenderPasses.</summary>
        public int PixelSize { get; set; }

        public LowResolutionPass(Settings settings)
        {
            this.settings = settings;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings.material == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();

            if (resourceData.isActiveTargetBackBuffer)
            {
                Debug.LogError("LowResolutionFeature requires an intermediate color texture. " +
                               "Set the renderer's Intermediate Texture mode to Always, or change the pass event.");
                return;
            }

            var source = resourceData.activeColorTexture;

            var destDesc = renderGraph.GetTextureDesc(source);
            destDesc.name        = "_LowResResult";
            destDesc.clearBuffer = false;

            TextureHandle destination = renderGraph.CreateTexture(destDesc);

            settings.material.SetFloat(PixelSizeID, PixelSize);

            var para = new RenderGraphUtils.BlitMaterialParameters(source, destination, settings.material, 0);
            renderGraph.AddBlitPass(para, passName: "LowResolutionPass");

            // Swap camera color to our result — no copy-back blit needed.
            resourceData.cameraColor = destination;
        }

        public void Dispose() { }
    }

    // -------------------------------------------------------------------------
    public Settings settings = new Settings();
    private LowResolutionPass pass;

    public override void Create()
    {
        pass = new LowResolutionPass(settings)
        {
            renderPassEvent = settings.passEvent
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.material == null) return;

        // Resolve the volume here — the stack is guaranteed to be updated
        // by the time AddRenderPasses is called.
        var stack = VolumeManager.instance.stack;
        var volume = stack.GetComponent<LowResolutionVolume>();

        if (volume == null || !volume.IsActive()) return;

        pass.renderPassEvent = settings.passEvent;
        pass.PixelSize = volume.pixelSize.value;
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        pass?.Dispose();
    }
}
