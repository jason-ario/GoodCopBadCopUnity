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
using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FronkonGames.Retro.Noir
{
  /// <summary> The method of the noir effect. </summary>
  public enum NoirMethod
  {
    /// <summary> Bayer dither 8x8. </summary>
    Dither,

    /// <summary> Dot screen. </summary>
    DotScreen,

    /// <summary> Halftone. </summary>
    Halftone,

    /// <summary> Lines. </summary>
    Lines,
  }

  ///------------------------------------------------------------------------------------------------------------------
  /// <summary> Settings. </summary>
  /// <remarks> Only available for Universal Render Pipeline. </remarks>
  ///------------------------------------------------------------------------------------------------------------------
  public sealed partial class Noir
  {
    /// <summary> Settings. </summary>
    [Serializable]
    public sealed class Settings
    {
      #region Common settings.
      /// <summary> Controls the intensity of the effect [0, 1]. Default 1. </summary>
      /// <remarks> An effect with Intensity equal to 0 will not be executed. </remarks>
      public float intensity = 1.0f;
      #endregion

      /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
      #region Noir settings.

      /// <summary> The method of the noir effect, default Dither8x8. </summary>
      public NoirMethod method = NoirMethod.Dither;

      /// <summary> The intensity of the noir effect, default 1.0 [0.0 - 1.0]. </summary>
      public float noirIntensity = 1.0f;

      /// <summary> The spread of the dither, default 1.0 [0.0 - 1.0]. </summary>
      /// <remarks> Only available for Bayer method. </remarks>
      public float ditherSpread = 1.0f;

      /// <summary> The density of the dither, default 1.0 [0.0 - 1.0]. </summary>
      /// <remarks> Only available for Bayer method. </remarks>
      public float ditherDensity = 1.0f;

      /// <summary> The color blend of the dither, default Solid. </summary>
      /// <remarks> Only available for Bayer method. </remarks>
      public ColorBlends ditherColorBlend = ColorBlends.Solid;

      /// <summary> The size of the dot screen grid, default 92 [8 - 256]. </summary>
      /// <remarks> Only available for DotScreen method. </remarks>
      public int dotScreenGridSize = 92;

      /// <summary> The luminance gain of the dot screen, default 0.3 [0.0 - 1.0]. </summary>
      /// <remarks> Only available for DotScreen method. </remarks>
      public float dotScreenLuminanceGain = 0.3f;

      /// <summary> The color blend of the dot screen, default Multiply. </summary>
      /// <remarks> Only available for DotScreen method. </remarks>
      public ColorBlends dotScreenColorBlend = ColorBlends.Multiply;

      /// <summary> The size of the halftone, default 2.0 [0.0 - 10.0]. </summary>
      /// <remarks> Only available for Halftone method. </remarks>
      public float halftoneSize = 2.0f;

      /// <summary> The angle of the halftone, default 30.0 [0.0 - 360.0]. </summary>
      /// <remarks> Only available for Halftone method. </remarks>
      public float halftoneAngle = 30.0f;

      /// <summary> The threshold of the halftone, default 0.75 [0.0 - 1.0]. </summary>
      /// <remarks> Only available for Halftone method. </remarks>
      public float halftoneThreshold = 0.75f;

      /// <summary> The count of the lines, default 4 [1 - 10]. </summary>
      /// <remarks> Only available for Lines method. </remarks>
      public int linesCount = 4;

      /// <summary> The granularity of the lines, default 0.25 [0.0 - 1.0]. </summary>
      /// <remarks> Only available for Lines method. </remarks>
      public float linesGranularity = 0.25f;

      /// <summary> The threshold of the lines, default 0.5 [0.0 - 1.0]. </summary>
      /// <remarks> Only available for Lines method. </remarks>
      public float linesThreshold = 0.5f;

      /// <summary> The color blend of the lines, default Solid. </summary>
      /// <remarks> Only available for Lines method. </remarks>
      public ColorBlends linesColorBlend = ColorBlends.Solid;

      /// <summary> The color blend of the halftone, default Solid. </summary>
      /// <remarks> Only available for Halftone method. </remarks>
      public ColorBlends halftoneColorBlend = ColorBlends.Solid;

      /// <summary> The intensity of the duo tone, default 1.0 [0.0 - 1.0]. </summary>
      public float duoToneIntensity = 1.0f;

      /// <summary> The color of the brightness, default white. </summary>
      public Color duoToneBrightnessColor = Color.white;

      /// <summary> The color of the darkness, default DefaultDuoToneDarknessColor. </summary>
      public Color duoToneDarknessColor = DefaultDuoToneDarknessColor;

      /// <summary> The threshold between black and white, default 0.5 [0.0 - 1.0]. </summary>
      public float duoToneThreshold = 0.5f;

      /// <summary> The minimum range of the luminance, default 0.0 [0.0 - 1.0]. </summary>
      public float duoToneLuminanceMinRange = 0.0f;

      /// <summary> The maximum range of the luminance, default 1.0 [0.0 - 1.0]. </summary>
      public float duoToneLuminanceMaxRange = 1.0f;

      /// <summary> Smooth transition between black and white, default 0.5 [0.0 - 1.0]. </summary>
      public float duoToneSoftness = 0.5f;

      /// <summary> The amount of light, default 1.0 [0.0 - 5.0]. </summary>
      public float duoToneExposure = 1.0f;

      /// <summary> The intensity of the emboss dark, default 0.05 [0.0 - 1.0]. </summary>
      public float duoToneEmbossDark = 0.05f;

      /// <summary> The color blend of the duo tone, default Solid. </summary>
      public ColorBlends duoToneColorBlend = ColorBlends.Solid;

      /// <summary> Controls the intensity of the Sepia effect [0, 1]. Default 0. </summary>
      public float sepiaIntensity = 0.0f;

      /// <summary> Controls the intensity of the Chromatic Aberration effect [0, 1]. Default 0. </summary>
      /// <remarks>
      /// When activated, one more pass is added to the shader.
      /// It can produce a moiré effect; apply film grain to mitigate it.
      /// </remarks>
      public float chromaticAberrationIntensity = 0.0f;

      /// <summary> Controls the intensity of the Blotches effect [0, 1]. Default 0. </summary>
      public float blotchesIntensity = 0.0f;

      /// <summary> Speed of the blotches effect [0, 33]. Default 10. </summary>
      public float blotchesSpeed = 10.0f;

      /// <summary> Intensity of the vignette effect [0, 1]. Default 1.0. </summary>
      public float vignetteIntensity = 1.0f;

      /// <summary> Size of the vignette's clear central area [0, 2]. Default 1. </summary>
      public float vignetteSize = 1.0f;

      /// <summary> Intensity of the film grain effect [0, 1]. Default 0. </summary>
      public float filmGrainIntensity = 0.0f;

      /// <summary> Speed of the film grain effect [0, 1]. Default 0.5. </summary>
      public float filmGrainSpeed = 0.5f;

      /// <summary> Color of the vignette. Alpha component of color can influence overall vignette visibility. Default black. </summary>
      public Color vignetteColor = Color.black;

      /// <summary> Smoothness of the vignette falloff [0.01, 1]. Default 0.5. </summary>
      public float vignetteSmoothness = 0.5f;

      /// <summary> Time variation of the vignette effect [0, 1]. Default 0. </summary>
      public float vignetteTimeVariation = 0.0f;

      /// <summary> Intensity of the scratches effect [0, 1]. Default 0. </summary>
      public float scratchesIntensity = 0.0f;

      /// <summary> Speed of the scratches effect [0, 1]. Default 0.5. </summary>
      public float scratchesSpeed = 0.5f;

      public static Color DefaultDuoToneDarknessColor = new Color(0.05f, 0.05f, 0.05f, 1.0f);
      #endregion
      /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
      
      /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
      #region Color settings.

      /// <summary> Brightness [-1, 1]. Default 0. </summary>
      public float brightness = 0.0f;

      /// <summary> Contrast [0, 10]. Default 1. </summary>
      public float contrast = 1.0f;

      /// <summary> Gamma [0.1, 10]. Default 1. </summary>
      public float gamma = 1.0f;

      /// <summary> The color wheel [0, 1]. Default 0. </summary>
      public float hue = 0.0f;

      /// <summary> Intensity of a colors [0, 2]. Default 1. </summary>
      public float saturation = 1.0f;

      #endregion
      /////////////////////////////////////////////////////////////////////////////////////////////////////////////////

      /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
      #region Advanced settings.
      /// <summary> Does it affect the Scene View? </summary>
      public bool affectSceneView = false;

#if !UNITY_6000_0_OR_NEWER
      /// <summary> Enable render pass profiling. </summary>
      public bool enableProfiling = false;

      /// <summary> Filter mode. Default Bilinear. </summary>
      public FilterMode filterMode = FilterMode.Bilinear;
#endif

      /// <summary> Render pass injection. Default BeforeRenderingPostProcessing. </summary>
      public RenderPassEvent whenToInsert = RenderPassEvent.BeforeRenderingPostProcessing;

      /// <summary> Use scaled time. </summary>
      public bool useScaledTime = true;
      #endregion
      /////////////////////////////////////////////////////////////////////////////////////////////////////////////////

      /// <summary> Reset to default values. </summary>
      public void ResetDefaultValues()
      {
        intensity = 1.0f;

        method = NoirMethod.Dither;
        noirIntensity = 1.0f;
        
        ditherSpread = 1.0f;
        ditherDensity = 1.0f;
        ditherColorBlend = ColorBlends.Solid;

        dotScreenGridSize = 92;
        dotScreenLuminanceGain = 0.3f;
        dotScreenColorBlend = ColorBlends.Multiply;

        halftoneSize = 2.0f;
        halftoneAngle = 30.0f;
        halftoneThreshold = 0.75f;
        halftoneColorBlend = ColorBlends.Solid;

        linesCount = 4;
        linesGranularity = 0.25f;
        linesThreshold = 0.5f;
        linesColorBlend = ColorBlends.Solid;

        duoToneIntensity = 1.0f;
        duoToneBrightnessColor = Color.white;
        duoToneDarknessColor = DefaultDuoToneDarknessColor;
        duoToneThreshold = 0.5f;   
        duoToneLuminanceMinRange = 0.0f;
        duoToneLuminanceMaxRange = 1.0f;
        duoToneSoftness = 0.5f;
        duoToneExposure = 1.0f;
        duoToneEmbossDark = 0.05f;
        duoToneColorBlend = ColorBlends.Solid;

        sepiaIntensity = 0.0f;
        chromaticAberrationIntensity = 0.0f;

        filmGrainIntensity = 0.0f;
        filmGrainSpeed = 0.5f;

        blotchesIntensity = 0.0f;
        blotchesSpeed = 10.0f;

        scratchesIntensity = 0.0f;
        scratchesSpeed = 0.5f;

        vignetteColor = Color.black;
        vignetteIntensity = 1.0f;
        vignetteSize = 1.0f;
        vignetteSmoothness = 0.5f;
        vignetteTimeVariation = 0.0f;

        brightness = 0.0f;
        contrast = 1.0f;
        gamma = 1.0f;
        hue = 0.0f;
        saturation = 1.0f;

        affectSceneView = false;
#if !UNITY_6000_0_OR_NEWER
        enableProfiling = false;
        filterMode = FilterMode.Bilinear;
#endif
        whenToInsert = RenderPassEvent.BeforeRenderingPostProcessing;
        useScaledTime = true;
      }
    }    
  }
}
