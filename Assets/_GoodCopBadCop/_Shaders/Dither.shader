Shader "GoodCopBadCop/Dither"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Dither"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local DITHER_BAYER2 DITHER_BAYER4 DITHER_BAYER8
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // ----------------------------------------------------------------
            // Parameters set from C#
            // ----------------------------------------------------------------
            float  _DitherStrength;   // Threshold spread (0–1)
            float  _DitherBlend;      // Lerp between original and dithered result (0–1)
            float  _DitherScale;      // Pixel block size in screen pixels
            int    _DitherColorDepth; // Quantization steps per channel
            float  _DitherLumaOnly;   // 1 = luminance only

            // ----------------------------------------------------------------
            // Bayer matrices (values normalised 0..1)
            // ----------------------------------------------------------------
            static const float Bayer2[4] =
            {
                0.0/4.0, 2.0/4.0,
                3.0/4.0, 1.0/4.0
            };

            static const float Bayer4[16] =
            {
                 0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
                 3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
            };

            static const float Bayer8[64] =
            {
                 0.0/64.0,  32.0/64.0,   8.0/64.0,  40.0/64.0,   2.0/64.0,  34.0/64.0,  10.0/64.0,  42.0/64.0,
                48.0/64.0,  16.0/64.0,  56.0/64.0,  24.0/64.0,  50.0/64.0,  18.0/64.0,  58.0/64.0,  26.0/64.0,
                12.0/64.0,  44.0/64.0,   4.0/64.0,  36.0/64.0,  14.0/64.0,  46.0/64.0,   6.0/64.0,  38.0/64.0,
                60.0/64.0,  28.0/64.0,  52.0/64.0,  20.0/64.0,  62.0/64.0,  30.0/64.0,  54.0/64.0,  22.0/64.0,
                 3.0/64.0,  35.0/64.0,  11.0/64.0,  43.0/64.0,   1.0/64.0,  33.0/64.0,   9.0/64.0,  41.0/64.0,
                51.0/64.0,  19.0/64.0,  59.0/64.0,  27.0/64.0,  49.0/64.0,  17.0/64.0,  57.0/64.0,  25.0/64.0,
                15.0/64.0,  47.0/64.0,   7.0/64.0,  39.0/64.0,  13.0/64.0,  45.0/64.0,   5.0/64.0,  37.0/64.0,
                63.0/64.0,  31.0/64.0,  55.0/64.0,  23.0/64.0,  61.0/64.0,  29.0/64.0,  53.0/64.0,  21.0/64.0
            };

            float SampleBayer(float2 screenPos)
            {
                float2 scaled = screenPos / max(_DitherScale, 1.0);

                #if defined(DITHER_BAYER2)
                    uint2 px = (uint2)scaled % 2;
                    return Bayer2[px.y * 2 + px.x];
                #elif defined(DITHER_BAYER4)
                    uint2 px = (uint2)scaled % 4;
                    return Bayer4[px.y * 4 + px.x];
                #else // DITHER_BAYER8
                    uint2 px = (uint2)scaled % 8;
                    return Bayer8[px.y * 8 + px.x];
                #endif
            }

            float3 Quantize(float3 col, float steps)
            {
                return floor(col * steps + 0.5) / steps;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv       = input.texcoord;
                float2 screenPx = uv * _ScreenParams.xy;

                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half4 col      = original;

                float bayer = SampleBayer(screenPx);
                float steps = max((float)_DitherColorDepth, 2.0);
                float shift = (bayer - 0.5) * _DitherStrength / steps;

                half4 dithered = col;
                if (_DitherLumaOnly > 0.5)
                {
                    float luma     = dot(col.rgb, float3(0.2126, 0.7152, 0.0722));
                    float ditheredL = Quantize(float3(luma + shift, 0, 0), steps).x;
                    dithered.rgb   = col.rgb * (ditheredL / max(luma, 1e-5));
                }
                else
                {
                    dithered.rgb = Quantize(col.rgb + shift, steps);
                }

                // Blend between original and dithered so the user can dial back darkening
                col.rgb = lerp(original.rgb, dithered.rgb, _DitherBlend);

                return col;
            }
            ENDHLSL
        }
    }
}
