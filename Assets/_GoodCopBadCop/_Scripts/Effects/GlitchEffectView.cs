using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace GoodCopBadCop.Effects
{
    /// <summary>
    /// Local-only edge glitch (warping, band tearing, RGB split and a tinted glow at the screen borders)
    /// for presets using <see cref="EFullscreenEffectMode.Glitch"/>.
    /// The pass is injected at runtime through <see cref="RenderPipelineManager.beginCameraRendering"/>
    /// at AfterRenderingPostProcessing, so no renderer asset setup is needed, and the tint is never
    /// re-graded by the scene's color grading (which is what turned the old yellow vignette dark green).
    /// The pass is only enqueued while the effect is visible.
    /// </summary>
    public sealed class GlitchEffectView : MonoBehaviour
    {
        private const string ShaderResourcePath = "Shaders/RadiationGlitch";

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
        private static readonly int WarpStrengthId = Shader.PropertyToID("_WarpStrength");
        private static readonly int GlitchStrengthId = Shader.PropertyToID("_GlitchStrength");
        private static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");

        private sealed class GlitchPass : ScriptableRenderPass
        {
            public Material Material;

            public GlitchPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (Material == null)
                    return;

                var resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer)
                    return;

                TextureHandle source = resourceData.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "_RadiationGlitchResult";
                desc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(desc);

                var blitParams = new RenderGraphUtils.BlitMaterialParameters(source, destination, Material, 0);
                renderGraph.AddBlitPass(blitParams, passName: "RadiationGlitchPass");

                resourceData.cameraColor = destination;
            }
        }

        private Material material;
        private GlitchPass pass;
        private Coroutine routine;
        private float intensity;

        public static GlitchEffectView CreateDefaultView()
        {
            var viewObject = new GameObject("Glitch Effect View");
            Object.DontDestroyOnLoad(viewObject);
            return viewObject.AddComponent<GlitchEffectView>();
        }

        public void Play(FullscreenEffectSettings settings)
        {
            if (settings == null || !EnsureMaterial())
                return;

            Color tint = settings.Tint;
            tint.a = 1f;
            material.SetColor(TintId, tint);
            material.SetFloat(EdgeWidthId, settings.GlitchEdgeWidth);
            material.SetFloat(WarpStrengthId, settings.GlitchWarpStrength);
            material.SetFloat(GlitchStrengthId, settings.GlitchTearStrength);
            material.SetFloat(GlowStrengthId, settings.GlitchGlowStrength);

            if (routine != null)
                StopCoroutine(routine);

            routine = StartCoroutine(PlayRoutine(settings));
        }

        /// <summary>Immediately clears the effect (e.g. on death/spectate).</summary>
        public void Hide()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            SetIntensity(0f);
        }

        private IEnumerator PlayRoutine(FullscreenEffectSettings settings)
        {
            float duration = Mathf.Max(0.01f, settings.Duration);
            float peak = Mathf.Clamp01(settings.Opacity);
            // Carry over a still-fading pulse so back-to-back ticks hold the effect instead of popping.
            float carry = intensity;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float target = peak * Mathf.Clamp01(settings.OpacityCurve.Evaluate(t));
                SetIntensity(Mathf.Max(target, carry * (1f - t)));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            SetIntensity(0f);
            routine = null;
        }

        private void SetIntensity(float value)
        {
            intensity = value;
            if (material != null)
                material.SetFloat(IntensityId, value);
        }

        private bool EnsureMaterial()
        {
            if (material != null)
                return true;

            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                Debug.LogWarning($"[GlitchEffectView] Shader not found at Resources/{ShaderResourcePath}.", this);
                return false;
            }

            material = new Material(shader) { name = "Radiation Glitch (Runtime)" };
            pass ??= new GlitchPass();
            pass.Material = material;
            return true;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            routine = null;
            SetIntensity(0f);
        }

        private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (intensity <= 0.002f || pass == null || camera.cameraType != CameraType.Game)
                return;

            if (!camera.TryGetComponent(out UniversalAdditionalCameraData cameraData) ||
                cameraData.renderType != CameraRenderType.Base ||
                cameraData.scriptableRenderer == null)
                return;

            cameraData.scriptableRenderer.EnqueuePass(pass);
        }

        private void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }
    }
}
