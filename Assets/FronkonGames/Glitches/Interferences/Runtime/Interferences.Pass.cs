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
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace FronkonGames.Glitches.Interferences
{
  /// <summary> Render Pass. </summary>
  public sealed partial class Interferences
  {
    [DisallowMultipleRendererFeature]
    private sealed class RenderPass : ScriptableRenderPass
    {
      // Internal use only.
      internal Material material { get; set; }

      private static class ShaderIDs
      {
        internal static readonly int Intensity = Shader.PropertyToID("_Intensity");
        internal static readonly int EffectTime = Shader.PropertyToID("_EffectTime");

        internal static readonly int Blend = Shader.PropertyToID("_Blend");
        internal static readonly int Offset = Shader.PropertyToID("_Offset");
        internal static readonly int Distortion = Shader.PropertyToID("_Distortion");
        internal static readonly int DistortionSpeed = Shader.PropertyToID("_DistortionSpeed");
        internal static readonly int DistortionDensity = Shader.PropertyToID("_DistortionDensity");
        internal static readonly int DistortionAmplitude = Shader.PropertyToID("_DistortionAmplitude");
        internal static readonly int DistortionFrequency = Shader.PropertyToID("_DistortionFrequency");
        internal static readonly int Scanlines = Shader.PropertyToID("_Scanlines");
        internal static readonly int ScanlinesDensity = Shader.PropertyToID("_ScanlinesDensity");
        internal static readonly int ScanlinesOpacity = Shader.PropertyToID("_ScanlinesOpacity");

        internal static readonly int Brightness = Shader.PropertyToID("_Brightness");
        internal static readonly int Contrast = Shader.PropertyToID("_Contrast");
        internal static readonly int Gamma = Shader.PropertyToID("_Gamma");
        internal static readonly int Hue = Shader.PropertyToID("_Hue");
        internal static readonly int Saturation = Shader.PropertyToID("_Saturation");
      }

      /// <summary> Render pass constructor. </summary>
      public RenderPass() : base()
      {
        profilingSampler = new ProfilingSampler(Constants.Asset.AssemblyName);
      }

      private void UpdateVolume(InterferencesVolume volume)
      {
        material.shaderKeywords = null;

        material.SetFloat(ShaderIDs.Intensity, volume.intensity.value);

        float time = volume.useScaledTime.value == true ? Time.time : Time.unscaledTime;
        material.SetVector(ShaderIDs.EffectTime, new Vector4(time / 20.0f, time, time * 2.0f, time * 3.0f));
        material.SetFloat(ShaderIDs.Distortion, volume.distortion.value);

        material.SetInt(ShaderIDs.Blend, (int)volume.blend.value);
        material.SetFloat(ShaderIDs.Offset, volume.offset.value);
        material.SetFloat(ShaderIDs.DistortionSpeed, volume.distortionSpeed.value);
        material.SetFloat(ShaderIDs.DistortionDensity, volume.distortionDensity.value);
        material.SetFloat(ShaderIDs.DistortionAmplitude, volume.distortionAmplitude.value);
        material.SetFloat(ShaderIDs.DistortionFrequency, volume.distortionFrequency.value);
        material.SetFloat(ShaderIDs.Scanlines, volume.scanlines.value);
        material.SetFloat(ShaderIDs.ScanlinesDensity, volume.scanlinesDensity.value);
        material.SetFloat(ShaderIDs.ScanlinesOpacity, volume.scanlinesOpacity.value);

        material.SetFloat(ShaderIDs.Brightness, volume.brightness.value);
        material.SetFloat(ShaderIDs.Contrast, volume.contrast.value);
        material.SetFloat(ShaderIDs.Gamma, 1.0f / volume.gamma.value);
        material.SetFloat(ShaderIDs.Hue, volume.hue.value);
        material.SetFloat(ShaderIDs.Saturation, volume.saturation.value);
      }

      /// <inheritdoc/>
      public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
      {
        InterferencesVolume volume = VolumeManager.instance.stack.GetComponent<InterferencesVolume>();

        if (material == null || (volume != null && volume.IsActive() == false) || volume.intensity.value <= 0.0f)
          return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        if (resourceData.isActiveTargetBackBuffer == true)
          return;

        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        if (cameraData.camera.cameraType == CameraType.SceneView && volume.affectSceneView.value == false || cameraData.postProcessEnabled == false)
          return;

        TextureHandle source = resourceData.activeColorTexture;
        TextureHandle destination = renderGraph.CreateTexture(source.GetDescriptor(renderGraph));

        UpdateVolume(volume);

        RenderGraphUtils.BlitMaterialParameters pass = new(source, destination, material, 0);
        renderGraph.AddBlitPass(pass, $"{Constants.Asset.AssemblyName}.Pass");

        resourceData.cameraColor = destination;
      }
    }
  }
}
