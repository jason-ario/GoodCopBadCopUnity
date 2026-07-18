Shader "GoodCopBadCop/GlassCrackOverlay"
{
    Properties
    {
        // Driven at runtime via MaterialPropertyBlock by BreakableGlassController.
        [PerRendererData] _CrackProgress ("Crack Progress", Range(0, 1)) = 0

        _CrackColor    ("Crack Color",        Color)          = (0.07, 0.07, 0.09, 1)
        _LineWidth     ("Line Width",          Range(0.002, 0.05)) = 0.009
        _ImpactSize    ("Impact Point Size",   Range(0.005, 0.10)) = 0.030
    }

    SubShader
    {
        // Renders transparently on top of the existing glass material.
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent+1"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull   Off          // Double-sided so it shows from both interior and exterior.

        Pass
        {
            Name "GlassCrackOverlay"

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _CrackProgress;
                float4 _CrackColor;
                float  _LineWidth;
                float  _ImpactSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                return OUT;
            }

            #define TWO_PI 6.28318530718
            #define PI     3.14159265359

            // Shortest signed angular distance, normalised to (-PI, PI].
            float AngleDiff(float a, float b)
            {
                float d = a - b;
                d -= TWO_PI * floor((d + PI) / TWO_PI);
                return abs(d);
            }

            // Signed-distance contribution of one radial crack ray.
            //   rayAngle  – direction (radians) of the ray from the center
            //   maxR      – current crack extent (expands with _CrackProgress)
            //   rayScale  – per-ray length fraction so rays have irregular ends
            float CrackRay(float2 uv, float rayAngle, float maxR, float rayScale)
            {
                float2 d     = uv - float2(0.5, 0.5);
                float  r     = length(d);
                float  theta = atan2(d.y, d.x);

                float thisCrackR = maxR * rayScale;
                float angDiff    = AngleDiff(theta, rayAngle);

                // Slightly thicker near the centre for an organic look.
                float adaptiveWidth = _LineWidth * (1.0 + 0.6 * (1.0 - saturate(r / 0.12)));

                float onLine    = 1.0 - smoothstep(0.0, adaptiveWidth,       angDiff);
                float inRange   = 1.0 - smoothstep(thisCrackR * 0.85, thisCrackR, r);
                float notCenter = smoothstep(0.0, 0.012, r);

                return onLine * inRange * notCenter;
            }

            // Signed-distance contribution of one concentric ring.
            //   ringR          – radius of the ring
            //   maxR           – current crack extent
            //   visThreshold   – _CrackProgress value at which this ring begins to appear
            float CrackRing(float2 uv, float ringR, float maxR, float visThreshold)
            {
                float2 d = uv - float2(0.5, 0.5);
                float  r = length(d);

                float ringVis    = smoothstep(visThreshold, visThreshold + 0.13, _CrackProgress);
                float ringInRange = step(ringR, maxR);
                float onRing     = 1.0 - smoothstep(0.0, _LineWidth * 0.65, abs(r - ringR));

                return onRing * ringVis * ringInRange;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Early-out when undamaged: avoids any cost on the intact glass.
                if (_CrackProgress < 0.001)
                    return half4(0, 0, 0, 0);

                float2 d    = IN.uv - float2(0.5, 0.5);
                float  r    = length(d);
                float  maxR = _CrackProgress * 0.5;   // cracks reach the edge at progress = 1.

                float crack = 0.0;

                // ── Central impact point ──────────────────────────────────────
                float impact = 1.0 - smoothstep(0.0, _ImpactSize * _CrackProgress, r);
                crack = max(crack, impact);

                // ── 7 primary rays at irregular angles ────────────────────────
                // Angles chosen to avoid perfect symmetry; length fractions vary
                // so crack tips look organic rather than uniform.
                crack = max(crack, CrackRay(IN.uv,  0.00,  maxR, 1.00));
                crack = max(crack, CrackRay(IN.uv,  0.92,  maxR, 0.88));
                crack = max(crack, CrackRay(IN.uv,  1.83,  maxR, 0.94));
                crack = max(crack, CrackRay(IN.uv,  2.75,  maxR, 0.82));
                crack = max(crack, CrackRay(IN.uv, -2.36,  maxR, 0.90));
                crack = max(crack, CrackRay(IN.uv, -1.44,  maxR, 0.86));
                crack = max(crack, CrackRay(IN.uv, -0.52,  maxR, 0.92));

                // ── Concentric ring fragments (appear progressively) ──────────
                // Ring 1: first visible at ~25% progress, fully visible by ~38%
                crack = max(crack, CrackRing(IN.uv, 0.12, maxR, 0.22));
                // Ring 2: first visible at ~52%, fully visible by ~65%
                crack = max(crack, CrackRing(IN.uv, 0.22, maxR, 0.50));
                // Ring 3: first visible at ~78%, fully visible by ~91%
                crack = max(crack, CrackRing(IN.uv, 0.33, maxR, 0.78));

                return half4(_CrackColor.rgb, crack * _CrackColor.a);
            }
            ENDHLSL
        }
    }
}
