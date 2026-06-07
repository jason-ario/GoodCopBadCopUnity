using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP Renderer Feature that applies a full-screen ordered (Bayer matrix) dithering effect.
/// Add this to your URP Renderer asset, then assign the GoodCopBadCop/Dither material.
/// </summary>
public class DitherFeature : ScriptableRendererFeature
{
    public enum DitherPattern
    {
        Bayer2x2,
        Bayer4x4,
        Bayer8x8,
    }

    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;
        public Material material = null;

        [Tooltip("Bayer matrix size. Larger = finer, more dispersed pattern.")]
        public DitherPattern pattern = DitherPattern.Bayer8x8;

        [Tooltip("How far the dither threshold spreads within each quantization step. Higher = more visible pattern.")]
        [Range(0f, 1f)] public float strength = 0.5f;

        [Tooltip("Blend between the original image (0) and the fully dithered result (1). Lower values reduce darkening.")]
        [Range(0f, 1f)] public float blend = 1f;

        [Tooltip("Size of each dither pixel block in screen pixels. 1 = native, 2+ = chunky retro look.")]
        [Range(1f, 16f)] public float scale = 1f;

        [Tooltip("Number of quantization steps per channel. Lower = more posterized. 4-8 gives a clear palette effect, 256 is near-lossless.")]
        [Range(2, 256)] public int colorDepth = 8;

        [Tooltip("Dither luminance only, preserving hue and saturation.")]
        public bool lumaOnly = false;
    }

    // -------------------------------------------------------------------------
    private class DitherPass : ScriptableRenderPass
    {
        private static readonly int StrengthID = Shader.PropertyToID("_DitherStrength");
        private static readonly int BlendID    = Shader.PropertyToID("_DitherBlend");
        private static readonly int ScaleID    = Shader.PropertyToID("_DitherScale");
        private static readonly int DepthID    = Shader.PropertyToID("_DitherColorDepth");
        private static readonly int LumaOnlyID = Shader.PropertyToID("_DitherLumaOnly");

        private const string KwBayer2 = "DITHER_BAYER2";
        private const string KwBayer4 = "DITHER_BAYER4";
        private const string KwBayer8 = "DITHER_BAYER8";

        private static readonly Vector4 ScaleBias = new Vector4(1f, 1f, 0f, 0f);

        private readonly Settings settings;

        public DitherPass(Settings settings)
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
                Debug.LogError("DitherFeature requires an intermediate color texture. Set the pass event before AfterRendering.");
                return;
            }

            var source = resourceData.activeColorTexture;

            var destDesc = renderGraph.GetTextureDesc(source);
            destDesc.name        = "_DitherResult";
            destDesc.clearBuffer = false;

            TextureHandle destination = renderGraph.CreateTexture(destDesc);

            settings.material.DisableKeyword(KwBayer2);
            settings.material.DisableKeyword(KwBayer4);
            settings.material.DisableKeyword(KwBayer8);
            switch (settings.pattern)
            {
                case DitherPattern.Bayer2x2: settings.material.EnableKeyword(KwBayer2); break;
                case DitherPattern.Bayer4x4: settings.material.EnableKeyword(KwBayer4); break;
                default:                     settings.material.EnableKeyword(KwBayer8); break;
            }

            settings.material.SetFloat(StrengthID, settings.strength);
            settings.material.SetFloat(BlendID,    settings.blend);
            settings.material.SetFloat(ScaleID,    settings.scale);
            settings.material.SetInt(DepthID,      settings.colorDepth);
            settings.material.SetFloat(LumaOnlyID, settings.lumaOnly ? 1f : 0f);

            var para = new RenderGraphUtils.BlitMaterialParameters(source, destination, settings.material, 0);
            renderGraph.AddBlitPass(para, passName: "DitherPass");

            // Swap camera color to our result — no copy-back blit needed.
            resourceData.cameraColor = destination;
        }

        public void Dispose() { }
    }

    // -------------------------------------------------------------------------
    public Settings settings = new Settings();
    private DitherPass pass;

    public override void Create()
    {
        pass = new DitherPass(settings)
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
