Shader "GoodCopBadCop/TMP 3D Lit"
{
    // Lit TextMeshPro SDF for 3D TextMeshPro objects (MeshRenderer, not canvas UI).
    // SDF math follows TMP Mobile/Distance Field (face, outline, bold, dilate).
    // Lighting is full URP PBR: main + additional lights (Forward / Forward+), received shadows,
    // light probes, reflection probes, fog. Optional SDF-derived bevel normals pick up real scene
    // lights, and a ShadowCaster pass lets the glyphs cast shadows (enable Cast Shadows on the renderer).
    Properties
    {
        _FaceColor          ("Face Color", Color) = (1,1,1,1)
        _FaceDilate         ("Face Dilate", Range(-1,1)) = 0

        [Toggle(OUTLINE_ON)] _OutlineEnabled ("Outline", Float) = 0
        _OutlineColor       ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth       ("Outline Thickness", Range(0,1)) = 0
        _OutlineSoftness    ("Outline Softness", Range(0,1)) = 0

        [Header(Surface)]
        _Smoothness         ("Smoothness", Range(0,1)) = 0.3
        _Metallic           ("Metallic", Range(0,1)) = 0
        _AmbientStrength    ("Ambient (Light Probes) Strength", Range(0,2)) = 1
        _Emission           ("Self Illumination (unlit mix)", Range(0,1)) = 0
        [Toggle(BEVEL_ON)] _BevelEnabled ("SDF Bevel (lit edges)", Float) = 0
        _BevelStrength      ("Bevel Strength", Range(0,4)) = 1
        [Toggle] _BackfaceFlip ("Flip Normal On Back Faces", Float) = 1

        [Header(Rendering)]
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip (solid, use with ZWrite)", Float) = 0
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("Cull Mode", Float) = 0
        _ColorMask          ("Color Mask", Float) = 15

        [Header(TMP Internal)]
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4  _FaceColor;
            half   _FaceDilate;
            half4  _OutlineColor;
            half   _OutlineWidth;
            half   _OutlineSoftness;
            half   _Smoothness;
            half   _Metallic;
            half   _AmbientStrength;
            half   _Emission;
            half   _BevelStrength;
            half   _BackfaceFlip;
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
        CBUFFER_END

        int _UIVertexColorAlwaysGammaSpace;

        // TMP glyph quads face the viewer along object -Z; atlas U/V follow object +X/+Y.
        float3 TMP3DNormalWS()    { return TransformObjectToWorldNormal(float3(0, 0, -1)); }
        float3 TMP3DTangentWS()   { return TransformObjectToWorldDir(float3(1, 0, 0)); }
        float3 TMP3DBitangentWS() { return TransformObjectToWorldDir(float3(0, 1, 0)); }

        float TMP3DWeight(float uvW)
        {
            float bold = step(uvW, 0);
            float weight = lerp(_WeightNormal, _WeightBold, bold) / 4.0;
            return (weight + _FaceDilate) * _ScaleRatioA * 0.5;
        }
        ENDHLSL

        Pass
        {
            Name "TMP3DLitForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_CullMode]
            ZWrite [_ZWrite]
            ZTest LEqual
            Blend One OneMinusSrcAlpha
            ColorMask [_ColorMask]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #pragma shader_feature_local _ OUTLINE_ON
            #pragma shader_feature_local_fragment _ BEVEL_ON
            #pragma shader_feature_local_fragment _ _ALPHATEST_ON

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float3 positionWS   : TEXCOORD2;
                float3 normalWS     : TEXCOORD3;
                float3 tangentWS    : TEXCOORD4;
                float3 bitangentWS  : TEXCOORD5;
                half   fogFactor    : TEXCOORD6;
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half3  vertexLight  : TEXCOORD7;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float4 vert = IN.positionOS;
                vert.x += _VertexOffsetX;
                vert.y += _VertexOffsetY;

                VertexPositionInputs pos = GetVertexPositionInputs(vert.xyz);
                float4 positionCS = pos.positionCS;
                float3 normalWS = TMP3DNormalWS();

                float2 pixelSize = positionCS.w;
                pixelSize /= float2(_ScaleX, _ScaleY) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

                float scale = rsqrt(dot(pixelSize, pixelSize));
                scale *= abs(IN.uv0.w) * _GradientScale * (_Sharpness + 1);
                if (UNITY_MATRIX_P[3][3] == 0)
                {
                    float3 viewDir = normalize(GetWorldSpaceViewDir(pos.positionWS));
                    scale = lerp(abs(scale) * (1 - _PerspectiveFilter), scale, abs(dot(normalWS, viewDir)));
                }

                float weight = TMP3DWeight(IN.uv0.w);

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

                OUT.positionCS   = positionCS;
                OUT.positionWS   = pos.positionWS;
                OUT.normalWS     = normalWS;
                OUT.tangentWS    = TMP3DTangentWS();
                OUT.bitangentWS  = TMP3DBitangentWS();
                OUT.faceColor    = faceColor;
                OUT.outlineColor = outlineColor;
                OUT.uv           = IN.uv0.xy;
                OUT.param        = half4(scale, bias - outline, bias + outline, bias);
                OUT.fogFactor    = ComputeFogFactor(positionCS.z);
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                OUT.vertexLight  = VertexLighting(pos.positionWS, normalWS);
                #endif
                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a * IN.param.x;
                half4 c = IN.faceColor * saturate(d - IN.param.w);

                #ifdef OUTLINE_ON
                    c = lerp(IN.outlineColor, IN.faceColor, saturate(d - IN.param.z));
                    c *= saturate(d - IN.param.y);
                #endif

                #ifdef _ALPHATEST_ON
                    clip(c.a - 0.5);
                #else
                    clip(c.a - 0.001);
                #endif

                // SDF colour is premultiplied; light the straight colour, re-premultiply afterwards.
                half3 albedo = c.rgb / max(c.a, 1e-4h);
                #ifdef _ALPHATEST_ON
                    c.a = 1;
                #endif

                bool isFront = IS_FRONT_VFACE(face, true, false);
                half side = (_BackfaceFlip > 0.5h && !isFront) ? -1.0h : 1.0h;
                float3 N = normalize(IN.normalWS) * side;
                float3 n = N;

                #ifdef BEVEL_ON
                    // Treat the distance field as a height map: its gradient tilts the normal at glyph edges.
                    float2 texel = float2(1.0 / _TextureWidth, 1.0 / _TextureHeight);
                    half ax0 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv - float2(texel.x, 0)).a;
                    half ax1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv + float2(texel.x, 0)).a;
                    half ay0 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv - float2(0, texel.y)).a;
                    half ay1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv + float2(0, texel.y)).a;
                    half2 g = half2(ax1 - ax0, ay1 - ay0) * (_GradientScale * 0.5h * _BevelStrength);
                    half3 ts = normalize(half3(-g.x, -g.y, 1.0h));
                    n = normalize(normalize(IN.tangentWS) * ts.x + normalize(IN.bitangentWS) * ts.y + N * ts.z);
                #endif

                InputData inputData = (InputData)0;
                inputData.positionWS              = IN.positionWS;
                inputData.normalWS                = n;
                inputData.viewDirectionWS         = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord             = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord                = IN.fogFactor;
                inputData.bakedGI                 = max(SampleSH(n) * _AmbientStrength, 0);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask              = half4(1, 1, 1, 1);
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                inputData.vertexLighting          = IN.vertexLight;
                #endif

                SurfaceData surface = (SurfaceData)0;
                surface.albedo     = albedo;
                surface.metallic   = _Metallic;
                surface.smoothness = _Smoothness;
                surface.occlusion  = 1;
                surface.alpha      = 1;
                surface.normalTS   = half3(0, 0, 1);

                half3 col = UniversalFragmentPBR(inputData, surface).rgb;
                col = lerp(col, albedo, _Emission);

                col *= c.a;
                col = MixFogColor(col, unity_FogColor.rgb * c.a, IN.fogFactor);
                return half4(col, c.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ OUTLINE_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float4 uv0        : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half2  clipParams : TEXCOORD1;   // SDF threshold(x), visibility(y)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings shadowVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionOS = IN.positionOS.xyz;
                positionOS.x += _VertexOffsetX;
                positionOS.y += _VertexOffsetY;

                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS   = TMP3DNormalWS();

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                // Glyph edge sits where atlas alpha == 0.5 - weight (outline pushes it outward).
                float threshold = 0.5 - TMP3DWeight(IN.uv0.w);
                #ifdef OUTLINE_ON
                    threshold -= _OutlineWidth * _ScaleRatioA * 0.5;
                #endif

                OUT.positionCS = positionCS;
                OUT.uv         = IN.uv0.xy;
                OUT.clipParams = half2(threshold, IN.color.a * _FaceColor.a);
                return OUT;
            }

            half4 shadowFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                clip(IN.clipParams.y - 0.01);
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                clip(a - IN.clipParams.x);
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback "TextMeshPro/Mobile/Distance Field"
}
