Shader "GoodCopBadCop/RadiationGlitch"
{
    // Fullscreen radiation vignette used by GlitchEffectView.
    // Mirrors the hurt effect (URP colored Vignette + desaturation, optional chromatic aberration) in yellow.
    // Glitching comes from GlitchController (the mutant Interferences/film-grain volume), not this shader.
    // Runs after post-processing so the yellow is never re-graded by the scene's color grade
    // (a URP Vignette is applied before grading, which is what turned yellow vignettes green).
    Properties
    {
        _Intensity          ("Intensity", Range(0, 1)) = 0
        _Tint               ("Tint", Color) = (1, 0.86, 0.15, 1)
        _VignetteIntensity  ("Vignette Intensity", Range(0, 1)) = 0.5
        _VignetteSmoothness ("Vignette Smoothness", Range(0.01, 1)) = 0.75
        _Chromatic          ("Chromatic Aberration", Range(0, 1)) = 0
        _Desaturate         ("Desaturate", Range(0, 1)) = 0.2
        _GlowStrength       ("Glow Strength", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "RadiationVignette"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float  _Intensity;
            float4 _Tint;
            float  _VignetteIntensity;
            float  _VignetteSmoothness;
            float  _Chromatic;
            float  _Desaturate;
            float  _GlowStrength;

            // Same falloff as URP's UberPost vignette: 1 in the centre, 0 at the corners.
            float VignetteFactor(float2 uv, float intensity, float smoothness)
            {
                float2 d = abs(uv - 0.5) * (intensity * 3.0);
                return pow(saturate(1.0 - dot(d, d)), smoothness * 5.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                if (_Intensity < 0.002)
                    return src;

                float I = _Intensity;
                half3 col = src.rgb;

                // Optional URP-style chromatic aberration (off by default).
                float ca = _Chromatic * I;
                if (ca > 0.001)
                {
                    float2 coords = 2.0 * uv - 1.0;
                    float2 delta  = coords * dot(coords, coords) * ca * 0.05;
                    col.g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - delta * 0.5).g;
                    col.b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - delta).b;
                }

                // Desaturation (hurt preset uses -20 saturation).
                half lum = dot(col, half3(0.2126, 0.7152, 0.0722));
                col = lerp(col, lum.xxx, _Desaturate * I);

                // Yellow vignette, URP formula, with slight breathing so sustained exposure reads as ongoing.
                float breath = 0.9 + 0.1 * sin(_Time.y * 3.0);
                float vf     = VignetteFactor(uv, _VignetteIntensity * I * breath, _VignetteSmoothness);
                half3 tint   = (half3)_Tint.rgb;
                col *= lerp(tint, 1.0, vf);

                // Small additive yellow lift at the edges so it still reads in dark night scenes.
                col += tint * (1.0 - vf) * _GlowStrength * 0.25 * I;

                return half4(col, src.a);
            }
            ENDHLSL
        }
    }
}
