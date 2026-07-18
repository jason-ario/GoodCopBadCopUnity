// UI-overlay version of TentacleBlackout.
// Renders on a full-screen RawImage inside a Screen Space Overlay Canvas so that it
// composites on top of ALL UI canvases, regardless of their sort order.
// Drive _Progress (0=transparent, 1=fully covered) via TentacleBlackoutController.
Shader "GoodCopBadCop/TentacleBlackoutOverlay"
{
    Properties
    {
        _MainTex     ("(required by RawImage, unused)", 2D) = "white" {}
        _Progress    ("Progress", Range(0, 1)) = 0
        _DarkColor   ("Dark Color", Color) = (0.018, 0.0, 0.045, 1)
        _WiggleSpeed ("Wiggle Speed", Float) = 0.65
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
            Name "TentacleBlackoutOverlay"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            float  _Progress;
            float4 _DarkColor;
            float  _WiggleSpeed;

            // ─── Hash & value noise ───────────────────────────────────────────

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
                for (int i = 0; i < 5; ++i)
                {
                    v += a * vnoise(p);
                    p  = p * 2.1 + float2(3.17, 1.93);
                    a *= 0.5;
                }
                return v;
            }

            // ─── Single spiral vortex ─────────────────────────────────────────

            float Vortex(float2 uv, float2 center, float lp, float t)
            {
                if (lp <= 0.0) return 0.0;

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 d = uv - center;
                d.x *= aspect;

                float r     = length(d);
                float theta = atan2(d.y, d.x);

                float coreR = lp * 0.28;
                float core  = 1.0 - smoothstep(coreR * 0.60, coreR, r);

                float armSpin  = theta * 4.0 - r * 16.0 + t * 2.1 * _WiggleSpeed;
                float armShape = saturate(sin(armSpin) * 1.65 + 0.2);
                float armR     = lp * 0.50;
                float armMask  = smoothstep(0.0, coreR * 0.45, r) *
                                 (1.0 - smoothstep(coreR * 0.85, armR, r));
                float arms     = armShape * armMask;

                float tipSpin  = theta * 10.0 - r * 34.0 + t * 4.8 * _WiggleSpeed;
                float tipShape = saturate(sin(tipSpin) + 0.28) * 0.72;
                float tipMask  = smoothstep(armR - 0.04, armR + 0.01, r) *
                                 (1.0 - smoothstep(armR + 0.01, armR + 0.08, r));
                float tips     = tipShape * tipMask;

                float haloR = lp * 0.65;
                float halo  = (1.0 - smoothstep(armR, haloR, r)) * 0.25
                            * smoothstep(coreR * 0.5, armR, r);

                return saturate(core + arms * 0.92 + tips + halo);
            }

            // ─── Vertex shader ────────────────────────────────────────────────

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv         = input.uv;
                return output;
            }

            // ─── Fragment shader ──────────────────────────────────────────────

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float  t  = _Time.y;

                // Fully transparent when idle — no overdraw cost
                if (_Progress < 0.002)
                    return half4(0, 0, 0, 0);

                // ── Organic wavefront ──────────────────────────────────────────
                float warp =
                    fbm(float2(uv.y * 4.2, uv.y * 2.1 + t * 0.18 * _WiggleSpeed)) * 0.17
                  + sin(uv.y * 7.3 + t * 1.15 * _WiggleSpeed) * 0.04
                  + sin(uv.y * 3.1 - t * 0.62 * _WiggleSpeed) * 0.03;

                float wave  = _Progress * 1.28 - 0.14;
                float sweep = 1.0 - smoothstep(wave - 0.07, wave + 0.07, uv.x + warp);

                // ── 6 vortex seeds ─────────────────────────────────────────────
                float vDark = 0.0;
                vDark = max(vDark, Vortex(uv, float2(0.10, 0.26), saturate((_Progress - 0.00) / 0.52), t));
                vDark = max(vDark, Vortex(uv, float2(0.09, 0.74), saturate((_Progress - 0.04) / 0.52), t));
                vDark = max(vDark, Vortex(uv, float2(0.33, 0.50), saturate((_Progress - 0.14) / 0.52), t));
                vDark = max(vDark, Vortex(uv, float2(0.56, 0.18), saturate((_Progress - 0.27) / 0.52), t));
                vDark = max(vDark, Vortex(uv, float2(0.57, 0.80), saturate((_Progress - 0.31) / 0.52), t));
                vDark = max(vDark, Vortex(uv, float2(0.80, 0.50), saturate((_Progress - 0.50) / 0.52), t));

                // ── Octopus-style curly tendrils ───────────────────────────────
                float edgeTend = 0.0;
                {
                    float xRel     = (uv.x + warp) - wave;
                    float tipFade  = 1.0 - smoothstep(-0.02, 0.22, xRel);
                    float massFade = smoothstep(-0.18, -0.01, xRel);
                    float appear   = smoothstep(0.0, 0.25, _Progress);

                    for (int ti = 0; ti < 7; ti++)
                    {
                        float baseY = (float(ti) + 0.5) / 7.0;
                        float seed  = float(ti) * 1.6180;
                        float pathY = baseY
                                    + sin(xRel *  7.0 - t * 1.5 * _WiggleSpeed + seed * 2.39) * 0.060
                                    + sin(xRel * 18.0 - t * 2.8 * _WiggleSpeed + seed * 5.11) * 0.018;
                        float thick = lerp(0.022, 0.010, saturate(xRel / 0.15));
                        edgeTend    = max(edgeTend, 1.0 - smoothstep(0.0, thick, abs(uv.y - pathY)));
                    }
                    edgeTend *= tipFade * massFade * appear;
                }

                // ── Combine ────────────────────────────────────────────────────
                float vDarkMasked = vDark * smoothstep(0.05, 0.55, sweep);
                float darkness    = saturate(sweep + vDarkMasked * 0.85 + edgeTend * 0.55);

                // Output: dark color with darkness as alpha, blended over everything below
                return half4((half3)_DarkColor.rgb, (half)darkness);
            }
            ENDHLSL
        }
    }
}
