// UVReveal.shader
// Transparent until a UVLight's world-space cone overlaps the surface.
// Cone shape is defined by a forward direction, a half-angle, and a range.
// Supports radial falloff, soft angular edges, and optional alpha cutoff.

Shader "GoodCopBadCop/UVReveal"
{
    Properties
    {
        [Header(UV Reveal)]
        _RevealColor        ("Reveal Color Tint",  Color)       = (1, 1, 1, 1)
        _RevealMap          ("Reveal Texture",     2D)          = "white" {}
        _RevealEdgeSoftness ("Edge Softness",      Range(0, 1)) = 0.15
        _RevealFalloff      ("Reveal Falloff",     Range(0, 4)) = 1.0

        [Header(Alpha)]
        [Toggle(_ALPHATEST_ON)] _AlphaTest ("Alpha Cutoff",     Float)       = 0
        _Cutoff                            ("Cutoff Threshold", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
        }

        Pass
        {
            Name "UVReveal"
            Tags { "LightMode" = "UniversalForward" }

            Blend  SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest  LEqual
            Cull   Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ── Uniform buffers ───────────────────────────────────────────
            CBUFFER_START(UnityPerMaterial)
                float4 _RevealColor;
                float4 _RevealMap_ST;
                float  _RevealEdgeSoftness;
                float  _RevealFalloff;
                float  _Cutoff;
            CBUFFER_END

            // UV light arrays declared OUTSIDE the CBuffer so MaterialPropertyBlock
            // overrides work correctly per-renderer in URP.
            //
            // _UVLightPositions  : xyz = world position, w = range
            // _UVLightDirections : xyz = normalized forward direction
            // _UVLightParams     : x   = cos(halfAngle)
            #define UV_LIGHT_MAX_COUNT 4
            float4 _UVLightPositions[UV_LIGHT_MAX_COUNT];
            float4 _UVLightDirections[UV_LIGHT_MAX_COUNT];
            float4 _UVLightParams[UV_LIGHT_MAX_COUNT];
            int    _UVLightCount;

            // ── Textures ──────────────────────────────────────────────────
            TEXTURE2D(_RevealMap); SAMPLER(sampler_RevealMap);

            // ── Vertex ────────────────────────────────────────────────────
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
                float3 positionWS : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.uv         = IN.uv;
                OUT.fogFactor  = ComputeFogFactor(posInputs.positionCS.z);

                return OUT;
            }

            // ── Fragment ──────────────────────────────────────────────────
            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // ── UV light cone mask ─────────────────────────────────────
                //
                // For each cone light, nd is the normalized lateral distance from
                // the cone axis at each depth slice (0 = centre, 1 = rim), matching
                // the sphere version's radial nd so the gradient is smooth across
                // the full width of the beam rather than just front-to-back.
                //
                float combinedMask = 0.0;
                for (int i = 0; i < _UVLightCount; i++)
                {
                    float3 lightPos   = _UVLightPositions[i].xyz;
                    float  lightRange = _UVLightPositions[i].w;
                    float3 lightDir   = _UVLightDirections[i].xyz; // normalized forward
                    float  cosOuter   = _UVLightParams[i].x;       // cos(halfAngle)

                    float3 toFrag     = IN.positionWS - lightPos;
                    float  depthAlong = dot(toFrag, lightDir); // signed depth along cone axis

                    // Only affect surfaces in front of the apex and within range.
                    float validMask = step(0.001, depthAlong) * step(depthAlong, lightRange);

                    // Perpendicular distance from the cone axis at this depth slice.
                    float perpDist = length(toFrag - depthAlong * lightDir);

                    // Cone rim radius at this depth (tan = sin/cos, no acos needed).
                    float tanHalfAngle = sqrt(1.0 - cosOuter * cosOuter) / max(cosOuter, 0.001);
                    float coneRadius   = depthAlong * tanHalfAngle;

                    // Normalized lateral position: 0 at the axis, 1 at the rim.
                    float nd = saturate(perpDist / max(coneRadius, 0.001));

                    // Soft fade at the outer rim.
                    float softBand  = max(_RevealEdgeSoftness, 0.001);
                    float edgeMask  = 1.0 - smoothstep(1.0 - softBand, 1.0, nd);

                    // Power-curve falloff: bright at the beam centre, dims toward the rim.
                    float falloffMask = pow(1.0 - nd, max(_RevealFalloff, 0.001));

                    combinedMask = max(combinedMask, edgeMask * falloffMask * validMask);
                }

                // ── Reveal surface ─────────────────────────────────────────
                half4 revealSample = SAMPLE_TEXTURE2D(_RevealMap, sampler_RevealMap,
                    IN.uv * _RevealMap_ST.xy + _RevealMap_ST.zw);
                half3 revealColor = revealSample.rgb * _RevealColor.rgb;
                half  finalAlpha  = revealSample.a * combinedMask;

                #if defined(_ALPHATEST_ON)
                    clip(finalAlpha - _Cutoff);
                #endif

                revealColor = MixFog(revealColor, IN.fogFactor);
                return half4(revealColor, finalAlpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
