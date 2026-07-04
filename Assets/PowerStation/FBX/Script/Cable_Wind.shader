Shader "Custom/Cable_Wind_HLSL"
{
    Properties
    {
        _MainTex ("Albedo Texture", 2D) = "white" {}
        _Color ("Tint Color", Color) = (1,1,1,1)
        
        [Header(Wind Parameters)]
        _WindDirection ("Wind Direction (World)", Vector) = (1, 0, 0.5, 0)
        _WindSpeed ("Wind Speed", Float) = 1.5
        _WindAmplitude ("Wind Amplitude", Range(0, 1)) = 0.08
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline" 
            "Queue" = "Geometry" 
        }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;        // R = Wind Weight, G = Phase
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float4 color        : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _WindDirection;
                float _WindSpeed;
                float _WindAmplitude;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // --- GPU VERTEX WIND DISPLACEMENT ---
                float windWeight = input.color.r;
                float windPhase  = input.color.g;

                // Transform world wind direction to local object space
                float3 windDirWS = normalize(_WindDirection.xyz);
                float3 windDirOS = TransformWorldToObjectDir(windDirWS);

                // Calculate stacked primary sway + secondary gust
                float time = _Time.y * _WindSpeed;
                float sway = sin(time + windPhase * 6.2831) * _WindAmplitude * windWeight;
                float gust = sin(time * 0.23 + windPhase * 3.1) * (_WindAmplitude * 0.35) * windWeight;

                // Apply displacement to vertex position
                input.positionOS.xyz += windDirOS * (sway + gust);
                // ------------------------------------

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, float4(0,0,0,0));

                output.positionCS = vertexInput.positionCS;
                output.normalWS = normalInput.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // Sample texture and tint
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;

                // Simple Lambertian directional lighting (PS3-style efficiency)
                Light mainLight = GetMainLight();
                half3 normalWS = normalize(input.normalWS);
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                
                // Combine ambient + diffuse lighting
                half3 lighting = mainLight.color * NdotL + SampleSH(normalWS);
                
                return half4(texColor.rgb * lighting, texColor.a);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}