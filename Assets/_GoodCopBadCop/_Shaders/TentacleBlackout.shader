Shader "GoodCopBadCop/TentacleBlackout"
{
    Properties
    {
        _MainTex     ("Source (Blit)", 2D) = "white" {}
        _Progress    ("Progress", Range(0, 1)) = 0
        _DarkColor   ("Dark Color", Color) = (0.018, 0.0, 0.045, 1)
        _WiggleSpeed ("Wiggle Speed", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "TentacleBlackout"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

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

            // 5-octave fBm for organic edge warping
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
            // center: UV-space position
            // lp    : local progress 0→1 (how far this vortex has expanded)
            // t     : animated time
            float Vortex(float2 uv, float2 center, float lp, float t)
            {
                if (lp <= 0.0) return 0.0;

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 d = uv - center;
                d.x *= aspect;                     // aspect-correct so arms are circular

                float r     = length(d);
                float theta = atan2(d.y, d.x);

                // ── Core disk
                float coreR = lp * 0.28;
                float core  = 1.0 - smoothstep(coreR * 0.60, coreR, r);

                // ── 4 spiral arms (Archimedean pattern)
                float armSpin  = theta * 4.0 - r * 16.0 + t * 2.1 * _WiggleSpeed;
                float armShape = saturate(sin(armSpin) * 1.65 + 0.2);
                float armR     = lp * 0.50;
                float armMask  = smoothstep(0.0, coreR * 0.45, r) *
                                 (1.0 - smoothstep(coreR * 0.85, armR, r));
                float arms     = armShape * armMask;

                // ── Thin wriggling tendrils at arm extremities
                float tipSpin  = theta * 10.0 - r * 34.0 + t * 4.8 * _WiggleSpeed;
                float tipShape = saturate(sin(tipSpin) + 0.28) * 0.72;
                float tipMask  = smoothstep(armR - 0.04, armR + 0.01, r) *
                                 (1.0 - smoothstep(armR + 0.01, armR + 0.08, r));
                float tips     = tipShape * tipMask;

                // ── Outer halo: very faint darkness beyond the arms
                float haloR    = lp * 0.65;
                float halo     = (1.0 - smoothstep(armR, haloR, r)) * 0.25
                               * smoothstep(coreR * 0.5, armR, r);

                return saturate(core + arms * 0.92 + tips + halo);
            }

            // ─── Fragment shader ──────────────────────────────────────────────
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float  t  = _Time.y;

                // Fast-out — no effect when progress is essentially zero
                if (_Progress < 0.002)
                    return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // ── Organic wavefront (domain-warped wipe, left → right) ──────
                float warp =
                    fbm(float2(uv.y * 4.2, uv.y * 2.1 + t * 0.18 * _WiggleSpeed)) * 0.17
                  + sin(uv.y * 7.3 + t * 1.15 * _WiggleSpeed) * 0.04
                  + sin(uv.y * 3.1 - t * 0.62 * _WiggleSpeed) * 0.03;

                float wave  = _Progress * 1.28 - 0.14;   // allow slight over/under-shoot
                float sweep = 1.0 - smoothstep(wave - 0.07, wave + 0.07, uv.x + warp);

                // ── 6 vortex seeds, staggered left→right ──────────────────────
                // (delay = _Progress threshold at which each vortex begins to grow)
                float vDark = 0.0;
                vDark = max(vDark, Vortex(uv, float2(0.10, 0.26), saturate((_Progress - 0.00) / 0.52), t));
                vDark = max(vDark, Vortex(uv, float2(0.09, 0.74), saturate((_Progress - 0.04) / 0.52), t));
                vDark = max(vDark, Vortex(uv, float2(0.33, 0.50), saturate((_Progress - 0.14) / 0.52), t));
                vDark = max(vDark, Vortex(uv, float2(0.56, 0.18), saturate((_Progress - 0.27) / 0.52), t));
                vDark = max(vDark, Vortex(uv, float2(0.57, 0.80), saturate((_Progress - 0.31) / 0.52), t));
                vDark = max(vDark, Vortex(uv, float2(0.80, 0.50), saturate((_Progress - 0.50) / 0.52), t));

                // ── Octopus-style curly tendrils at the wavefront ─────────────
                // 7 sinusoidal tentacle paths, each with a slow large curl and a
                // faster secondary wiggle, tapering toward the tip.
                float edgeTend = 0.0;
                {
                    float xRel     = (uv.x + warp) - wave;  // negative = inside dark mass
                    float tipFade  = 1.0 - smoothstep(-0.02, 0.22, xRel);   // fades at advancing tip
                    float massFade = smoothstep(-0.18, -0.01, xRel);          // blends into dark mass
                    float appear   = smoothstep(0.0, 0.25, _Progress);

                    for (int ti = 0; ti < 7; ti++)
                    {
                        float baseY = (float(ti) + 0.5) / 7.0;
                        float seed  = float(ti) * 1.6180; // golden ratio — unique phase per tendril

                        // Slow primary S-bend + faster secondary wiggle = curly tentacle shape
                        float pathY = baseY
                                    + sin(xRel *  7.0 - t * 1.5 * _WiggleSpeed + seed * 2.39) * 0.060
                                    + sin(xRel * 18.0 - t * 2.8 * _WiggleSpeed + seed * 5.11) * 0.018;

                        // Soft rounded edge, thickness tapers toward tip
                        float thick = lerp(0.022, 0.010, saturate(xRel / 0.15));
                        edgeTend    = max(edgeTend, 1.0 - smoothstep(0.0, thick, abs(uv.y - pathY)));
                    }

                    edgeTend *= tipFade * massFade * appear;
                }

                // ── Combine layers ────────────────────────────────────────────
                // Vortices are masked by the sweep so they only add swirling texture
                // inside the already-dark region — never as isolated shapes in clear space.
                float vDarkMasked = vDark * smoothstep(0.05, 0.55, sweep);
                float darkness = saturate(sweep + vDarkMasked * 0.85 + edgeTend * 0.55);

                half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half3 col   = lerp(scene.rgb, (half3)_DarkColor.rgb, darkness);
                return half4(col, scene.a);
            }
            ENDHLSL
        }
    }
}
