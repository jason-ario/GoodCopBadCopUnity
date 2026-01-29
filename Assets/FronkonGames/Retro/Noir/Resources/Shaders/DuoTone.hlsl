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
#pragma once

#pragma multi_compile _ EMBOSS_DARK_ON

float _DuoToneIntensity;
float _DuoToneThreshold;
float3 _DuoToneBrightnessColor;
float3 _DuoToneDarknessColor;
float _DuoToneLuminanceMinRange;
float _DuoToneLuminanceMaxRange;
float _DuoToneSoftness;
float _DuoToneExposure;
int _DuoToneColorBlend;
float _DuoToneEmbossDark;

float3 DuoTone(float3 pixel, float2 uv)
{
  float3 tc = pixel * (exp2(pixel) * (float3)_DuoToneExposure);
  
  float luminance = RemapValue(Luminance(tc), _DuoToneLuminanceMinRange, _DuoToneLuminanceMaxRange, 0.0, 1.0);

  float3 duotone;
#if defined(EMBOSS_DARK_ON)
  if (luminance < _DuoToneEmbossDark)
  {
    float t1 = saturate(luminance / _DuoToneEmbossDark);
    duotone = lerp(float3(0.0, 0.0, 0.0), _DuoToneDarknessColor, t1);
  }
  else
  {
    float blend_factor_stage2 = smoothstep(_DuoToneThreshold - _DuoToneSoftness, 
                                           _DuoToneThreshold + _DuoToneSoftness, 
                                           luminance);
    duotone = lerp(_DuoToneDarknessColor, _DuoToneBrightnessColor, blend_factor_stage2);
  }
#else
  duotone = smoothstep(_DuoToneThreshold - _DuoToneSoftness, _DuoToneThreshold + _DuoToneSoftness, luminance);
  duotone = lerp(_DuoToneDarknessColor, _DuoToneBrightnessColor, duotone);
#endif

  return lerp(pixel, ColorBlend(_DuoToneColorBlend, pixel, duotone), _DuoToneIntensity);
}