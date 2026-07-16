Shader "GoodCopBadCop/XRayAnatomy"
{
    Properties
    {
        [Enum(Body, 0, Anatomy, 1, Anomaly, 2)] _Mode ("Render Mode", Float) = 0
        _Color ("Tint", Color) = (0.25, 0.8, 1, 0.35)
        _EmissionColor ("Emission", Color) = (0.25, 0.9, 1, 1)
        _RimColor ("Rim Color", Color) = (0.55, 0.05, 0.75, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _Alpha ("Alpha", Range(0, 1)) = 0.35
        [Enum(Off, 0, On, 1)] _ZWrite ("Z Write", Float) = 0
        // Values must match UnityEngine.Rendering.CompareFunction. In particular,
        // LEqual is 4 and Always is 8; 0/1 would mean Disabled/Never.
        [Enum(LEqual, 4, Always, 8)] _ZTest ("Depth Test", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "XRayAnatomy"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _EmissionColor;
                float4 _RimColor;
                float _Mode;
                float _RimPower;
                float _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirection = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                float rim = pow(saturate(1.0 - dot(normalWS, viewDirection)), _RimPower);

                if (_Mode < 0.5)
                {
                    // Body mode: faint transparent mass with a brighter cold contour.
                    float3 color = lerp(_Color.rgb * 0.35, _EmissionColor.rgb, rim);
                    float alpha = saturate(_Alpha + rim * (1.0 - _Alpha));
                    return half4(color, alpha);
                }

                if (_Mode < 1.5)
                {
                    // Anatomy mode: solid emissive cyan/white, rendered above the body overlay.
                    return half4(_EmissionColor.rgb, 1.0);
                }

                // Anomaly mode: black core with an intentionally conspicuous violet shell.
                float anomalyRim = smoothstep(0.08, 0.65, rim);
                float3 anomalyColor = lerp(float3(0.002, 0.001, 0.004), _RimColor.rgb * 1.35, anomalyRim);
                return half4(anomalyColor, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
