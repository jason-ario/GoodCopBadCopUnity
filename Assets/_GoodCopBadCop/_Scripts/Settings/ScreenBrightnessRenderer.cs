using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace GoodCopBadCop.Settings
{
    /// <summary>
    /// Applies the Brightness preference as a final-image gamma adjustment. The pass is injected at
    /// runtime through <see cref="RenderPipelineManager.beginCameraRendering"/> after all
    /// post-processing (same pattern as GlitchEffectView), so it never changes exposure, bloom or
    /// tonemapping and needs no renderer asset setup. Only the last camera of a stack is adjusted,
    /// and the pass is skipped entirely when the setting is neutral.
    /// </summary>
    public sealed class ScreenBrightnessRenderer : IDisposable
    {
        private const string ShaderResourcePath = "Shaders/ScreenBrightness";
        private const float NeutralThreshold = 0.001f;

        private static readonly int BrightnessGammaId = Shader.PropertyToID("_BrightnessGamma");

        private sealed class BrightnessPass : ScriptableRenderPass
        {
            public Material Material;

            public BrightnessPass()
            {
                // Run after any other AfterRenderingPostProcessing passes (glitch, smudge, ...).
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing + 10;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (Material == null)
                    return;

                var cameraData = frameData.Get<UniversalCameraData>();
                if (!cameraData.resolveFinalTarget)
                    return;

                var resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer)
                    return;

                TextureHandle source = resourceData.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "_ScreenBrightnessResult";
                desc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(desc);

                var blitParams = new RenderGraphUtils.BlitMaterialParameters(source, destination, Material, 0);
                renderGraph.AddBlitPass(blitParams, passName: "ScreenBrightnessPass");

                resourceData.cameraColor = destination;
            }
        }

        private readonly Material material;
        private readonly BrightnessPass pass;
        private float gamma = 1f;

        public ScreenBrightnessRenderer()
        {
            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                Debug.LogWarning($"[ScreenBrightnessRenderer] Shader not found at Resources/{ShaderResourcePath}; Brightness will not be applied.");
                return;
            }

            material = new Material(shader) { name = "Screen Brightness (Runtime)", hideFlags = HideFlags.HideAndDontSave };
            pass = new BrightnessPass { Material = material };
            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        }

        /// <summary>Gamma exponent applied to the final image: &lt;1 brightens, &gt;1 darkens, 1 is neutral.</summary>
        public void SetGamma(float value)
        {
            gamma = Mathf.Max(0.01f, value);
            if (material != null)
                material.SetFloat(BrightnessGammaId, gamma);
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            if (material != null)
                Object.Destroy(material);
        }

        private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (pass == null || Mathf.Abs(gamma - 1f) < NeutralThreshold || camera.cameraType != CameraType.Game)
                return;

            if (!camera.TryGetComponent(out UniversalAdditionalCameraData cameraData) ||
                cameraData.scriptableRenderer == null)
                return;

            cameraData.scriptableRenderer.EnqueuePass(pass);
        }
    }
}
