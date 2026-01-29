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

#if defined(HALFTONE_ON)

float _HalftoneThreshold;
float _HalftoneSize;
float _HalftoneAngle;
int _HalftoneColorBlend;

inline float Added(float2 sh, float sa, float ca, float2 c, float d)
{
	return 0.5 + 0.25 * cos((sh.x * sa + sh.y * ca + c.x) * d) + 0.25 * cos((sh.x * ca - sh.y * sa + c.y) * d);
}

inline float3 Halftone(float3 pixel, float2 uv)
{
  float3 halftone = pixel;

	const float2 rotationCenter = float2(0.5, 0.5);
  uv.y *= _ScreenParams.y / _ScreenParams.x;
	float2 shift = uv - rotationCenter;

  halftone = float3(Added(shift, sin(_HalftoneAngle + 00.0), cos(_HalftoneAngle), rotationCenter, PI / _HalftoneSize * 680.0),
                    Added(shift, sin(_HalftoneAngle + 30.0), cos(_HalftoneAngle), rotationCenter, PI / _HalftoneSize * 680.0),
                    Added(shift, sin(_HalftoneAngle + 60.0), cos(_HalftoneAngle), rotationCenter, PI / _HalftoneSize * 680.0));

  halftone = float3((halftone.r * _HalftoneThreshold + pixel.r - _HalftoneThreshold) / (1.0 - _HalftoneThreshold),
                    (halftone.g * _HalftoneThreshold + pixel.g - _HalftoneThreshold) / (1.0 - _HalftoneThreshold),
                    (halftone.b * _HalftoneThreshold + pixel.b - _HalftoneThreshold) / (1.0 - _HalftoneThreshold));

	return lerp(pixel, ColorBlend(_HalftoneColorBlend, pixel, halftone), _NoirIntensity);
}

#endif
