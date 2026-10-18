using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace GoodCopBadCop.Effects
{
    /// <summary>
    /// Local-only tinted vignette (hurt-style colored vignette, desaturation and optional chromatic
    /// aberration) for presets using <see cref="EFullscreenEffectMode.Glitch"/> and for the sustained
    /// radiation effect. Glitching for radiation comes from <c>GlitchController</c>.
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
        private static readonly int VignetteIntensityId = Shader.PropertyToID("_VignetteIntensity");
        private static readonly int VignetteSmoothnessId = Shader.PropertyToID("_VignetteSmoothness");
        private static readonly int ChromaticId = Shader.PropertyToID("_Chromatic");
        private static readonly int DesaturateId = Shader.PropertyToID("_Desaturate");
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

        private static GlitchEffectView s_instance;

        private Material material;
        private GlitchPass pass;
        private Coroutine routine;
        private float intensity;
        private float pulseIntensity;
        private float sustainedIntensity;

        /// <summary>Returns the scene's view, creating a persistent default one if none exists.</summary>
        public static GlitchEffectView GetOrCreate()
        {
            if (s_instance == null)
                s_instance = Object.FindFirstObjectByType<GlitchEffectView>();
            if (s_instance == null)
                s_instance = CreateDefaultView();
            return s_instance;
        }

        public static GlitchEffectView CreateDefaultView()
        {
            var viewObject = new GameObject("Glitch Effect View");
            Object.DontDestroyOnLoad(viewObject);
            return viewObject.AddComponent<GlitchEffectView>();
        }

        private void Awake()
        {
            if (s_instance == null)
                s_instance = this;
        }

        public void Play(FullscreenEffectSettings settings)
        {
            if (settings == null)
                return;

            SetLook(settings.Tint, settings.VignetteIntensity, settings.VignetteSmoothness,
                settings.ChromaticAberration, Mathf.Clamp01(-settings.Saturation / 100f),
                settings.GlitchGlowStrength);

            if (material == null)
                return;

            if (routine != null)
                StopCoroutine(routine);

            routine = StartCoroutine(PlayRoutine(settings));
        }

        /// <summary>Sets the shader look parameters shared by pulses and the sustained level.</summary>
        /// <param name="desaturate">0 = untouched, 1 = fully greyscale at full intensity.</param>
        public void SetLook(Color tint, float vignetteIntensity, float vignetteSmoothness,
            float chromaticAberration, float desaturate, float glowStrength)
        {
            if (!EnsureMaterial())
                return;

            tint.a = 1f;
            material.SetColor(TintId, tint);
            material.SetFloat(VignetteIntensityId, vignetteIntensity);
            material.SetFloat(VignetteSmoothnessId, Mathf.Max(0.01f, vignetteSmoothness));
            material.SetFloat(ChromaticId, chromaticAberration);
            material.SetFloat(DesaturateId, desaturate);
            material.SetFloat(GlowStrengthId, glowStrength);
        }

        /// <summary>
        /// Continuous intensity (e.g. driven by the radiation level). The rendered intensity is the
        /// stronger of this and any in-flight one-shot pulse, so the two never fight.
        /// </summary>
        public void SetSustainedIntensity(float value)
        {
            sustainedIntensity = Mathf.Clamp01(value);
            ApplyIntensity();
        }

        /// <summary>Immediately clears the effect (e.g. on death/spectate).</summary>
        public void Hide()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            pulseIntensity = 0f;
            sustainedIntensity = 0f;
            ApplyIntensity();
        }

        private IEnumerator PlayRoutine(FullscreenEffectSettings settings)
        {
            float duration = Mathf.Max(0.01f, settings.Duration);
            float peak = Mathf.Clamp01(settings.Opacity);
            // Carry over a still-fading pulse so back-to-back ticks hold the effect instead of popping.
            float carry = pulseIntensity;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float target = peak * Mathf.Clamp01(settings.OpacityCurve.Evaluate(t));
                pulseIntensity = Mathf.Max(target, carry * (1f - t));
                ApplyIntensity();
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            pulseIntensity = 0f;
            ApplyIntensity();
            routine = null;
        }

        private void ApplyIntensity()
        {
            intensity = Mathf.Max(pulseIntensity, sustainedIntensity);
            if (material != null)
                material.SetFloat(IntensityId, intensity);
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
            pulseIntensity = 0f;
            sustainedIntensity = 0f;
            ApplyIntensity();
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
            if (s_instance == this)
                s_instance = null;

            if (material != null)
                Destroy(material);
        }
    }
}
