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

#pragma multi_compile _ BLOTCHES_ON

#if defined(BLOTCHES_ON)
float _BlotchesIntensity;
float _BlotchesSpeed;

float RandomBlotch(float2 uv, float seed)
{
	float x = Rand(seed);
	float y = Rand(seed + 1.0);
	float s = 0.01 * Rand(seed + 2.0);
	
	float2 p = float2(x, y) - uv;
	p.x *= _ScreenParams.x / _ScreenParams.y;
	float a = atan2(p.y, p.x);
	float v = 1.0;
	float ss = s * s * (sin(6.2831 * a * x) * 0.1 + 1.0);
	
  v = dot(p, p) < ss ? 0.2 : pow(abs(dot(p, p) - ss), 1.0 / 16.0);
	
  return lerp(0.3 + 0.2 * (1.0 - (s / 0.02)), 1.0, v);
}

inline float3 Blotches(float3 pixel, float2 uv)
{
  float speed = float(int(_EffectTime.y * _BlotchesSpeed));
  int s = int(max(8.0 * Rand(speed + 18.0) - 2.0, 0.0));

  float3 blotches = pixel;
  if (0 < s) blotches *= RandomBlotch(uv, speed + 6.0 + 19.0 * 0.0);
  if (1 < s) blotches *= RandomBlotch(uv, speed + 6.0 + 19.0 * 1.0);
  if (2 < s) blotches *= RandomBlotch(uv, speed + 6.0 + 19.0 * 2.0);
  if (3 < s) blotches *= RandomBlotch(uv, speed + 6.0 + 19.0 * 3.0);
  if (4 < s) blotches *= RandomBlotch(uv, speed + 6.0 + 19.0 * 4.0);
  if (5 < s) blotches *= RandomBlotch(uv, speed + 6.0 + 19.0 * 5.0);

  return lerp(pixel, pixel * blotches, _BlotchesIntensity);
}

#else

inline float3 Blotches(float3 pixel, float2 uv)
{
  return pixel;
}

#endif