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

#pragma multi_compile _ VIGNETTE_ON

#if defined(VIGNETTE_ON)
float _VignetteIntensity;
float _VignetteSmoothness;
float _VignetteSize;
float4 _VignetteColor;
float _VignetteTimeVariation;

inline float3 Vignette(float3 pixel, float2 uv)
{
  float uvDistance = distance(uv, float2(0.5, 0.5)) * 2.0;
  float lower_bound = _VignetteSize;
  float upper_bound = _VignetteSize + _VignetteSmoothness;
  upper_bound = min(upper_bound, 1.5);

  float vignetteFactor = smoothstep(lower_bound, upper_bound, uvDistance);

  float3 vignette = lerp(pixel, _VignetteColor.rgb, vignetteFactor * _VignetteIntensity * _VignetteColor.a);

  if (_VignetteTimeVariation > 0.0)
    vignette *= lerp(0.9, 1.0, sin(_EffectTime.y * _VignetteTimeVariation));

  return lerp(pixel, vignette, _VignetteIntensity);
}
#else
inline float3 Vignette(float3 pixel, float2 uv)
{
  return pixel;
}
#endif