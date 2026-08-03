Shader "Custom/StagnantToxicWater"
{
    Properties
    {
        [Header(Water Colors)]
        _ShallowColor ("Shallow Edge Color", Color) = (0.5, 0.9, 0.2, 1)
        _DeepColor ("Deep Water Color", Color) = (0.1, 0.3, 0.1, 1)
        
        [Header(Depth Settings)]
        _MaxDepth ("Depth Fade Distance", Float) = 2.0
        _EdgeSoftness ("Edge Softness", Float) = 5.0

        [Header(Sludge Movement)]
        _NoiseTex ("Sludge Noise Texture", 2D) = "white" {}
        _ScrollSpeed ("Scroll Speed (X, Y)", Vector) = (0.05, 0.02, 0, 0)
        _NoiseStrength ("Noise Distortion", Range(0, 1)) = 0.4
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _MaxDepth;
                float _EdgeSoftness;
                float4 _NoiseTex_ST;
                float4 _ScrollSpeed;
                float _NoiseStrength;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.uv = IN.uv * _NoiseTex_ST.xy + _NoiseTex_ST.zw; 
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float2 pannedUV = IN.uv + (_Time.y * _ScrollSpeed.xy);
                half sludgeNoise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, pannedUV).r;

                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneZ = LinearEyeDepth(rawDepth, _ZBufferParams);
                float surfaceZ = IN.screenPos.w; 
                float depthDifference = max(0.0, sceneZ - surfaceZ);
                
                float depthFade = saturate(depthDifference / _MaxDepth);
                float noisyFade = saturate(depthFade + (sludgeNoise * _NoiseStrength - (_NoiseStrength * 0.5)));

                half4 finalColor = lerp(_ShallowColor, _DeepColor, noisyFade);
                
                finalColor.a = saturate((depthDifference * _EdgeSoftness) - (sludgeNoise * _NoiseStrength)); 

                finalColor.rgb = MixFog(finalColor.rgb, IN.fogFactor);

                return finalColor;
            }
            ENDHLSL
        }
    }
}