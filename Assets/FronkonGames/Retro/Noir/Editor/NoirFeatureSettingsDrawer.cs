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
using UnityEditor;
using static FronkonGames.Retro.Noir.Inspector;

namespace FronkonGames.Retro.Noir.Editor
{
  /// <summary> Retro Noir inspector. </summary>
  [CustomPropertyDrawer(typeof(Noir.Settings))]
  public class NoirFeatureSettingsDrawer : Drawer
  {
    private Noir.Settings settings;

    protected override void ResetValues() => settings?.ResetDefaultValues();

    protected override void InspectorGUI()
    {
      settings ??= GetSettings<Noir.Settings>();

      /////////////////////////////////////////////////
      // Common.
      /////////////////////////////////////////////////
      settings.intensity = Slider("Intensity", "Controls the intensity of the effect [0, 1]. Default 0.", settings.intensity, 0.0f, 1.0f, 1.0f);

      /////////////////////////////////////////////////
      // Noir.
      /////////////////////////////////////////////////
      Separator();

      settings.noirIntensity = Slider("Noir", "The intensity of the noir effect, default 1.0 [0.0 - 1.0].", settings.noirIntensity, 0.0f, 1.0f, 1.0f);
      IndentLevel++;
      settings.method = (NoirMethod)EnumPopup("Method", "The method of the noir effect, default Dither.", settings.method, NoirMethod.Dither);
      switch (settings.method)
      {
        case NoirMethod.Dither:
          settings.ditherSpread = Slider("Spread", "The spread of the dither, default 1.0 [0.0 - 1.0].", settings.ditherSpread, 0.0f, 1.0f, 1.0f);
          settings.ditherDensity = Slider("Density", "The density of the dither, default 1.0 [0.0 - 1.0].", settings.ditherDensity, 0.0f, 1.0f, 1.0f);
          settings.ditherColorBlend = (ColorBlends)EnumPopup("Blend", "The color blend operation, default Solid.", settings.ditherColorBlend, ColorBlends.Solid);
          break;
        case NoirMethod.DotScreen:
          settings.dotScreenGridSize = Slider("Size", "The size of the dot screen grid, default 92 [8 - 256].", settings.dotScreenGridSize, 8, 256, 92);
          settings.dotScreenLuminanceGain = Slider("Luminance gain", "The luminance gain of the dot screen, default 0.3 [0.0 - 1.0].", settings.dotScreenLuminanceGain, 0.0f, 1.0f, 0.3f);
          settings.dotScreenColorBlend = (ColorBlends)EnumPopup("Blend", "The color blend operation, default Multiply.", settings.dotScreenColorBlend, ColorBlends.Multiply);
          break;
        case NoirMethod.Halftone:
          settings.halftoneSize = Slider("Size", "The size of the halftone, default 2.0 [0.0 - 10.0].", settings.halftoneSize, 0.0f, 10.0f, 2.0f);
          settings.halftoneAngle = Slider("Angle", "The angle of the halftone, default 30.0 [0.0 - 360.0].", settings.halftoneAngle, 0.0f, 360.0f, 30.0f);
          settings.halftoneThreshold = Slider("Threshold", "The threshold of the halftone, default 0.75 [0.0 - 1.0].", settings.halftoneThreshold, 0.0f, 1.0f, 0.75f);
          settings.halftoneColorBlend = (ColorBlends)EnumPopup("Blend", "The color blend operation, default Solid.", settings.halftoneColorBlend, ColorBlends.Solid);
          break;
        case NoirMethod.Lines:
          settings.linesCount = Slider("Count", "The count of the lines, default 4 [1 - 10].", settings.linesCount, 1, 10, 4);
          settings.linesGranularity = Slider("Granularity", "The granularity of the lines, default 0.25 [0.0 - 1.0].", settings.linesGranularity, 0.0f, 1.0f, 0.25f);
          settings.linesThreshold = Slider("Threshold", "The threshold of the lines, default 0.5 [0.0 - 1.0].", settings.linesThreshold, 0.0f, 1.0f, 0.5f);
          settings.linesColorBlend = (ColorBlends)EnumPopup("Blend", "The color blend operation, default Solid.", settings.linesColorBlend, ColorBlends.Solid);
          break;
      }
      IndentLevel--;

      settings.duoToneIntensity = Slider("Duo Tone", "The intensity of the duo tone, default 1.0 [0.0 - 1.0].", settings.duoToneIntensity, 0.0f, 1.0f, 1.0f);
      IndentLevel++;
      settings.duoToneBrightnessColor = ColorField("Brightness color", "The color of the brightness, default white.", settings.duoToneBrightnessColor, Color.white);
      settings.duoToneDarknessColor = ColorField("Darkness color", "The color of the darkness, default DefaultDuoToneDarknessColor.", settings.duoToneDarknessColor, Noir.Settings.DefaultDuoToneDarknessColor);
      MinMaxSlider("Remap luminance", "Luminance range used to change colors.", ref settings.duoToneLuminanceMinRange, ref settings.duoToneLuminanceMaxRange, 0.0f, 1.0f, 0.0f, 1.0f);
      settings.duoToneThreshold = Slider("Threshold", "The threshold between black and white, default 0.5 [0.0 - 1.0].", settings.duoToneThreshold, 0.0f, 1.0f, 0.5f);
      settings.duoToneSoftness = Slider("Softness", "Smooth transition between black and white, default 0.5 [0.0 - 1.0].", settings.duoToneSoftness, 0.0f, 1.0f, 0.5f);
      settings.duoToneExposure = Slider("Exposure", "The amount of light, default 5.0 [0.0 - 5.0].", settings.duoToneExposure, 0.0f, 5.0f, 1.0f);
      settings.duoToneEmbossDark = Slider("Emboss dark", "The intensity of the emboss dark, default 0.05 [0.0 - 1.0].", settings.duoToneEmbossDark, 0.0f, 1.0f, 0.05f);
      settings.duoToneColorBlend = (ColorBlends)EnumPopup("Blend", "The color blend operation, default Solid.", settings.duoToneColorBlend, ColorBlends.Solid);
      IndentLevel--;

      settings.sepiaIntensity = Slider("Sepia", "Controls the intensity of the Sepia effect [0, 1]. Default 0.", settings.sepiaIntensity, 0.0f, 1.0f, 0.0f);

      settings.chromaticAberrationIntensity = Slider("Chromatic Aberration", "Controls the intensity of the Chromatic Aberration effect [0, 1]. Default 0.", settings.chromaticAberrationIntensity, 0.0f, 1.0f, 0.0f);

      settings.filmGrainIntensity = Slider("Film Grain", "Controls the intensity of the Film Grain effect [0, 1]. Default 0.", settings.filmGrainIntensity, 0.0f, 1.0f, 0.0f);
      IndentLevel++;
      settings.filmGrainSpeed = Slider("Speed", "The speed of the Film Grain effect, default 0.5 [0.0 - 1.0].", settings.filmGrainSpeed, 0.0f, 1.0f, 0.5f);
      IndentLevel--;

      settings.vignetteIntensity = Slider("Vignette", "The amount of vignette, default 1.0 (no vignette) [0.0 - 1.0].", settings.vignetteIntensity, 0.0f, 1.0f, 1.0f);
      IndentLevel++;
      settings.vignetteSize = Slider("Size", "The size of the vignette, default 1.0 [0.0 - 2.0].", settings.vignetteSize, 0.0f, 2.0f, 1.0f);
      settings.vignetteSmoothness = Slider("Smoothness", "The smoothness of the vignette, default 0.5 [0.0 - 1.0].", settings.vignetteSmoothness, 0.0f, 1.0f, 0.5f);
      settings.vignetteTimeVariation = Slider("Time variation", "The time variation of the vignette, default 0.0 [0.0 - 1.0].", settings.vignetteTimeVariation, 0.0f, 1.0f, 0.0f);
      settings.vignetteColor = ColorField("Color", "The color of the vignette, default black.", settings.vignetteColor, Color.black);
      IndentLevel--;

      settings.blotchesIntensity = Slider("Blotches", "Controls the intensity of the Blotches effect [0, 1]. Default 0.", settings.blotchesIntensity, 0.0f, 1.0f, 0.0f);
      IndentLevel++;
      settings.blotchesSpeed = Slider("Speed", "The speed of the Blotches effect, default 10.0 [0.0 - 33.0].", settings.blotchesSpeed, 0.0f, 33.0f, 10.0f);
      IndentLevel--;

      settings.scratchesIntensity = Slider("Scratches", "The intensity of the scratches effect, default 0.0 [0.0 - 1.0].", settings.scratchesIntensity, 0.0f, 1.0f, 0.0f);
      IndentLevel++;
      settings.scratchesSpeed = Slider("Speed", "The speed of the Scratches effect, default 0.5 [0.0 - 1.0].", settings.scratchesSpeed, 0.0f, 1.0f, 0.5f);
      IndentLevel--;

      /////////////////////////////////////////////////
      // Color.
      /////////////////////////////////////////////////
      Separator();

      if (Foldout("Color") == true)
      {
        IndentLevel++;

        settings.brightness = Slider("Brightness", "Brightness [-1.0, 1.0]. Default 0.", settings.brightness, -1.0f, 1.0f, 0.0f);
        settings.contrast = Slider("Contrast", "Contrast [0.0, 10.0]. Default 1.", settings.contrast, 0.0f, 10.0f, 1.0f);
        settings.gamma = Slider("Gamma", "Gamma [0.1, 10.0]. Default 1.", settings.gamma, 0.01f, 10.0f, 1.0f);
        settings.hue = Slider("Hue", "The color wheel [0.0, 1.0]. Default 0.", settings.hue, 0.0f, 1.0f, 0.0f);
        settings.saturation = Slider("Saturation", "Intensity of a colors [0.0, 2.0]. Default 1.", settings.saturation, 0.0f, 2.0f, 1.0f);

        IndentLevel--;
      }

      /////////////////////////////////////////////////
      // Advanced.
      /////////////////////////////////////////////////
      Separator();

      if (Foldout("Advanced") == true)
      {
        IndentLevel++;

#if !UNITY_6000_0_OR_NEWER
        settings.filterMode = (FilterMode)EnumPopup("Filter mode", "Filter mode. Default Bilinear.", settings.filterMode, FilterMode.Bilinear);
#endif
        settings.affectSceneView = Toggle("Affect the Scene View?", "Does it affect the Scene View?", settings.affectSceneView);
        settings.whenToInsert = (UnityEngine.Rendering.Universal.RenderPassEvent)EnumPopup("RenderPass event",
          "Render pass injection. Default BeforeRenderingPostProcessing.",
          settings.whenToInsert,
          UnityEngine.Rendering.Universal.RenderPassEvent.BeforeRenderingPostProcessing);
        settings.useScaledTime = Toggle("Use scaled time", "Use scaled time.", settings.useScaledTime);
#if !UNITY_6000_0_OR_NEWER
        settings.enableProfiling = Toggle("Enable profiling", "Enable render pass profiling", settings.enableProfiling);
#endif

        IndentLevel--;
      }
    }
  }
}
