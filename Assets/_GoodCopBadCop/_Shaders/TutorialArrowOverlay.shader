Shader "GoodCopBadCop/TutorialArrowOverlay"
{
    // Unlit sprite shader for world-space UI markers (e.g. the Tutorial Arrow) that
    // must always read as "on top" of the scene without actually being screen-space
    // geometry. Ignores the depth buffer entirely and renders in the Overlay queue,
    // so it draws after everything else (opaque, transparent, and other sprites)
    // regardless of what's physically in front of it in 3D space.
    //
    // Assign to a SpriteRenderer. Reads the sprite texture/tint the same way
    // Sprites-Default does (per-vertex COLOR + TEXCOORD0), so existing SpriteRenderer
    // color/alpha and sprite swaps keep working with no script changes.

    Properties
    {
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor]   _Color   ("Tint",           Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Overlay"
            "IgnoreProjector"  = "True"
            "CanUseSpriteAtlas" = "True"
            "PreviewType"    = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        // Always pass the depth test — draws in front of everything, "in front of
        // everything else" as requested, even though the arrow stays world-positioned.
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "TutorialArrowOverlay"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv    = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return texColor * IN.color;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/InternalErrorShader"
}
