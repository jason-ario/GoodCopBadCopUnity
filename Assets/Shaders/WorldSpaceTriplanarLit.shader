Shader "URP/WorldSpaceTriplanarLit"
{
    Properties
    {
        _MainTex("Base Texture", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        _TileSize("Tile Size (meters)", Float) = 2.0
        _BlendSharpness("Blend Sharpness", Range(1, 8)) = 2.0
        _Metallic("Metallic", Range(0,1)) = 0.0
        _Smoothness("Smoothness", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float _TileSize;
                float _BlendSharpness;
                float _Metallic;
                float _Smoothness;
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
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            float3 TriplanarSample(Texture2D tex, SamplerState samp, float3 worldPos, float3 worldNormal, float tileSize)
            {
                float3 absN = pow(abs(worldNormal), _BlendSharpness);
                absN /= (absN.x + absN.y + absN.z + 1e-5);

                float2 uvX = worldPos.zy / tileSize;
                float2 uvY = worldPos.xz / tileSize;
                float2 uvZ = worldPos.xy / tileSize;

                float3 colX = SAMPLE_TEXTURE2D(tex, samp, uvX).rgb;
                float3 colY = SAMPLE_TEXTURE2D(tex, samp, uvY).rgb;
                float3 colZ = SAMPLE_TEXTURE2D(tex, samp, uvZ).rgb;

                return colX * absN.x + colY * absN.y + colZ * absN.z;
            }

            float3 TriplanarNormal(Texture2D tex, SamplerState samp, float3 worldPos, float3 worldNormal, float tileSize)
            {
                float3 absN = pow(abs(worldNormal), _BlendSharpness);
                absN /= (absN.x + absN.y + absN.z + 1e-5);

                float2 uvX = worldPos.zy / tileSize;
                float2 uvY = worldPos.xz / tileSize;
                float2 uvZ = worldPos.xy / tileSize;

                float3 nX = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, uvX));
                float3 nY = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, uvY));
                float3 nZ = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, uvZ));

                // Orient normals per axis
                nX = float3(nX.z, nX.y, -nX.x);
                nY = float3(nY.x, nY.z, -nY.y);
                nZ = float3(nZ.x, nZ.y, nZ.z);

                return normalize(nX * absN.x + nY * absN.y + nZ * absN.z);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 nWS = normalize(IN.normalWS);
                float3 albedo = TriplanarSample(_MainTex, sampler_MainTex, IN.positionWS, nWS, _TileSize);
                float3 blendedNormal = normalize(TriplanarNormal(_NormalMap, sampler_NormalMap, IN.positionWS, nWS, _TileSize));

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = blendedNormal;
                inputData.viewDirectionWS = GetWorldSpaceViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);

                // Fully initialize all SurfaceData fields
                SurfaceData surfaceData;
                surfaceData.albedo = albedo;
                surfaceData.metallic = _Metallic;
                surfaceData.specular = 0;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1;
                surfaceData.emission = 0;
                surfaceData.alpha = 1;
                surfaceData.clearCoatMask = 0;
                surfaceData.clearCoatSmoothness = 0;
                surfaceData.normalTS = float3(0, 0, 1);

                // ✅ No missing initialization warnings now
                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                return color;
            }

            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
