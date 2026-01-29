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

#if defined(DOTSCREEN_ON)

int _DotScreenGridSize;
int _DotScreenColorBlend;
float _DotScreenLuminanceGain;

inline float3 DotScreen(float3 pixel, float2 uv)
{
  float2 gv = frac(uv * _DotScreenGridSize);
  gv.x *= _ScreenParams.x / _ScreenParams.y;
  gv -= float2(1.0, 0.5);

  float3 average = 0.0;

  for (float y = -1.0; y < 1.0; y++)
    for (float x = -1.0; x < 1.0; x++)
      average += SAMPLE_MAIN(uv + (float2(x, y) / _DotScreenGridSize)).rgb;

  average /= 9.0;

  float radius = _DotScreenLuminanceGain + Luminance(average);
  float circle = length(gv) - radius;
  float3 noir = ColorBlend(_DotScreenColorBlend, pixel, smoothstep(radius * 0.1, radius * 0.1 - 0.1, circle));

  return lerp(pixel, noir, _NoirIntensity);
}

#endif
