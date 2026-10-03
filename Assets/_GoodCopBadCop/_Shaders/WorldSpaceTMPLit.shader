Shader "GoodCopBadCop/WorldSpaceTMPLit"
{
    // Lit TextMeshPro SDF for world-space text (TextMeshProUGUI on world-space canvases, or 3D TextMeshPro).
    // SDF rendering follows TMP Mobile/Distance Field (face + optional outline, bold, clip rect, stencil).
    // Lighting is shared with GoodCopBadCop/WorldSpaceUILit (WorldSpaceUILighting.hlsl) so text and
    // images on the same page respond identically to scene lights.
    Properties
    {
        _FaceColor          ("Face Color", Color) = (1,1,1,1)
        _FaceDilate         ("Face Dilate", Range(-1,1)) = 0

        _OutlineColor       ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth       ("Outline Thickness", Range(0,1)) = 0
        _OutlineSoftness    ("Outline Softness", Range(0,1)) = 0

        _WeightNormal       ("Weight Normal", float) = 0
        _WeightBold         ("Weight Bold", float) = 0.5

        _ShaderFlags        ("Flags", float) = 0
        _ScaleRatioA        ("Scale RatioA", float) = 1
        _ScaleRatioB        ("Scale RatioB", float) = 1
        _ScaleRatioC        ("Scale RatioC", float) = 1

        _MainTex            ("Font Atlas", 2D) = "white" {}
        _TextureWidth       ("Texture Width", float) = 512
        _TextureHeight      ("Texture Height", float) = 512
        _GradientScale      ("Gradient Scale", float) = 5
        _ScaleX             ("Scale X", float) = 1
        _ScaleY             ("Scale Y", float) = 1
        _PerspectiveFilter  ("Perspective Correction", Range(0, 1)) = 0.875
        _Sharpness          ("Sharpness", Range(-1,1)) = 0

        _VertexOffsetX      ("Vertex OffsetX", float) = 0
        _VertexOffsetY      ("Vertex OffsetY", float) = 0

        [Header(Lighting)]
        _Wrap             ("Light Wrap (soft falloff)",  Range(0, 1)) = 0.25
        _AmbientStrength  ("Ambient (Light Probes) Strength", Range(0, 2)) = 1
        _MinLight         ("Minimum Light (never darker than)", Color) = (0.08, 0.08, 0.08, 1)
        _ShadowStrength   ("Received Shadow Strength",   Range(0, 1)) = 1
        _AdditionalLightStrength ("Additional Lights Strength", Range(0, 2)) = 1
        _Emission         ("Self Illumination (unlit mix)", Range(0, 1)) = 0
        [Toggle] _BackfaceFlip ("Flip Normal On Back Faces", Float) = 1

        _ClipRect           ("Clip Rect", vector) = (-32767, -32767, 32767, 32767)
        _MaskSoftnessX      ("Mask SoftnessX", float) = 0
        _MaskSoftnessY      ("Mask SoftnessY", float) = 0

        _StencilComp        ("Stencil Comparison", Float) = 8
        _Stencil            ("Stencil ID", Float) = 0
        _StencilOp          ("Stencil Operation", Float) = 0
        _StencilWriteMask   ("Stencil Write Mask", Float) = 255
        _StencilReadMask    ("Stencil Read Mask", Float) = 255

        _CullMode           ("Cull Mode", Float) = 0
        _ColorMask          ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
        }

        Stencil
        {
            Ref       [_Stencil]
            Comp      [_StencilComp]
            Pass      [_StencilOp]
            ReadMask  [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull [_CullMode]
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "WorldSpaceTMPLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #pragma shader_feature_local __ OUTLINE_ON
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
                half4  _FaceColor;
                half   _FaceDilate;
                half4  _OutlineColor;
                half   _OutlineWidth;
                half   _OutlineSoftness;
                float  _WeightNormal;
                float  _WeightBold;
                float  _ShaderFlags;
                float  _ScaleRatioA;
                float  _ScaleRatioB;
                float  _ScaleRatioC;
                float  _TextureWidth;
                float  _TextureHeight;
                float  _GradientScale;
                float  _ScaleX;
                float  _ScaleY;
                float  _PerspectiveFilter;
                float  _Sharpness;
                float  _VertexOffsetX;
                float  _VertexOffsetY;
                float  _MaskSoftnessX;
                float  _MaskSoftnessY;

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
            int    _UIVertexColorAlwaysGammaSpace;

            #include "WorldSpaceUILighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float4 uv0        : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                half4  faceColor    : COLOR;
                half4  outlineColor : COLOR1;
                float2 uv           : TEXCOORD0;
                half4  param        : TEXCOORD1;   // Scale(x), BiasIn(y), BiasOut(z), Bias(w)
                half4  mask         : TEXCOORD2;   // Position in clip space(xy), Softness(zw)
                float3 positionWS   : TEXCOORD3;
                float3 normalWS     : TEXCOORD4;
                half   fogFactor    : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float bold = step(IN.uv0.w, 0);

                float4 vert = IN.positionOS;
                vert.x += _VertexOffsetX;
                vert.y += _VertexOffsetY;

                VertexPositionInputs pos = GetVertexPositionInputs(vert.xyz);
                float4 positionCS = pos.positionCS;

                float2 pixelSize = positionCS.w;
                pixelSize /= float2(_ScaleX, _ScaleY) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

                float3 normalWS = WorldSpaceUINormal();

                float scale = rsqrt(dot(pixelSize, pixelSize));
                scale *= abs(IN.uv0.w) * _GradientScale * (_Sharpness + 1);
                if (UNITY_MATRIX_P[3][3] == 0)
                {
                    float3 viewDir = normalize(GetWorldSpaceViewDir(pos.positionWS));
                    scale = lerp(abs(scale) * (1 - _PerspectiveFilter), scale, abs(dot(normalWS, viewDir)));
                }

                float weight = lerp(_WeightNormal, _WeightBold, bold) / 4.0;
                weight = (weight + _FaceDilate) * _ScaleRatioA * 0.5;

                scale /= 1 + (_OutlineSoftness * _ScaleRatioA * scale);
                float bias    = (0.5 - weight) * scale - 0.5;
                float outline = _OutlineWidth * _ScaleRatioA * 0.5 * scale;

                half4 color = IN.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                if (_UIVertexColorAlwaysGammaSpace)
                    color.rgb = SRGBToLinear(color.rgb);
                #endif

                half4 faceColor = color * _FaceColor;
                faceColor.rgb *= faceColor.a;

                half4 outlineColor = _OutlineColor;
                outlineColor.a *= color.a;
                outlineColor.rgb *= outlineColor.a;
                outlineColor = lerp(faceColor, outlineColor, sqrt(min(1.0, outline * 2)));

                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                half2 maskSoftness = half2(max(_UIMaskSoftnessX, _MaskSoftnessX), max(_UIMaskSoftnessY, _MaskSoftnessY));

                OUT.positionCS   = positionCS;
                OUT.positionWS   = pos.positionWS;
                OUT.normalWS     = normalWS;
                OUT.faceColor    = faceColor;
                OUT.outlineColor = outlineColor;
                OUT.uv           = IN.uv0.xy;
                OUT.param        = half4(scale, bias - outline, bias + outline, bias);
                OUT.mask         = half4(vert.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * maskSoftness + pixelSize.xy));
                OUT.fogFactor    = ComputeFogFactor(positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a * IN.param.x;
                half4 c = IN.faceColor * saturate(d - IN.param.w);

                #ifdef OUTLINE_ON
                    c = lerp(IN.outlineColor, IN.faceColor, saturate(d - IN.param.z));
                    c *= saturate(d - IN.param.y);
                #endif

                #ifdef UNITY_UI_CLIP_RECT
                    half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                    c *= m.x * m.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                    clip(c.a - 0.001);
                #endif

                half3 n = WorldSpaceUIFaceNormal(IN.normalWS, IS_FRONT_VFACE(face, true, false));
                half3 lighting = WorldSpaceUILighting(IN.positionWS, n, IN.positionCS);

                // Colour is premultiplied: light rgb, then fog toward the premultiplied fog colour.
                c.rgb *= lighting;
                c.rgb = MixFogColor(c.rgb, unity_FogColor.rgb * c.a, IN.fogFactor);
                return c;
            }
            ENDHLSL
        }
    }
    Fallback "TextMeshPro/Mobile/Distance Field"
}
