Shader "GoodCopBadCop/RadiationGlitch"
{
    // Edge-masked fullscreen glitch used by GlitchEffectView (EFullscreenEffectMode.Glitch).
    // Runs after post-processing so the tint is never re-graded by the scene's color grade.
    // The screen centre stays readable; warping, tearing, RGB split and a glow live at the borders.
    Properties
    {
        _Intensity      ("Intensity", Range(0, 1)) = 0
        _Tint           ("Tint", Color) = (1, 0.86, 0.15, 1)
        _EdgeWidth      ("Edge Width", Range(0.05, 1)) = 0.45
        _WarpStrength   ("Warp Strength", Range(0, 1)) = 0.6
        _GlitchStrength ("Glitch Strength", Range(0, 1)) = 0.6
        _GlowStrength   ("Glow Strength", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "RadiationGlitch"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float  _Intensity;
            float4 _Tint;
            float  _EdgeWidth;
            float  _WarpStrength;
            float  _GlitchStrength;
            float  _GlowStrength;

            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float hash21(float2 p)
            {
                p  = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(hash21(i),                hash21(i + float2(1, 0)), u.x),
                    lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), u.x),
                    u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int i = 0; i < 3; ++i)
                {
                    v += a * vnoise(p);
                    p  = p * 2.03 + float2(3.17, 1.93);
                    a *= 0.5;
                }
                return v;
            }

            // 0 in the centre, 1 at the borders. Biased toward the left/right sides,
            // with an animated noisy boundary so the edge crawls instead of being a clean ring.
            float EdgeMask(float2 uv, float t)
            {
                float2 d = abs(uv - 0.5) * 2.0;
                d.y *= 0.82;
                float e = pow(pow(d.x, 4.0) + pow(d.y, 4.0), 0.25);
                e += (fbm(uv * 3.5 + float2(t * 0.35, -t * 0.22)) - 0.5) * 0.28;
                return smoothstep(1.0 - _EdgeWidth, 1.05, e);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                if (_Intensity < 0.002)
                    return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float t = _Time.y;
                float I = _Intensity;
                float mask = EdgeMask(uv, t) * I;

                // Warp: noise displacement plus a pulsing pull toward the centre at the edges.
                float2 n = float2(
                    fbm(uv * 5.0 + float2(t * 0.9, 0.0)),
                    fbm(uv * 5.0 + float2(17.3, -t * 0.8))) - 0.5;
                float2 fromCentre = uv - 0.5;
                float pulse = 0.5 + 0.5 * sin(t * 3.1 + length(fromCentre) * 9.0);
                float2 warpedUV = uv
                                + n * 0.06 * _WarpStrength * mask
                                - fromCentre * 0.05 * _WarpStrength * mask * pulse;

                // Glitch: horizontal bands that tear sideways in short random bursts.
                float tStep     = floor(t * 14.0);
                float bandCount = lerp(18.0, 42.0, hash11(tStep * 1.7));
                float band      = floor(uv.y * bandCount);
                float bandRand  = hash21(float2(band, tStep));
                float active    = step(1.0 - 0.28 * I * _GlitchStrength, bandRand);
                float bandMask  = saturate(EdgeMask(uv, t) * 1.6) * I;
                float tear      = (hash21(float2(band * 3.1, tStep + 5.0)) - 0.5) * 0.12 * _GlitchStrength;
                warpedUV.x += tear * active * bandMask;

                // RGB split along the outward direction, strongest at the borders and on torn bands.
                float split = (0.004 + 0.012 * active) * _GlitchStrength * (mask + active * bandMask * 0.5);
                float2 dir  = normalize(fromCentre + 1e-5);
                half r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, warpedUV + dir * split).r;
                half g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, warpedUV).g;
                half b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, warpedUV - dir * split).b;
                half a = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).a;
                half3 col = half3(r, g, b);

                // Scanline shimmer and sparse radiation "snow" inside the edge band.
                float scan = sin(uv.y * _ScreenParams.y * 0.5 + t * 30.0) * 0.5 + 0.5;
                col *= 1.0 - scan * 0.12 * mask;
                float snow = step(0.996, hash21(floor(uv * _ScreenParams.xy * 0.5) + floor(t * 24.0)));

                // Yellow glow, screen-blended so it brightens toward yellow instead of darkening.
                half3 tint = (half3)_Tint.rgb;
                half3 glow = tint * saturate(mask * _GlowStrength * 1.4 + active * bandMask * 0.25 + snow * mask);
                col = 1.0 - (1.0 - col) * (1.0 - glow);

                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
