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

#pragma multi_compile _ CHROMATIC_ABERRATION_ON

#if defined(CHROMATIC_ABERRATION_ON)
float _ChromaticAberrationIntensity;

inline float3 ChromaticAberration(float3 pixel, float2 uv)
{
  float2 direction = (uv - 0.5) * _ChromaticAberrationIntensity * TEXEL_SIZE.xy;

  return float3(SAMPLE_MAIN(uv - direction * 0.25).r,
                pixel.g,
                SAMPLE_MAIN(uv + direction * 0.25).b);
}

#else

inline float3 ChromaticAberration(float3 pixel, float2 uv)
{
  return pixel;
}

#endif