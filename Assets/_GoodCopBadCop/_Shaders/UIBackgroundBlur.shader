Shader "Custom/UI/BackgroundBlur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _BlurSize ("Blur Size (px)", Range(0, 40)) = 12
        _BlurSamples ("Blur Quality", Range(4, 16)) = 10

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            half _BlurSize;
            half _BlurSamples;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.screenPos = ComputeScreenPos(o.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float2 texel = _BlurSize / _ScaledScreenParams.xy;

                half3 sum = 0;
                half total = 0;
                int samples = (int)_BlurSamples;

                UNITY_UNROLL
                for (int s = 0; s < 16; s++)
                {
                    if (s >= samples) break;

                    float angle = (s / (float)samples) * TWO_PI;
                    float radius = 0.35 + 0.65 * frac(s * 0.6180339887);
                    float2 offset = float2(cos(angle), sin(angle)) * texel * radius;

                    sum += SampleSceneColor(uv + offset).rgb;
                    total += 1.0;
                }
                sum += SampleSceneColor(uv).rgb;
                total += 1.0;

                half3 blurred = sum / max(total, 1.0);

                half3 finalColor = lerp(blurred, i.color.rgb, i.color.a);

                return half4(finalColor, i.color.a);
            }
            ENDHLSL
        }
    }
}
