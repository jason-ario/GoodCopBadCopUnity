using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP Renderer Feature that applies the full-screen tentacle-spiral blackout effect.
/// Add this to your URP Renderer asset, then assign the TentacleBlackout material.
/// Drive <c>_Progress</c> on the material (0 = clear, 1 = fully dark) via
/// <see cref="TentacleBlackoutController"/>.
/// </summary>
public class TentacleBlackoutFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [Tooltip("Material using the GoodCopBadCop/TentacleBlackout shader.")]
        public Material material = null;
    }

    // ─── Render pass ─────────────────────────────────────────────────────────

    private class TentaclePass : ScriptableRenderPass
    {
        private readonly Settings _settings;

        public TentaclePass(Settings settings)
        {
            _settings = settings;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_settings.material == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();

            if (resourceData.isActiveTargetBackBuffer)
            {
                Debug.LogError(
                    "TentacleBlackoutFeature requires an intermediate colour texture. " +
                    "Set the pass event before AfterRendering.");
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;

            var destDesc         = renderGraph.GetTextureDesc(source);
            destDesc.name        = "_TentacleBlackoutResult";
            destDesc.clearBuffer = false;

            TextureHandle destination = renderGraph.CreateTexture(destDesc);

            var blitParams = new RenderGraphUtils.BlitMaterialParameters(
                source, destination, _settings.material, 0);

            renderGraph.AddBlitPass(blitParams, passName: "TentacleBlackoutPass");

            resourceData.cameraColor = destination;
        }

        public void Dispose() { }
    }

    // ─── Feature ─────────────────────────────────────────────────────────────

    public Settings settings = new Settings();
    private TentaclePass _pass;

    public override void Create()
    {
        _pass = new TentaclePass(settings)
        {
            renderPassEvent = settings.passEvent
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.material == null) return;
        _pass.renderPassEvent = settings.passEvent;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
    }
}
