Shader "GoodCopBadCop/TentacleVignette"
{
    Properties
    {
        _MainTex          ("(required by RawImage, unused)", 2D) = "white" {}
        _Intensity        ("Intensity",       Range(0, 1))       = 0.85
        _VignetteRadius   ("Vignette Radius", Range(0.1, 0.7))   = 0.33
        _Softness         ("Edge Softness",   Range(0.02, 0.4))  = 0.20
        _TendrilCount     ("Tendril Count",   Range(2, 20))      = 9
        _TendrilDepth     ("Tendril Depth",   Range(0, 0.8))     = 0.13
        _TendrilRootWidth ("Root Width",      Range(0.002, 0.08)) = 0.025
        _TendrilTipWidth  ("Tip Width",       Range(0.001, 0.04)) = 0.007
        _WiggleSpeed      ("Wiggle Speed",    Float)              = 0.65
        _PulseSpeed       ("Pulse Speed",     Float)              = 0.25
        _DarkColor        ("Dark Color",      Color)              = (0.018, 0.0, 0.045, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Overlay"
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "TentacleVignette"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float  _Intensity;
            float  _VignetteRadius;
            float  _Softness;
            int    _TendrilCount;
            float  _TendrilDepth;
            float  _TendrilRootWidth;
            float  _TendrilTipWidth;
            float  _WiggleSpeed;
            float  _PulseSpeed;
            float4 _DarkColor;

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
                    lerp(hash21(i),               hash21(i + float2(1, 0)), u.x),
                    lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), u.x),
                    u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                p += float2(1.7, 9.2);
                for (int k = 0; k < 4; ++k)
                {
                    v += a * vnoise(p);
                    p  = p * 2.1 + float2(3.17, 1.93);
                    a *= 0.5;
                }
                return v;
            }

            float wrapAngle(float a)
            {
                float inv2pi = 0.15915494f;
                float twopi  = 6.28318530f;
                return a - floor(a * inv2pi + 0.5f) * twopi;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv         = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float  t  = _Time.y;

                if (_Intensity < 0.002)
                    return half4(0, 0, 0, 0);

                // UV centred at (0,0), range [-0.5, +0.5]
                float2 c = uv - 0.5;
                float  r = length(c);

                // Aspect-corrected angle for even visual distribution of tendrils
                float aspect     = _ScreenParams.x / _ScreenParams.y;
                float theta      = atan2(c.y, c.x * aspect);

                // Raw angle used only to find the real screen-edge radius at this direction.
                // UV space is square (0-1 in both axes), so the screen boundary is a square:
                //   edgeR = 0.5 / max(|cos|, |sin|)
                // = 0.5 at horizontal/vertical, 0.707 at 45° corners.
                float theta_raw  = atan2(c.y, c.x);
                float edgeR      = 0.5 / max(max(abs(cos(theta_raw)), abs(sin(theta_raw))), 0.0001);

                // Distance measured inward from the actual screen border.
                // 0   = at the screen edge pixel.
                // > 0 = moving toward the center.
                // The root of every tendril is at fromEdge = 0 (screen border),
                // which is always inside the dark vignette — no gap possible.
                float fromEdge = max(0.0, edgeR - r);

                // Organic edge warp on the vignette boundary
                float warp =
                    fbm(float2(theta * 1.8 + t * 0.06 * _WiggleSpeed,
                               r    * 3.5 + t * 0.04 * _WiggleSpeed)) * 0.05
                  + sin(theta * 3.0 + t * 0.35 * _WiggleSpeed) * 0.012;

                // Circular vignette (dark at edges, transparent at centre)
                float vignette = smoothstep(_VignetteRadius, _VignetteRadius + _Softness, r + warp);

                float twopi   = 6.28318530f;
                int   count   = clamp(_TendrilCount, 2, 20);
                float tendrils = 0.0;

                for (int ti = 0; ti < count; ti++)
                {
                    float seed      = float(ti) * 1.6180f;
                    float baseAngle = float(ti) / float(count) * twopi
                                    + t * 0.07 * _WiggleSpeed
                                    + seed * 0.41;

                    // Per-tendril independent pulsing
                    float randSpeed  = 0.4 + hash21(float2(seed, 13.5f)) * 1.2f;
                    float randOffset = hash21(float2(seed, 27.1f));
                    float pulseT     = frac(t * randSpeed * _PulseSpeed + randOffset);
                    float pulseFactor    = pow(max(0.0f, sin(pulseT * 3.14159265f)), 0.55f);
                    float effectiveDepth = _TendrilDepth * pulseFactor;

                    // Sinusoidal curling path — curls as it extends from screen edge inward
                    float tendAngle = baseAngle
                        + sin(fromEdge *  9.0 + t * 1.4 * _WiggleSpeed + seed * 2.39) * 0.095
                        + sin(fromEdge * 22.0 + t * 2.6 * _WiggleSpeed + seed * 5.11) * 0.030;

                    // Arc-length distance to this tendril's centreline
                    float dAngle  = wrapAngle(theta - tendAngle);
                    float arcDist = abs(dAngle) * max(r, 0.05);

                    // Width tapers from root (at screen edge) to needle at tip
                    float tipT = saturate(fromEdge / max(effectiveDepth, 0.0001f));
                    float width = lerp(_TendrilRootWidth, _TendrilTipWidth, tipT);

                    // Fade at the pulsed tip
                    float tipFade = 1.0 - smoothstep(effectiveDepth - 0.01, effectiveDepth + 0.005, fromEdge);

                    tendrils = max(tendrils, (1.0 - smoothstep(0.0, width, arcDist)) * tipFade);
                }

                // Combine — vignette naturally hides tendril roots in the dark zone
                // (saturate clamps darkness to 1 wherever vignette is already 1)
                float darkness = saturate(vignette + tendrils * 0.7) * _Intensity;
                return half4((half3)_DarkColor.rgb, (half)darkness);
            }
            ENDHLSL
        }
    }
}
