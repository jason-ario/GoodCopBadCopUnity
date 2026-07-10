Shader "GoodCopBadCop/RippedPoster"
{
    Properties
    {
        _MainTex        ("Poster Texture",        2D)           = "white" {}
        _Color          ("Tint",                  Color)         = (1,1,1,1)

        [Header(Rip Shape)]
        _RipAmount      ("Rip Amount",            Range(0,1))    = 0.40
        _RipScale       ("Tear Scale",            Range(0.5,20)) = 5.0
        _RipSeed        ("Rip Seed",              Range(0,100))  = 0.0
        _RipAnisotropy  ("Tear Stretch Y",        Range(0.1,4))  = 1.6

        [Header(Paper Edge)]
        _EdgeWidth      ("Edge Width",            Range(0,0.08)) = 0.022
        _EdgeColor      ("Paper Edge Color",      Color)         = (0.93,0.88,0.78,1)
        _ShadowWidth    ("Inner Shadow Width",    Range(0,0.1))  = 0.03
        _ShadowStrength ("Inner Shadow Strength", Range(0,1))    = 0.55
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "AlphaTest"
            "RenderType"     = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite On

        // ─────────────────────────────────────────────────────────────────
        // Forward Lit Pass
        // ─────────────────────────────────────────────────────────────────
        Pass
        {
            Name "RippedPoster"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ── Textures ──────────────────────────────────────────────────
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // ── Constant buffer ───────────────────────────────────────────
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half   _RipAmount;
                half   _RipScale;
                half   _RipSeed;
                half   _RipAnisotropy;
                half   _EdgeWidth;
                half4  _EdgeColor;
                half   _ShadowWidth;
                half   _ShadowStrength;
            CBUFFER_END

            // ── Structs ───────────────────────────────────────────────────
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ── Noise helpers ─────────────────────────────────────────────

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = frac(sin(dot(i,               float2(127.1, 311.7))) * 43758.5453);
                float b = frac(sin(dot(i + float2(1,0), float2(127.1, 311.7))) * 43758.5453);
                float c = frac(sin(dot(i + float2(0,1), float2(127.1, 311.7))) * 43758.5453);
                float d = frac(sin(dot(i + float2(1,1), float2(127.1, 311.7))) * 43758.5453);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float FractalNoise(float2 p)
            {
                float v = 0.0, amp = 0.5;
                UNITY_UNROLL
                for (int i = 0; i < 4; i++)
                {
                    v   += amp * ValueNoise(p);
                    p   *= 2.1;
                    amp *= 0.48;
                }
                return v;
            }

            // Compute the rip mask value at a given UV.
            // Returns signed distance from the tear edge:
            //   < 0  → torn away (clip)
            //   >= 0 → paper present
            float RipDist(float2 uv)
            {
                // Anisotropic UV stretches tears into elongated slashes
                float2 nUV = uv * float2(_RipScale, _RipScale * _RipAnisotropy)
                           + float2(_RipSeed * 7.13, _RipSeed * 3.47);

                // Two-pass domain warp → organic, non-repeating tear shapes
                float2 warp;
                warp.x = FractalNoise(nUV + float2(3.1, 0.7)) * 2.0 - 1.0;
                warp.y = FractalNoise(nUV + float2(0.3, 2.8)) * 2.0 - 1.0;
                float2 warpedUV = nUV + warp * 0.55;

                // Second warp pass at higher frequency for jagged micro-detail
                float2 warp2;
                warp2.x = ValueNoise(warpedUV * 2.1 + float2(1.7, 4.3)) * 2.0 - 1.0;
                warp2.y = ValueNoise(warpedUV * 2.1 + float2(4.1, 0.9)) * 2.0 - 1.0;
                float2 finalUV = warpedUV + warp2 * 0.18;

                // FractalNoise in [0,1]: low-value clusters form the torn patches.
                // _RipAmount controls what fraction of the poster is torn:
                //   0 = no rips, 0.5 = ~50% torn, 1 = fully torn.
                float noise = FractalNoise(finalUV);

                return noise - _RipAmount;
            }

            // ── Vertex ────────────────────────────────────────────────────
            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS   = vni.normalWS;
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.fogFactor  = ComputeFogFactor(vpi.positionCS.z);
                return OUT;
            }

            // ── Fragment ──────────────────────────────────────────────────
            half4 frag(Varyings IN) : SV_Target
            {
                float dist = RipDist(IN.uv);

                // Discard torn-away pixels
                clip(dist);

                // Edge masks
                // edgeMask   : 0 at the raw clip boundary → 1 at end of paper-white strip
                // shadowMask : 0 inside white strip → 1 in fully lit area
                float edgeMask   = saturate(dist / max(_EdgeWidth,   0.0001));
                float shadowMask = saturate((dist - _EdgeWidth) / max(_ShadowWidth, 0.0001));

                // Sample texture
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _Color;

                // Diffuse lighting – simple NdotL + ambient lift
                float3 normalWS  = normalize(IN.normalWS);
                Light  mainLight = GetMainLight();
                half   NdotL     = saturate(dot(normalWS, mainLight.direction));
                half3  diffuse   = mainLight.color * (NdotL * 0.7 + 0.3);

                half3 litColor = texColor.rgb * diffuse;

                // Inner shadow just past the white paper edge
                litColor *= lerp(1.0 - _ShadowStrength, 1.0, shadowMask);

                // Blend paper-edge color → lit poster color
                half3 finalRGB = lerp(_EdgeColor.rgb, litColor, edgeMask);

                // Fog
                finalRGB = MixFog(finalRGB, IN.fogFactor);

                return half4(finalRGB, 1.0);
            }

            ENDHLSL
        }

        // ─────────────────────────────────────────────────────────────────
        // Shadow Caster — torn regions cast no shadow
        // ─────────────────────────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half   _RipAmount;
                half   _RipScale;
                half   _RipSeed;
                half   _RipAnisotropy;
                half   _EdgeWidth;
                half4  _EdgeColor;
                half   _ShadowWidth;
                half   _ShadowStrength;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Duplicate noise helpers for shadow pass
            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = frac(sin(dot(i,               float2(127.1, 311.7))) * 43758.5453);
                float b = frac(sin(dot(i + float2(1,0), float2(127.1, 311.7))) * 43758.5453);
                float c = frac(sin(dot(i + float2(0,1), float2(127.1, 311.7))) * 43758.5453);
                float d = frac(sin(dot(i + float2(1,1), float2(127.1, 311.7))) * 43758.5453);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float FractalNoise(float2 p)
            {
                float v = 0.0, amp = 0.5;
                UNITY_UNROLL
                for (int i = 0; i < 4; i++) { v += amp * ValueNoise(p); p *= 2.1; amp *= 0.48; }
                return v;
            }

            float RipDist(float2 uv)
            {
                float2 nUV = uv * float2(_RipScale, _RipScale * _RipAnisotropy)
                           + float2(_RipSeed * 7.13, _RipSeed * 3.47);
                float2 warp;
                warp.x = FractalNoise(nUV + float2(3.1, 0.7)) * 2.0 - 1.0;
                warp.y = FractalNoise(nUV + float2(0.3, 2.8)) * 2.0 - 1.0;
                float2 warpedUV = nUV + warp * 0.55;
                float2 warp2;
                warp2.x = ValueNoise(warpedUV * 2.1 + float2(1.7, 4.3)) * 2.0 - 1.0;
                warp2.y = ValueNoise(warpedUV * 2.1 + float2(4.1, 0.9)) * 2.0 - 1.0;
                return FractalNoise(warpedUV + warp2 * 0.18) - _RipAmount;
            }

            Varyings vertShadow(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = positionCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 fragShadow(Varyings IN) : SV_Target
            {
                clip(RipDist(IN.uv));
                return 0;
            }

            ENDHLSL
        }

        // ─────────────────────────────────────────────────────────────────
        // Depth + Normals (for SSAO / depth effects)
        // ─────────────────────────────────────────────────────────────────
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half   _RipAmount;
                half   _RipScale;
                half   _RipSeed;
                half   _RipAnisotropy;
                half   _EdgeWidth;
                half4  _EdgeColor;
                half   _ShadowWidth;
                half   _ShadowStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = frac(sin(dot(i,               float2(127.1, 311.7))) * 43758.5453);
                float b = frac(sin(dot(i + float2(1,0), float2(127.1, 311.7))) * 43758.5453);
                float c = frac(sin(dot(i + float2(0,1), float2(127.1, 311.7))) * 43758.5453);
                float d = frac(sin(dot(i + float2(1,1), float2(127.1, 311.7))) * 43758.5453);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float FractalNoise(float2 p)
            {
                float v = 0.0, amp = 0.5;
                UNITY_UNROLL
                for (int i = 0; i < 4; i++) { v += amp * ValueNoise(p); p *= 2.1; amp *= 0.48; }
                return v;
            }

            float RipDist(float2 uv)
            {
                float2 nUV = uv * float2(_RipScale, _RipScale * _RipAnisotropy)
                           + float2(_RipSeed * 7.13, _RipSeed * 3.47);
                float2 warp;
                warp.x = FractalNoise(nUV + float2(3.1, 0.7)) * 2.0 - 1.0;
                warp.y = FractalNoise(nUV + float2(0.3, 2.8)) * 2.0 - 1.0;
                float2 warpedUV = nUV + warp * 0.55;
                float2 warp2;
                warp2.x = ValueNoise(warpedUV * 2.1 + float2(1.7, 4.3)) * 2.0 - 1.0;
                warp2.y = ValueNoise(warpedUV * 2.1 + float2(4.1, 0.9)) * 2.0 - 1.0;
                return FractalNoise(warpedUV + warp2 * 0.18) - _RipAmount;
            }

            Varyings vertDepth(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            float4 fragDepth(Varyings IN) : SV_Target
            {
                clip(RipDist(IN.uv));
                float3 n = normalize(IN.normalWS) * 0.5 + 0.5;
                return float4(n, 1.0);
            }

            ENDHLSL
        }
    }

    FallBack "Hidden/InternalErrorShader"
}
