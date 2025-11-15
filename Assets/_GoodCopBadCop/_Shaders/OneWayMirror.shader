Shader "Custom/OneWayMirror_URP"
{
    Properties
    {
        _MirrorColor("Mirror Tint", Color) = (1,1,1,1)
        _GlassColor("Glass Tint", Color) = (1,1,1,0.2)
        _GlassAlpha("Glass Alpha", Range(0,1)) = 0.2
        _Metallic("Metallic", Range(0,1)) = 1
        _Smoothness("Smoothness", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalRenderPipeline" "Queue"="Transparent" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : NORMAL;
                float3 viewDirWS : TEXCOORD0;
            };

            float4 _MirrorColor;
            float4 _GlassColor;
            float _GlassAlpha;
            float _Metallic;
            float _Smoothness;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(TransformObjectToWorld(IN.positionOS));
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float ndotv = dot(normalize(IN.normalWS), normalize(IN.viewDirWS));

                // Facing front → mirror
                if (ndotv < 0)
                {
                    float3 refl = reflect(-normalize(IN.viewDirWS), normalize(IN.normalWS));
                    float3 sky = SampleSH(refl); // simple "fake reflection"
                    return half4(sky * _MirrorColor.rgb, 1);
                }
                else
                {
                    // Back side → glass
                    return half4(_GlassColor.rgb, _GlassAlpha);
                }
            }
            ENDHLSL
        }
    }
}