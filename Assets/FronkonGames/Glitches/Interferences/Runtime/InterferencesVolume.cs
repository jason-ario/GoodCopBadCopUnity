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
using UnityEngine.Rendering;

namespace FronkonGames.Glitches.Interferences
{
  /// <summary> Interferences Volume. </summary>
  [Serializable, VolumeComponentMenu("Fronkon Games/Glitches/Interferences")]
  public sealed class InterferencesVolume : VolumeComponent, IPostProcessComponent
  {
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    #region Common settings.

    /// <summary> Controls the intensity of the effect [0, 1]. Default 1. </summary>
    /// <remarks> An effect with Intensity equal to 0 will not be executed. </remarks>
    [FloatSliderWithReset(1.0f, 0.0f, 1.0f, "Controls the intensity of the effect [0, 1]. Default 1.")]
    public FloatSliderParameterLinear intensity = new(1.0f, 0.0f, 1.0f);

    #endregion
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    #region Interferences settings.

    /// <summary> Color blend operation. Default Solid. </summary>
    [EnumDropdown((int)ColorBlends.Solid, "Color blend operation. Default Solid.")]
    public EnumParameterNoInterpolation<ColorBlends> blend = new(ColorBlends.Solid);

    /// <summary> Interference size [0, 10]. Default 1. </summary>
    [FloatSliderWithReset(1.0f, 0.0f, 10.0f, "Interference size [0, 10]. Default 1.")]
    public FloatSliderParameterNoInterpolation offset = new(1.0f, 0.0f, 10.0f);

    /// <summary> Distortion [0, 2]. Default 0.25. </summary>
    [FloatSliderWithReset(0.25f, 0.0f, 2.0f, "Distortion [0, 2]. Default 0.25.")]
    public FloatSliderParameterNoInterpolation distortion = new(0.25f, 0.0f, 2.0f);

    /// <summary> Distortion speed [0, 100]. Default 10. </summary>
    [FloatSliderWithReset(10.0f, 0.0f, 100.0f, "Distortion speed [0, 100]. Default 10.")]
    public FloatSliderParameterNoInterpolation distortionSpeed = new(10.0f, 0.0f, 100.0f);

    /// <summary> Distortion density [0, 10]. Default 2. </summary>
    [FloatSliderWithReset(2.0f, 0.0f, 10.0f, "Distortion density [0, 10]. Default 2.")]
    public FloatSliderParameterNoInterpolation distortionDensity = new(2.0f, 0.0f, 10.0f);

    /// <summary> Distortion amplitude [0, 5]. Default 0.15. </summary>
    [FloatSliderWithReset(0.15f, 0.0f, 5.0f, "Distortion amplitude [0, 5]. Default 0.15.")]
    public FloatSliderParameterNoInterpolation distortionAmplitude = new(0.15f, 0.0f, 5.0f);

    /// <summary> Distortion frequency [0, 10]. Default 0.3. </summary>
    [FloatSliderWithReset(0.3f, 0.0f, 10.0f, "Distortion frequency [0, 10]. Default 0.3.")]
    public FloatSliderParameterNoInterpolation distortionFrequency = new(0.3f, 0.0f, 10.0f);

    /// <summary> Scanlines [0, 1]. Default 0.75. </summary>
    [FloatSliderWithReset(0.75f, 0.0f, 1.0f, "Scanlines [0, 1]. Default 0.75.")]
    public FloatSliderParameterNoInterpolation scanlines = new(0.75f, 0.0f, 1.0f);

    /// <summary> Scanlines density [0, 1]. Default 0.25. </summary>
    [FloatSliderWithReset(0.25f, 0.0f, 1.0f, "Scanlines density [0, 1]. Default 0.25.")]
    public FloatSliderParameterNoInterpolation scanlinesDensity = new(0.25f, 0.0f, 1.0f);

    /// <summary> Scanlines opacity [0, 1]. Default 0.5. </summary>
    [FloatSliderWithReset(0.5f, 0.0f, 1.0f, "Scanlines opacity [0, 1]. Default 0.5.")]
    public FloatSliderParameterNoInterpolation scanlinesOpacity = new(0.5f, 0.0f, 1.0f);

    #endregion
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    #region Color settings.

    /// <summary> Brightness [-1, 1]. Default 0. </summary>
    [FloatSliderWithReset(0.0f, -1.0f, 1.0f, "Brightness [-1, 1]. Default 0.")]
    public FloatSliderParameterNoInterpolation brightness = new(0.0f, -1.0f, 1.0f);

    /// <summary> Contrast [0, 10]. Default 1. </summary>
    [FloatSliderWithReset(1.0f, 0.0f, 10.0f, "Contrast [0, 10]. Default 1.")]
    public FloatSliderParameterNoInterpolation contrast = new(1.0f, 0.0f, 10.0f);

    /// <summary> Gamma [0.1, 10]. Default 1. </summary>
    [FloatSliderWithReset(1.0f, 0.1f, 10.0f, "Gamma [0.1, 10]. Default 1.")]
    public FloatSliderParameterNoInterpolation gamma = new(1.0f, 0.1f, 10.0f);

    /// <summary> The color wheel [0, 1]. Default 0. </summary>
    [FloatSliderWithReset(0.0f, 0.0f, 1.0f, "The color wheel [0, 1]. Default 0.")]
    public FloatSliderParameterNoInterpolation hue = new(0.0f, 0.0f, 1.0f);

    /// <summary> Intensity of a colors [0, 2]. Default 1. </summary>
    [FloatSliderWithReset(1.0f, 0.0f, 2.0f, "Intensity of a colors [0, 2]. Default 1.")]
    public FloatSliderParameterNoInterpolation saturation = new(1.0f, 0.0f, 2.0f);

    #endregion
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    #region Advanced settings.
    
    /// <summary> Does it affect the Scene View? </summary>
    [ToggleWithReset(false, "Does it affect the Scene View?")]
    public BoolParameterNoInterpolation affectSceneView = new(false);

    /// <summary> Use scaled time. </summary>
    [ToggleWithReset(false, "Use scaled time.")]
    public BoolParameterNoInterpolation useScaledTime = new(true);

    #endregion
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary> Reset to default values. </summary> 
    public void Reset()
    {
      intensity.value = 1.0f;

      blend.value = ColorBlends.Solid;
      offset.value = 1.0f;
      distortion.value = 0.25f;
      distortionSpeed.value = 10.0f;
      distortionDensity.value = 2.0f;
      distortionAmplitude.value = 0.15f;
      distortionFrequency.value = 0.3f;
      scanlines.value = 0.75f;
      scanlinesDensity.value = 0.25f;
      scanlinesOpacity.value = 0.5f;

      brightness.value = 0.0f;
      contrast.value = 1.0f;
      gamma.value = 1.0f;
      hue.value = 0.0f;
      saturation.value = 1.0f;

      affectSceneView.value = false;
      useScaledTime.value = true;
    }

    /// <summary> Is the effect active? </summary>
    public bool IsActive() => intensity.overrideState == true && intensity.value > 0.0f;

    /// <summary> Is the effect tile compatible? </summary>
    public bool IsTileCompatible() => false;
  }
}