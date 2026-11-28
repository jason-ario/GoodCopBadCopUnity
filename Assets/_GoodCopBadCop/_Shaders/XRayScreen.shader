// Monitor display for the X-Ray machine. Shows the X-Ray camera feed (render texture)
// with a soft glow, scanlines, film grain, flicker and vignette. Unlit / self-illuminated.
Shader "GoodCopBadCop/XRay Screen"
{
    Properties
    {
        _MainTex ("X-Ray Feed", 2D) = "black" {}
        _Tint ("Tint", Color) = (0.8, 0.92, 1, 1)
        _BackgroundColor ("Background", Color) = (0, 0.008, 0.02, 1)
        _Brightness ("Brightness", Range(0, 4)) = 1.3
        _Contrast ("Contrast (gamma)", Range(0.3, 3)) = 1.1
        _GlowStrength ("Glow Strength", Range(0, 2)) = 0.6
        _GlowRadius ("Glow Radius", Range(0, 0.02)) = 0.006
        _ScanlineCount ("Scanline Count", Float) = 240
        _ScanlineStrength ("Scanline Strength", Range(0, 1)) = 0.12
        _ScanlineSpeed ("Scanline Speed", Float) = 3
        _NoiseStrength ("Grain Strength", Range(0, 0.5)) = 0.05
        _FlickerStrength ("Flicker Strength", Range(0, 0.5)) = 0.04
        _VignetteStrength ("Vignette Strength", Range(0, 3)) = 1.2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                half4 _BackgroundColor;
                half _Brightness;
                half _Contrast;
                half _GlowStrength;
                float _GlowRadius;
                float _ScanlineCount;
                half _ScanlineStrength;
                float _ScanlineSpeed;
                half _NoiseStrength;
                half _FlickerStrength;
                half _VignetteStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            Varyings vert (Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float t = _Time.y;

                half3 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;

                // Cheap 8-tap glow to soften edges like film bloom.
                float r = _GlowRadius;
                half3 glow = 0;
                glow += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( r, 0)).rgb;
                glow += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-r, 0)).rgb;
                glow += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0,  r)).rgb;
                glow += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, -r)).rgb;
                glow += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( r,  r) * 1.6).rgb;
                glow += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-r,  r) * 1.6).rgb;
                glow += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( r, -r) * 1.6).rgb;
                glow += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-r, -r) * 1.6).rgb;
                col += (glow / 8.0) * _GlowStrength;

                col = pow(max(col, 0), _Contrast) * _Brightness * _Tint.rgb;

                // Rolling scanlines.
                half scan = 0.5 + 0.5 * sin((uv.y * _ScanlineCount + t * _ScanlineSpeed) * TWO_PI);
                col *= 1.0 - _ScanlineStrength * scan;

                // Flicker.
                col *= 1.0 - _FlickerStrength * Hash12(float2(floor(t * 24.0), 7.13));

                // Film grain (follows the mode tint).
                half grain = Hash12(uv * 1024.0 + frac(t) * 517.0) - 0.5;
                col += grain * _NoiseStrength * _Tint.rgb;

                col += _BackgroundColor.rgb;

                // Vignette.
                float2 d = uv - 0.5;
                half vig = saturate(1.0 - dot(d, d) * _VignetteStrength * 2.0);
                col *= vig;

                return half4(max(col, 0), 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
