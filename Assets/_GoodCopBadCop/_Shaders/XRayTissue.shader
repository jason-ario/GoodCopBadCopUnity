// "Deep Tissue" override shader for the X-Ray machine (drawn by XRayBodyFeature when the
// Deep Tissue mode is active). Renders suspects as a soft, thermal-style glow where each
// region of skin gets a vitality value that maps to a colour ramp:
//   dead (cold violet) -> weak (dim red) -> healthy (orange) -> hot (yellow-white).
// Vitality = procedural noise in mesh UV space (sticks to the skin while animating)
// multiplied by an optional painted mask (R channel, white = alive, black = dead).
// Living tissue pulses faintly; dead tissue does not, which helps tell them apart.
Shader "GoodCopBadCop/XRay Tissue"
{
    Properties
    {
        [Header(Vitality)]
        _TissueMask ("Tissue Mask (R: 1 alive, 0 dead)", 2D) = "white" {}
        _NoiseScale ("Region Scale (UV)", Float) = 7
        _NoiseSeed ("Region Seed", Vector) = (13.7, 4.1, 0, 0)
        _VitalityBias ("Vitality Bias", Range(-1, 1)) = 0.1
        _RegionContrast ("Region Contrast", Range(0.5, 6)) = 2.4
        _Bands ("Region Bands", Range(2, 8)) = 4
        _BandStrength ("Band Strength (0 smooth, 1 posterized)", Range(0, 1)) = 0.55

        [Header(Colour Ramp)]
        [HDR] _DeadColor ("Dead", Color) = (0.25, 0.12, 0.55, 1)
        [HDR] _WeakColor ("Weak", Color) = (0.55, 0.08, 0.04, 1)
        [HDR] _HealthyColor ("Healthy", Color) = (1, 0.38, 0.08, 1)
        [HDR] _HotColor ("Hot", Color) = (1, 0.85, 0.55, 1)

        [Header(Density)]
        _BaseIntensity ("Base Intensity", Range(0, 1)) = 0.04
        _CoreIntensity ("Core Intensity (faces camera)", Range(0, 2)) = 0.35
        _CorePower ("Core Power", Range(0.1, 8)) = 1.6
        _RimIntensity ("Rim Intensity (silhouette)", Range(0, 2)) = 0.25
        _RimPower ("Rim Power", Range(0.1, 8)) = 3

        [Header(Pulse)]
        _PulseSpeed ("Pulse Speed", Float) = 4.5
        _PulseStrength ("Pulse Strength", Range(0, 1)) = 0.18

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "XRayTissue"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest [_ZTest]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_TissueMask);
            SAMPLER(sampler_TissueMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _TissueMask_ST;
                float _NoiseScale;
                float4 _NoiseSeed;
                half _VitalityBias;
                half _RegionContrast;
                half _Bands;
                half _BandStrength;
                half4 _DeadColor;
                half4 _WeakColor;
                half4 _HealthyColor;
                half4 _HotColor;
                half _BaseIntensity;
                half _CoreIntensity;
                half _CorePower;
                half _RimIntensity;
                half _RimPower;
                float _PulseSpeed;
                half _PulseStrength;
                float _Cull;
                float _ZTest;
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
                float3 viewDirWS  : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float sum = 0.0;
                float amp = 0.5;
                for (int o = 0; o < 4; o++)
                {
                    sum += ValueNoise(p) * amp;
                    p = p * 2.03 + 17.1;
                    amp *= 0.5;
                }
                return sum / 0.9375; // normalise 4 octaves to ~0..1
            }

            half3 Ramp(half v)
            {
                // 4-stop ramp at 0, 1/3, 2/3, 1.
                half3 c = lerp(_DeadColor.rgb, _WeakColor.rgb, saturate(v * 3.0));
                c = lerp(c, _HealthyColor.rgb, saturate(v * 3.0 - 1.0));
                c = lerp(c, _HotColor.rgb, saturate(v * 3.0 - 2.0));
                return c;
            }

            Varyings vert (Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                o.viewDirWS  = GetWorldSpaceViewDir(pos.positionWS);
                o.uv         = input.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                // --- Vitality per skin region ---
                float n = Fbm(i.uv * _NoiseScale + _NoiseSeed.xy);
                half vitality = saturate((n - 0.5 + _VitalityBias) * _RegionContrast + 0.5);
                vitality *= SAMPLE_TEXTURE2D(_TissueMask, sampler_TissueMask, TRANSFORM_TEX(i.uv, _TissueMask)).r;

                // Posterize into readable regions.
                half bands = max(_Bands, 2.0);
                half banded = floor(vitality * bands) / (bands - 1.0);
                vitality = lerp(vitality, saturate(banded), _BandStrength);

                // --- Density: bright core, soft silhouette ---
                float3 nrm = normalize(i.normalWS);
                float3 v = normalize(i.viewDirWS);
                half ndv = saturate(abs(dot(nrm, v)));
                half core = pow(ndv, _CorePower);
                half rim = pow(1.0h - ndv, _RimPower);
                half intensity = _BaseIntensity + core * _CoreIntensity + rim * _RimIntensity;

                // Living tissue pulses; dead tissue stays still.
                half pulse = 1.0h + _PulseStrength * vitality * sin(_Time.y * _PulseSpeed);

                return half4(Ramp(vitality) * intensity * pulse, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
