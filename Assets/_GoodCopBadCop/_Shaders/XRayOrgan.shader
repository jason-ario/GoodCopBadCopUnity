// Organ shader for the X-Ray machine's Organ mode (drawn by XRayBodyFeature).
// Solid, clearly readable organs: each organ's colour comes from the global _XRayPartColor
// (set per organ from XRayInternalPart), shaded with a soft view-facing falloff, a bright rim,
// fine vein-like mottling and a gentle shared heartbeat pulse.
Shader "GoodCopBadCop/XRay Organ"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 3)) = 1.1
        _ShadowAmount ("Edge Darkening", Range(0, 1)) = 0.55
        _RimColor ("Rim Color", Color) = (1, 0.9, 0.85, 1)
        _RimIntensity ("Rim Intensity", Range(0, 2)) = 0.45
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _VeinScale ("Vein Scale", Float) = 18
        _VeinStrength ("Vein Strength", Range(0, 1)) = 0.3
        _PulseSpeed ("Pulse Speed", Float) = 6
        _PulseStrength ("Pulse Strength", Range(0, 0.5)) = 0.08
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "XRayOrgan"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest [_ZTest]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Set per organ by XRayBodyFeature (not a material property).
            half4 _XRayPartColor;

            CBUFFER_START(UnityPerMaterial)
                half _Intensity;
                half _ShadowAmount;
                half4 _RimColor;
                half _RimIntensity;
                half _RimPower;
                float _VeinScale;
                half _VeinStrength;
                float _PulseSpeed;
                half _PulseStrength;
                float _Cull;
                float _ZTest;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);
                float n000 = Hash31(i);
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));
                float x00 = lerp(n000, n100, u.x);
                float x10 = lerp(n010, n110, u.x);
                float x01 = lerp(n001, n101, u.x);
                float x11 = lerp(n011, n111, u.x);
                return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
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
                o.positionOS = input.positionOS.xyz;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewDirWS);
                half ndv = saturate(dot(n, v));

                // Soft volume shading: bright centre, darker edges.
                half shade = lerp(1.0h - _ShadowAmount, 1.0h, ndv);

                // Vein-like mottling: thin dark ridges of 3D noise.
                float vn = ValueNoise3(i.positionOS * _VeinScale);
                half vein = 1.0h - smoothstep(0.0, 0.08, abs(vn - 0.5)) ;
                half mottling = 1.0h - vein * _VeinStrength;

                half pulse = 1.0h + _PulseStrength * sin(_Time.y * _PulseSpeed);

                half3 col = _XRayPartColor.rgb * shade * mottling * _Intensity * pulse;
                // Rim keeps the organ's hue so colours stay saturated.
                col += lerp(_XRayPartColor.rgb, _RimColor.rgb, 0.35h) * pow(1.0h - ndv, _RimPower) * _RimIntensity;
                return half4(col, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
