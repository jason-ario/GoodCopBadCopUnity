// Override shader used by the X-Ray renderer (XRay_Renderer RenderObjects features).
// Draws any mesh (incl. skinned) as an additive, view-facing fresnel glow so overlapping
// surfaces stack up brighter, like tissue density on an X-ray film.
Shader "GoodCopBadCop/XRay Body"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.3, 0.6, 1, 1)
        _BaseIntensity ("Base Intensity (faces camera)", Range(0, 1)) = 0.06
        _RimIntensity ("Rim Intensity (silhouette)", Range(0, 4)) = 0.9
        _RimPower ("Rim Power", Range(0.1, 8)) = 2.2
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "XRay"
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

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _BaseIntensity;
                half _RimIntensity;
                half _RimPower;
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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                o.viewDirWS  = GetWorldSpaceViewDir(pos.positionWS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewDirWS);
                // abs() so back faces (Cull Off) glow the same way.
                half ndv = saturate(abs(dot(n, v)));
                half rim = pow(1.0h - ndv, _RimPower);
                half intensity = _BaseIntensity + rim * _RimIntensity;
                return half4(_Color.rgb * intensity, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
