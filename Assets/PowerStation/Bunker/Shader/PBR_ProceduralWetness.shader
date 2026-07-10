Shader "Custom/URP/PBR_ProceduralWetness_GI_Fixed"
{
    Properties
    {
        [Header(Base PBR Maps)]
        [MainTexture] _BaseMap("Albedo (Base Color)", 2D) = "white" {}
        [MainColor] _BaseColor("Color Tint", Color) = (1, 1, 1, 1)
        
        [NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}
        _NormalScale("Normal Strength", Range(0, 2)) = 1.0
        
        [NoScaleOffset] _RoughnessMap("Roughness Map", 2D) = "white" {}
        _Roughness("Roughness Multiplier", Range(0, 1)) = 1.0
        
        [NoScaleOffset] _MetallicMap("Metallic Map", 2D) = "black" {}
        _Metallic("Metallic Multiplier", Range(0, 1)) = 1.0

        [Header(Procedural Puddles)]
        _WetnessAmount("Puddle Coverage", Range(0, 1)) = 0.3
        _PuddleScale("Puddle Scale", Float) = 2.0
        _WetDarkening("Wet Color Darkening", Range(0, 1)) = 0.5
        _PuddleSmoothness("Water Smoothness", Range(0.5, 1)) = 0.95
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment

            // Realtime Light Pragmas
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            
            // Baked GI Pragmas
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 uv0          : TEXCOORD0;
                float2 uv1          : TEXCOORD1; 
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float3 tangentWS    : TEXCOORD2;
                float3 bitangentWS  : TEXCOORD3;
                float2 uv0          : TEXCOORD4;
                float2 lightmapUV   : TEXCOORD5; 
                half3 vertexSH      : TEXCOORD6; 
            };

            TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_RoughnessMap);       SAMPLER(sampler_RoughnessMap);
            TEXTURE2D(_MetallicMap);        SAMPLER(sampler_MetallicMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _NormalScale;
                half _Roughness;
                half _Metallic;
                
                half _WetnessAmount;
                float _PuddleScale;
                half _WetDarkening;
                half _PuddleSmoothness;
            CBUFFER_END

            float hash(float2 p) 
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            float valueNoise(float2 p) 
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                
                return lerp(lerp(hash(i + float2(0.0, 0.0)), hash(i + float2(1.0, 0.0)), u.x),
                            lerp(hash(i + float2(0.0, 1.0)), hash(i + float2(1.0, 1.0)), u.x), u.y);
            }

            float fbm(float2 p) 
            {
                float f = 0.0;
                f += 0.5000 * valueNoise(p); p = p * 2.02;
                f += 0.2500 * valueNoise(p); p = p * 2.03;
                return f;
            }

            Varyings LitPassVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.tangentWS = normalInput.tangentWS;
                output.bitangentWS = normalInput.bitangentWS;
                
                output.uv0 = TRANSFORM_TEX(input.uv0, _BaseMap);

                #if defined(LIGHTMAP_ON)
                    output.lightmapUV = input.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
                    output.vertexSH = half3(0,0,0);
                #else
                    output.lightmapUV = float2(0,0);
                    output.vertexSH = SampleSHVertex(normalInput.normalWS);
                #endif

                return output;
            }

            half4 LitPassFragment(Varyings input) : SV_Target
            {
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv0) * _BaseColor;
                half rawRoughness = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, input.uv0).r * _Roughness;
                half baseSmoothness = 1.0 - saturate(rawRoughness); 
                half baseMetallic = SAMPLE_TEXTURE2D(_MetallicMap, sampler_MetallicMap, input.uv0).r * _Metallic;
                
                half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv0);
                half3 normalTS = UnpackNormalScale(normalSample, _NormalScale);

                float noiseVal = fbm(input.positionWS.xz * _PuddleScale);
                
                float threshold = 1.0 - _WetnessAmount;
                half puddleMask = smoothstep(threshold, threshold + 0.1, noiseVal);

                baseColor.rgb *= lerp(1.0, _WetDarkening, puddleMask);
                half finalSmoothness = lerp(baseSmoothness, _PuddleSmoothness, puddleMask);
                half3 finalNormalTS = lerp(normalTS, half3(0, 0, 1), puddleMask);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = baseColor.rgb;
                surfaceData.metallic = baseMetallic;
                surfaceData.smoothness = finalSmoothness;
                surfaceData.normalTS = finalNormalTS;
                surfaceData.occlusion = 1.0;
                surfaceData.alpha = 1.0;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = TransformTangentToWorld(surfaceData.normalTS, half3x3(input.tangentWS, input.bitangentWS, input.normalWS));
                inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
                    inputData.shadowCoord = ComputeScreenPos(input.positionCS);
                #else
                    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                
                #if defined(LIGHTMAP_ON)
                    inputData.bakedGI = SampleLightmap(input.lightmapUV, inputData.normalWS);
                #else
                    inputData.bakedGI = input.vertexSH;
                #endif
                
                return UniversalFragmentPBR(inputData, surfaceData);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}