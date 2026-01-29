////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) Martin Bustos @FronkonGames <fronkongames@gmail.com>. All rights reserved.
//
// THIS FILE CAN NOT BE HOSTED IN PUBLIC REPOSITORIES.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
#endif

namespace FronkonGames.Retro.Noir
{
  ///------------------------------------------------------------------------------------------------------------------
  /// <summary> Render Pass. </summary>
  /// <remarks> Only available for Universal Render Pipeline. </remarks>
  ///------------------------------------------------------------------------------------------------------------------
  public sealed partial class Noir
  {
    [DisallowMultipleRendererFeature]
    private sealed class RenderPass : ScriptableRenderPass
    {
      // Internal use only.
      internal Material material { get; set; }

      private readonly Settings settings;

#if UNITY_6000_0_OR_NEWER
      private TextureHandle renderTextureHandle0;
      private TextureHandle renderTextureHandle1;
#else
      private RenderTargetIdentifier colorBuffer;
      private RenderTextureDescriptor renderTextureDescriptor;

      private readonly int renderTextureHandle0 = Shader.PropertyToID($"{Constants.Asset.AssemblyName}.RTH0");
      private readonly int renderTextureHandle1 = Shader.PropertyToID($"{Constants.Asset.AssemblyName}.RTH1");

      private const string CommandBufferName = Constants.Asset.AssemblyName;

      private ProfilingScope profilingScope;
      private readonly ProfilingSampler profilingSamples = new(Constants.Asset.AssemblyName);
#endif

      private static class ShaderIDs
      {
        public static readonly int Intensity = Shader.PropertyToID("_Intensity");
        public static readonly int EffectTime = Shader.PropertyToID("_EffectTime");

        public static readonly int NoirIntensity = Shader.PropertyToID("_NoirIntensity");

        public static readonly int DitherSpread = Shader.PropertyToID("_DitherSpread");
        public static readonly int DitherDensity = Shader.PropertyToID("_DitherDensity");
        public static readonly int DitherColorBlend = Shader.PropertyToID("_DitherColorBlend");

        public static readonly int DotScreenGridSize = Shader.PropertyToID("_DotScreenGridSize");
        public static readonly int DotScreenColorBlend = Shader.PropertyToID("_DotScreenColorBlend");
        public static readonly int DotScreenLuminanceGain = Shader.PropertyToID("_DotScreenLuminanceGain");

        public static readonly int HalftoneSize = Shader.PropertyToID("_HalftoneSize");
        public static readonly int HalftoneAngle = Shader.PropertyToID("_HalftoneAngle");
        public static readonly int HalftoneThreshold = Shader.PropertyToID("_HalftoneThreshold");
        public static readonly int HalftoneColorBlend = Shader.PropertyToID("_HalftoneColorBlend");

        public static readonly int LinesCount = Shader.PropertyToID("_LinesCount");
        public static readonly int LinesGranularity = Shader.PropertyToID("_LinesGranularity");
        public static readonly int LinesThreshold = Shader.PropertyToID("_LinesThreshold");
        public static readonly int LinesColorBlend = Shader.PropertyToID("_LinesColorBlend");

        public static readonly int DuoToneIntensity = Shader.PropertyToID("_DuoToneIntensity");
        public static readonly int DuoToneThreshold = Shader.PropertyToID("_DuoToneThreshold");
        public static readonly int DuoToneBrightnessColor = Shader.PropertyToID("_DuoToneBrightnessColor");
        public static readonly int DuoToneDarknessColor = Shader.PropertyToID("_DuoToneDarknessColor");
        public static readonly int DuoToneLuminanceMinRange = Shader.PropertyToID("_DuoToneLuminanceMinRange");
        public static readonly int DuoToneLuminanceMaxRange = Shader.PropertyToID("_DuoToneLuminanceMaxRange");
        public static readonly int DuoToneSoftness = Shader.PropertyToID("_DuoToneSoftness");
        public static readonly int DuoToneExposure = Shader.PropertyToID("_DuoToneExposure");
        public static readonly int DuoToneColorBlend = Shader.PropertyToID("_DuoToneColorBlend");
        public static readonly int DuoToneEmbossDark = Shader.PropertyToID("_DuoToneEmbossDark");

        public static readonly int SepiaIntensity = Shader.PropertyToID("_SepiaIntensity");

        public static readonly int ChromaticAberrationIntensity = Shader.PropertyToID("_ChromaticAberrationIntensity");

        public static readonly int FilmGrainIntensity = Shader.PropertyToID("_FilmGrainIntensity");
        public static readonly int FilmGrainSpeed = Shader.PropertyToID("_FilmGrainSpeed");

        public static readonly int BlotchesIntensity = Shader.PropertyToID("_BlotchesIntensity");
        public static readonly int BlotchesSpeed = Shader.PropertyToID("_BlotchesSpeed");

        public static readonly int VignetteColor = Shader.PropertyToID("_VignetteColor");
        public static readonly int VignetteIntensity = Shader.PropertyToID("_VignetteIntensity");
        public static readonly int VignetteSize = Shader.PropertyToID("_VignetteSize");
        public static readonly int VignetteSmoothness = Shader.PropertyToID("_VignetteSmoothness");
        public static readonly int VignetteTimeVariation = Shader.PropertyToID("_VignetteTimeVariation");

        public static readonly int ScratchesIntensity = Shader.PropertyToID("_ScratchesIntensity");
        public static readonly int ScratchesSpeed = Shader.PropertyToID("_ScratchesSpeed");

        public static readonly int Brightness = Shader.PropertyToID("_Brightness");
        public static readonly int Contrast = Shader.PropertyToID("_Contrast");
        public static readonly int Gamma = Shader.PropertyToID("_Gamma");
        public static readonly int Hue = Shader.PropertyToID("_Hue");
        public static readonly int Saturation = Shader.PropertyToID("_Saturation");      
      }

      private static class Keywords
      {
        public static readonly string Dither    = "DITHER_ON";
        public static readonly string DotScreen = "DOTSCREEN_ON";
        public static readonly string Halftone  = "HALFTONE_ON";
        public static readonly string Lines     = "LINES_ON";

        public static readonly string EmbossDark = "EMBOSS_DARK_ON";

        public static readonly string Sepia = "SEPIA_ON";

        public static readonly string ChromaticAberration = "CHROMATIC_ABERRATION_ON";

        public static readonly string FilmGrain = "FILMGRAIN_ON";

        public static readonly string Blotches = "BLOTCHES_ON";

        public static readonly string Vignette = "VIGNETTE_ON";

        public static readonly string Scratches = "SCRATCHES_ON";
      }
      
      /// <summary> Render pass constructor. </summary>
      public RenderPass(Settings settings) : base()
      {
        this.settings = settings;

#if UNITY_6000_0_OR_NEWER
        profilingSampler = new ProfilingSampler(Constants.Asset.AssemblyName);
#endif
      }

      private void UpdateMaterial()
      {
        if (material != null)
        {
          material.shaderKeywords = null;
          material.SetFloat(ShaderIDs.Intensity, settings.intensity);

          float time = settings.useScaledTime == true ? Time.time : Time.unscaledTime;
          material.SetVector(ShaderIDs.EffectTime, new Vector4(time / 20.0f, time, time * 2.0f, time * 3.0f));

          material.SetFloat(ShaderIDs.NoirIntensity, settings.noirIntensity);

          if (settings.noirIntensity > 0.0f)
          {
            switch (settings.method)
            {
              case NoirMethod.Dither:
                CoreUtils.SetKeyword(material, Keywords.Dither, true);
                material.SetInt(ShaderIDs.DitherColorBlend, (int)settings.ditherColorBlend);
                material.SetFloat(ShaderIDs.DitherSpread, 0.9f + 0.1f * settings.ditherSpread);
                material.SetFloat(ShaderIDs.DitherDensity, Mathf.Max(settings.ditherDensity, 0.05f));
                break;
              case NoirMethod.DotScreen:
                CoreUtils.SetKeyword(material, Keywords.DotScreen, true);
                material.SetInt(ShaderIDs.DotScreenGridSize, settings.dotScreenGridSize);
                material.SetInt(ShaderIDs.DotScreenColorBlend, (int)settings.dotScreenColorBlend);
                material.SetFloat(ShaderIDs.DotScreenLuminanceGain, settings.dotScreenLuminanceGain);
                break;
              case NoirMethod.Halftone:
                CoreUtils.SetKeyword(material, Keywords.Halftone, true);
                material.SetFloat(ShaderIDs.HalftoneSize, settings.halftoneSize);
                material.SetFloat(ShaderIDs.HalftoneAngle, settings.halftoneAngle * Mathf.Deg2Rad);
                material.SetFloat(ShaderIDs.HalftoneThreshold, settings.halftoneThreshold * 0.9f);
                material.SetInt(ShaderIDs.HalftoneColorBlend, (int)settings.halftoneColorBlend);
                break;
              case NoirMethod.Lines:
                CoreUtils.SetKeyword(material, Keywords.Lines, true);
                material.SetInt(ShaderIDs.LinesCount, settings.linesCount);
                material.SetFloat(ShaderIDs.LinesGranularity, settings.linesGranularity);
                material.SetFloat(ShaderIDs.LinesThreshold, settings.linesThreshold);
                material.SetInt(ShaderIDs.LinesColorBlend, (int)settings.linesColorBlend);
                break;
            }
          }

          if (settings.duoToneIntensity > 0.0f)
          {
            material.SetFloat(ShaderIDs.DuoToneIntensity, settings.duoToneIntensity);
            material.SetFloat(ShaderIDs.DuoToneThreshold, settings.duoToneThreshold);
            material.SetColor(ShaderIDs.DuoToneBrightnessColor, settings.duoToneBrightnessColor);
            material.SetColor(ShaderIDs.DuoToneDarknessColor, settings.duoToneDarknessColor);
            material.SetFloat(ShaderIDs.DuoToneLuminanceMinRange, settings.duoToneLuminanceMinRange);
            material.SetFloat(ShaderIDs.DuoToneLuminanceMaxRange, settings.duoToneLuminanceMaxRange);
            material.SetFloat(ShaderIDs.DuoToneSoftness, settings.duoToneSoftness);
            material.SetFloat(ShaderIDs.DuoToneExposure, settings.duoToneExposure);
            material.SetInt(ShaderIDs.DuoToneColorBlend, (int)settings.duoToneColorBlend);

            if (settings.duoToneEmbossDark > 0.0f)
            {
              CoreUtils.SetKeyword(material, Keywords.EmbossDark, true);
              material.SetFloat(ShaderIDs.DuoToneEmbossDark, Mathf.Min(settings.duoToneEmbossDark, 0.99f));
            }
          }

          if (settings.sepiaIntensity > 0.0f)
          {
            CoreUtils.SetKeyword(material, Keywords.Sepia, true);
            material.SetFloat(ShaderIDs.SepiaIntensity, settings.sepiaIntensity);
          }

          if (settings.blotchesIntensity > 0.0f)
          {
            CoreUtils.SetKeyword(material, Keywords.Blotches, true);
            material.SetFloat(ShaderIDs.BlotchesIntensity, settings.blotchesIntensity);
            material.SetFloat(ShaderIDs.BlotchesSpeed, settings.blotchesSpeed);
          }

          if (settings.chromaticAberrationIntensity > 0.0f)
          {
            CoreUtils.SetKeyword(material, Keywords.ChromaticAberration, true);
            material.SetFloat(ShaderIDs.ChromaticAberrationIntensity, settings.chromaticAberrationIntensity * 20.0f);
          }

          if (settings.filmGrainIntensity > 0.0f)
          {
            CoreUtils.SetKeyword(material, Keywords.FilmGrain, true);
            material.SetFloat(ShaderIDs.FilmGrainIntensity, settings.filmGrainIntensity);
            material.SetFloat(ShaderIDs.FilmGrainSpeed, settings.filmGrainSpeed);
          }

          if (settings.scratchesIntensity > 0.0f)
          {
            CoreUtils.SetKeyword(material, Keywords.Scratches, true);
            material.SetFloat(ShaderIDs.ScratchesIntensity, settings.scratchesIntensity);
            material.SetFloat(ShaderIDs.ScratchesSpeed, settings.scratchesSpeed * 10.0f);
          }

          CoreUtils.SetKeyword(material, Keywords.Vignette, settings.vignetteIntensity > 0.0f);
          material.SetVector(ShaderIDs.VignetteColor, settings.vignetteColor);
          material.SetFloat(ShaderIDs.VignetteIntensity, settings.vignetteIntensity);
          material.SetFloat(ShaderIDs.VignetteSmoothness, settings.vignetteSmoothness);
          material.SetFloat(ShaderIDs.VignetteSize, settings.vignetteSize);
          material.SetFloat(ShaderIDs.VignetteTimeVariation, settings.vignetteTimeVariation * 20.0f);

          material.SetFloat(ShaderIDs.Brightness, settings.brightness);
          material.SetFloat(ShaderIDs.Contrast, settings.contrast);
          material.SetFloat(ShaderIDs.Gamma, 1.0f / settings.gamma);
          material.SetFloat(ShaderIDs.Hue, settings.hue);
          material.SetFloat(ShaderIDs.Saturation, settings.saturation);
        }
      }

#if UNITY_6000_0_OR_NEWER
      /// <inheritdoc/>
      public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
      {
        if (material == null || settings.intensity == 0.0f)
          return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        if (resourceData.isActiveTargetBackBuffer == true)
          return;

        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        if (cameraData.camera.cameraType == CameraType.SceneView && settings.affectSceneView == false || cameraData.postProcessEnabled == false)
          return;

        TextureHandle source = resourceData.activeColorTexture;
        TextureDesc sourceDesc = source.GetDescriptor(renderGraph);

        UpdateMaterial();

        if (settings.chromaticAberrationIntensity > 0.0f)
        {
          renderTextureHandle0 = renderGraph.CreateTexture(sourceDesc);
          renderTextureHandle1 = renderGraph.CreateTexture(sourceDesc);

          renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, renderTextureHandle0, material, 0), $"{Constants.Asset.AssemblyName}.Pass0");
          renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(renderTextureHandle0, renderTextureHandle1, material, 1), $"{Constants.Asset.AssemblyName}.Pass1");

          resourceData.cameraColor = renderTextureHandle1;
        }
        else
        {
          TextureHandle destination = renderGraph.CreateTexture(sourceDesc);

          RenderGraphUtils.BlitMaterialParameters pass = new(source, destination, material, 0);
          renderGraph.AddBlitPass(pass, $"{Constants.Asset.AssemblyName}.Pass");

          resourceData.cameraColor = destination;
        }

      }
#elif UNITY_2022_3_OR_NEWER
      /// <inheritdoc/>
      public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
      {
        renderTextureDescriptor = renderingData.cameraData.cameraTargetDescriptor;
        renderTextureDescriptor.depthBufferBits = (int)DepthBits.None;

        colorBuffer = renderingData.cameraData.renderer.cameraColorTargetHandle;
        cmd.GetTemporaryRT(renderTextureHandle0, renderTextureDescriptor);
        cmd.GetTemporaryRT(renderTextureHandle1, renderTextureDescriptor);
      }

      /// <inheritdoc/>
      public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
      {
        base.Configure(cmd, cameraTextureDescriptor);
        ConfigureInput(ScriptableRenderPassInput.None);
      }

      /// <inheritdoc/>
      public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
      {
        if (material == null ||
            renderingData.postProcessingEnabled == false ||
            settings.intensity <= 0.0f ||
            (settings.affectSceneView == false && renderingData.cameraData.isSceneViewCamera == true))
          return;

        CommandBuffer cmd = CommandBufferPool.Get(CommandBufferName);

        if (settings.enableProfiling == true)
          profilingScope = new ProfilingScope(cmd, profilingSamples);

        UpdateMaterial();

        if (settings.chromaticAberrationIntensity > 0.0f)
        {
          cmd.Blit(colorBuffer, renderTextureHandle0, material, 0);
          cmd.Blit(renderTextureHandle0, renderTextureHandle1, material, 1);
          cmd.Blit(renderTextureHandle1, colorBuffer);
        }
        else
        {
          cmd.Blit(colorBuffer, renderTextureHandle0, material, 0);
          cmd.Blit(renderTextureHandle0, colorBuffer);
        }

        if (settings.enableProfiling == true)
          profilingScope.Dispose();

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
      }

      public override void OnCameraCleanup(CommandBuffer cmd)
      {
        if (cmd == null)
          throw new System.ArgumentNullException("cmd");

        cmd.ReleaseTemporaryRT(renderTextureHandle0);
      }
#else
      #error Unsupported Unity version. Please update to a newer version of Unity.
#endif
    }
  }
}
