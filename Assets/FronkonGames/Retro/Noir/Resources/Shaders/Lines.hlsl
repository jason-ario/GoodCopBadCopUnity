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

#if defined(LINES_ON)

int _LinesCount;
float _LinesGranularity;
float _LinesThreshold;
int _LinesColorBlend;

inline float3 Lines(float3 pixel, float2 uv)
{
  float3 lines = 1.0;
  float2 coord = uv * _ScreenParams.xy;

  float s = 0.5 + 2.0 * _LinesThreshold;
  float b = dot(SAMPLE_MAIN(uv).rgb * s, (float3)0.333);
  float b0 = b;
  b = floor(b * float(_LinesCount));
  float d = 0.3 - 0.25 + _LinesGranularity;

  for (int i = 0; i < _LinesCount; ++i)
  {
    if (float(_LinesCount - i - 1) < b)
      break;

    float ang = -(float)i / (float)_LinesCount * PI;
    float2 dir = float2(cos(ang), sin(ang));

    float s = sin(dot(dir, coord) * d / (_ScreenParams.x / _ScreenParams.y));
    lines -= 0.7 * exp(-s * s * 10.0);
    d *= 1.2;
  }

	return lerp(pixel, ColorBlend(_LinesColorBlend, pixel, lines), _NoirIntensity);
}

#endif
