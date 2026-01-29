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

#pragma multi_compile _ SCRATCHES_ON

#if defined(SCRATCHES_ON)
float _ScratchesIntensity;
float _ScratchesSpeed;

float RandomScratch(float2 uv, float seed)
{
	float b = 0.01 * Rand(seed);
	float a = Rand(seed + 1.0);
	float c = Rand(seed + 2.0) - 0.5;
	float mu = Rand(seed + 3.0);
	
	float l = 1.0;
	if (mu > 0.5)
		l = pow(abs(a * uv.x + b * uv.y + c ), 1.0 / 8.0);
	else
		l = 2.0 - pow(abs(a * uv.x + b * uv.y + c), 1.0 / 8.0);				
	
	return lerp(0.5, 1.0, l);
}

inline float3 Scratches(float3 pixel, float2 uv)
{
  float3 scratches = pixel;
  
  float t = _EffectTime.y * _ScratchesSpeed;
  
  int l = int(8.0 * Rand(t + 7.0));
  if (0 < l) scratches *= RandomScratch(uv, t + 6.0 + 17.0 * 0.0);
  if (1 < l) scratches *= RandomScratch(uv, t + 6.0 + 17.0 * 1.0);
  if (2 < l) scratches *= RandomScratch(uv, t + 6.0 + 17.0 * 2.0);		
  if (3 < l) scratches *= RandomScratch(uv, t + 6.0 + 17.0 * 3.0);
  if (4 < l) scratches *= RandomScratch(uv, t + 6.0 + 17.0 * 4.0);
  if (5 < l) scratches *= RandomScratch(uv, t + 6.0 + 17.0 * 5.0);
  if (6 < l) scratches *= RandomScratch(uv, t + 6.0 + 17.0 * 6.0);
  if (7 < l) scratches *= RandomScratch(uv, t + 6.0 + 17.0 * 7.0);

  return lerp(pixel, scratches, _ScratchesIntensity);
}

#else

inline float3 Scratches(float3 pixel, float2 uv)
{
  return pixel;
}

#endif