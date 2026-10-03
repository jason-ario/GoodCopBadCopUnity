Shader "GoodCopBadCop/WorldSpaceUILit"
{
    // Lit replacement for UI/Default on world-space canvases (Image, RawImage, etc.).
    // Diffuse main light (with shadows) + additional lights (Forward and Forward+) + ambient SH + fog.
    // Keeps Canvas features: sprite atlases, vertex tint, Mask (stencil), RectMask2D (clip rect / softness).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        [Header(Lighting)]
        _Wrap             ("Light Wrap (soft falloff)",  Range(0, 1)) = 0.25
        _AmbientStrength  ("Ambient (Light Probes) Strength", Range(0, 2)) = 1
        _MinLight         ("Minimum Light (never darker than)", Color) = (0.08, 0.08, 0.08, 1)
        _ShadowStrength   ("Received Shadow Strength",   Range(0, 1)) = 1
        _AdditionalLightStrength ("Additional Lights Strength", Range(0, 2)) = 1
        _Emission         ("Self Illumination (unlit mix)", Range(0, 1)) = 0
        [Toggle] _BackfaceFlip ("Flip Normal On Back Faces", Float) = 1

        // --- Stencil (required for Canvas UI masking) ---
        _StencilComp      ("Stencil Comparison", Float) = 8
        _Stencil          ("Stencil ID", Float) = 0
        _StencilOp        ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask  ("Stencil Read Mask", Float) = 255
        _ColorMask        ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline"    = "UniversalPipeline"
        }

        Stencil
        {
            Ref       [_Stencil]
            Comp      [_StencilComp]
            Pass      [_StencilOp]
            ReadMask  [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "WorldSpaceUILit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half   _Wrap;
                half   _AmbientStrength;
                half4  _MinLight;
                half   _ShadowStrength;
                half   _AdditionalLightStrength;
                half   _Emission;
                half   _BackfaceFlip;
            CBUFFER_END

            // Set by RectMask2D / Canvas per material instance.
            float4 _ClipRect;
            float  _UIMaskSoftnessX;
            float  _UIMaskSoftnessY;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 mask       : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            #include "WorldSpaceUILighting.hlsl"

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                // UI quads face the viewer along canvas -Z.
                OUT.normalWS   = WorldSpaceUINormal();
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color      = IN.color * _Color;
                OUT.fogFactor  = ComputeFogFactor(pos.positionCS.z);

                float2 pixelSize = OUT.positionCS.w;
                pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                OUT.mask = float4(IN.positionOS.xy * 2 - clampedRect.xy - clampedRect.zw,
                                  0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                    half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                    albedo.a *= m.x * m.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                    clip(albedo.a - 0.001);
                #endif

                half3 n = WorldSpaceUIFaceNormal(IN.normalWS, IS_FRONT_VFACE(face, true, false));
                half3 lighting = WorldSpaceUILighting(IN.positionWS, n, IN.positionCS);

                half3 color = albedo.rgb * lighting;
                color = MixFog(color, IN.fogFactor);
                return half4(color, albedo.a);
            }
            ENDHLSL
        }
    }
    Fallback "UI/Default"
}
