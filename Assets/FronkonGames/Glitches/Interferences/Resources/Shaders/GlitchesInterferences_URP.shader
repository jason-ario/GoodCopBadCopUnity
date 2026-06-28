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
Shader "Hidden/Fronkon Games/Glitches/Interferences URP"
{
  Properties
  {
    _MainTex("Main Texture", 2D) = "white" {}
  }

  SubShader
  {
    Tags
    {
      "RenderType" = "Opaque"
      "RenderPipeline" = "UniversalPipeline"
    }
    LOD 100
    ZTest Always ZWrite Off Cull Off

    Pass
    {
      Name "Fronkon Games Glitches Interferences Pass"

      HLSLPROGRAM
      #pragma vertex GlitchesVert
      #pragma fragment GlitchesFrag
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma exclude_renderers d3d9 d3d11_9x ps3 flash
      #pragma multi_compile_instancing
      #pragma multi_compile _ STEREO_INSTANCING_ON
      #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_MULTIVIEW_ON

      #include "Glitches.hlsl"
      #include "ColorBlend.hlsl"

      int _Blend;
      float _Offset;
      float _Distortion;
      float _DistortionSpeed;
      float _DistortionAmplitude;
      float _DistortionFrequency;
      float _DistortionDensity;
      float _Scanlines;
      float _ScanlinesDensity;
      float _ScanlinesOpacity;

      half4 GlitchesFrag(GlitchesVaryings input) : SV_Target
      {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord).xy;
        const float2 screenCoord = uv * _ScreenParams.xy;

        const half4 color = SAMPLE_MAIN(uv);
        half4 pixel = color;

        float rnd = Rand(_EffectTime.yy) * 2.0;

        float noise = max(0.0, snoise(float2(_EffectTime.y, uv.y * _DistortionFrequency)) - 0.3) * (1.0 / 0.7);
        noise += (snoise(float2(_EffectTime.y * _DistortionSpeed, uv.y * _DistortionDensity)) - 0.5) * _DistortionAmplitude;
        noise *= noise;

        uv.x -= noise * _Distortion;

        half3 interfarence = pixel.rgb;
        UNITY_BRANCH
        if (mod(screenCoord.y, rnd) > rnd / 8.0)
        {
          float of = sin(uv.y / rnd * _EffectTime.y) * rnd * 2.0;
          uv = float2(uv.x + of * TEXEL_SIZE.x, uv.y + of * 0.5 * TEXEL_SIZE.y);
          pixel.rgb = SAMPLE_MAIN(uv).rgb;

          float2 offset = of * rnd * float2(5.0, 0.0) * _Offset * TEXEL_SIZE.xy;
          interfarence.r = SAMPLE_MAIN(uv + offset).r;
          interfarence.g = pixel.g;
          interfarence.b = SAMPLE_MAIN(uv - offset).b;
        }
        else
        {
          uv = float2(uv.x - rnd * 4.0 * TEXEL_SIZE.x, uv.y + rnd * 0.5 * TEXEL_SIZE.y);
          pixel.rgb = SAMPLE_MAIN(uv).rgb;

          float2 offset = rnd * float2(50.0, 0.0) * _Offset * TEXEL_SIZE.xy;
          interfarence.r = SAMPLE_MAIN(uv + offset).r;
          interfarence.g = pixel.g;
          interfarence.b = SAMPLE_MAIN(uv - offset).b;
        }
        
        pixel.rgb = ColorBlend(_Blend, pixel.rgb, interfarence);

        UNITY_BRANCH
        if (floor(mod(screenCoord.y * _ScanlinesDensity, 2.0)) == 0.0)
          pixel.rgb *= lerp(1.0, 1.0 - (_ScanlinesOpacity * noise), _Scanlines);

        pixel.rgb = ColorAdjust(pixel.rgb, _Contrast, _Brightness, _Hue, _Gamma, _Saturation);

        return lerp(color, pixel, _Intensity);
      }

      ENDHLSL
    }
  }
  
  FallBack "Diffuse"
}
