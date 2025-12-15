Shader "Hidden/Posterize"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalRenderPipeline" }

        Pass
        {
            Name "Posterize"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Provided automatically by Blitter.BlitCameraTexture
            TEXTURE2D_X(_BlitTexture);

            float _PosterSteps;
            float _DitherStrength;
            float _LumaOnly;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // Fullscreen triangle
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.uv = float2((v.vertexID << 1) & 2, v.vertexID & 2);
                o.positionCS = float4(o.uv * 2.0 - 1.0, 0.0, 1.0);

                // Flip Y for DX-style UVs
                o.uv.y = 1.0 - o.uv.y;
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float3 PosterizeRGB(float3 c, float steps)
            {
                return floor(c * steps) / max(steps - 1.0, 1.0);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;

                // IMPORTANT: sampler_LinearClamp is already defined by URP
                float3 col = SAMPLE_TEXTURE2D_X(
                    _BlitTexture,
                    sampler_LinearClamp,
                    uv
                ).rgb;

                float steps = max(_PosterSteps, 2.0);

                // Dithering (subtle)
                float n = Hash21(uv * _ScreenParams.xy) - 0.5;
                col += n * (_DitherStrength / steps);

                if (_LumaOnly > 0.5)
                {
                    float l  = dot(col, float3(0.2126, 0.7152, 0.0722));
                    float lp = PosterizeRGB(l.xxx, steps).x;
                    col *= (lp / max(l, 1e-4));
                }
                else
                {
                    col = PosterizeRGB(col, steps);
                }

                return half4(saturate(col), 1);
            }
            ENDHLSL
        }
    }
}
