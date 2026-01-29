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
Shader "Hidden/Fronkon Games/Retro/Noir URP"
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
      Name "Fronkon Games Retro Noir: Pass 0"

      HLSLPROGRAM
      #include "Retro.hlsl"
      
      #pragma vertex RetroVert
      #pragma fragment RetroFrag
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma exclude_renderers d3d9 d3d11_9x ps3 flash
      #pragma multi_compile _ _USE_DRAW_PROCEDURAL
      #pragma multi_compile _ DITHER_ON DOTSCREEN_ON HALFTONE_ON LINES_ON
      
      float _NoirIntensity;

      #include "ColorBlend.hlsl"
      #include "Dither.hlsl"
      #include "DotScreen.hlsl"
      #include "Halftone.hlsl"
      #include "Lines.hlsl"
      #include_with_pragmas "ChromaticAberration.hlsl"
      #include_with_pragmas "DuoTone.hlsl"
      #include_with_pragmas "Sepia.hlsl"
      #include_with_pragmas "FilmGrain.hlsl"
      #include_with_pragmas "Blotches.hlsl"
      #include_with_pragmas "Vignette.hlsl"
      #include_with_pragmas "Scratches.hlsl"

      half4 RetroFrag(RetroVaryings input) : SV_Target
      {
        const float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord).xy;
        const float3 color = SAMPLE_MAIN(uv).rgb;
        float3 pixel = color;
        
        #if defined(DITHER_ON)  
        pixel = Dither(pixel, uv);
        #elif defined(DOTSCREEN_ON)
        pixel = DotScreen(pixel, uv);
        #elif defined(HALFTONE_ON)
        pixel = Halftone(pixel, uv);
        #elif defined(LINES_ON)
        pixel *= Lines(pixel, uv);
        #endif

        pixel = DuoTone(pixel, uv);

        pixel = Sepia(pixel, uv);

        pixel = FilmGrain(pixel, uv);
        
        #if !defined(CHROMATIC_ABERRATION_ON)
        pixel = Blotches(pixel, uv);

        pixel = Scratches(pixel, uv);
        
        pixel = Vignette(pixel, uv);

        pixel = ColorAdjust(pixel);
        #endif

        #if 0
        pixel = PixelDemo(color, pixel, uv);
        #endif

        return half4(lerp(color, pixel, _Intensity), 1.0);
      }
      ENDHLSL
    }

    Pass
    {
      Name "Fronkon Games Retro Noir: Pass 1"

      HLSLPROGRAM
      #include "Retro.hlsl"

      #pragma vertex RetroVert
      #pragma fragment RetroFrag
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma exclude_renderers d3d9 d3d11_9x ps3 flash
      #pragma multi_compile _ _USE_DRAW_PROCEDURAL

      #include_with_pragmas "ChromaticAberration.hlsl"
      #include_with_pragmas "FilmGrain.hlsl"
      #include_with_pragmas "Blotches.hlsl"
      #include_with_pragmas "Vignette.hlsl"
      #include_with_pragmas "Scratches.hlsl"

      half4 RetroFrag(RetroVaryings input) : SV_Target
      {
        const float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord).xy;
        const float3 color = SAMPLE_MAIN(uv).rgb;
        float3 pixel = color;

        pixel = ChromaticAberration(pixel, uv);

        pixel = Blotches(pixel, uv);

        pixel = Scratches(pixel, uv);
        
        pixel = Vignette(pixel, uv);
        
        #if 1
        pixel = PixelDemo(color, pixel, uv);
        #endif

        pixel = ColorAdjust(pixel);

        return half4(lerp(color, pixel, _Intensity), 1.0);
      }
      ENDHLSL
    }    
  }
  
  FallBack "Diffuse"
}
