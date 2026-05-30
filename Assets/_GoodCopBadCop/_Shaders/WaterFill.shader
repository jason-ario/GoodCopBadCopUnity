Shader "GoodCopBadCop/WaterFill"
{
    // Apply to a closed mesh that represents the water volume.
    // Cull Front renders only interior (back) faces, which — combined
    // with a clip above the fill line — creates the volumetric fill effect.
    // A second SRPDefaultUnlit pass adds the water-surface cap at the cut plane.
    //
    // Setup:
    //   _FillMin / _FillMax  — object-space Y bounds of the mesh
    //                          (Unity default sphere/cube: -0.5 to 0.5)
    //   _FillAmount          — 0 = empty, 1 = full

    Properties
    {
        // ── Fill ──────────────────────────────────────────────────────────
        _FillAmount("Fill Amount", Range(0, 1)) = 0.5
        _FillMin("Fill Min Y (Object Space)", Float) = -0.5
        _FillMax("Fill Max Y (Object Space)", Float) = 0.5

        // ── Colors ────────────────────────────────────────────────────────
        [Space(4)]
        _DeepColor("Deep Color", Color) = (0.04, 0.20, 0.40, 0.95)
        _SurfaceColor("Surface Color", Color) = (0.13, 0.50, 0.76, 0.88)
        _FoamColor("Foam Color", Color) = (0.88, 0.96, 1.0, 1.0)

        // ── Texture ───────────────────────────────────────────────────────
        [Space(4)]
        _MainTex("Surface Texture", 2D) = "white" {}
        _TextureTiling("Texture Tiling", Float) = 1.0
        _TextureScrollX("Scroll X", Float) = 0.03
        _TextureScrollZ("Scroll Z", Float) = 0.02
        _TextureStrength("Texture Blend", Range(0, 1)) = 0.35

        // ── Waves ─────────────────────────────────────────────────────────
        [Space(4)]
        _WaveHeight("Wave Height", Range(0, 0.15)) = 0.025
        _WaveSpeed("Wave Speed", Float) = 1.5
        _WaveFreqX("Wave Frequency X", Float) = 6.0
        _WaveFreqZ("Wave Frequency Z", Float) = 5.0

        // ── Surface look ──────────────────────────────────────────────────
        [Space(4)]
        _FoamWidth("Foam Width", Range(0, 0.1)) = 0.015
        _SurfaceBandDepth("Surface Depth Band", Range(0, 0.3)) = 0.06
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        // ── Pass 1: Water body (interior back-faces) ──────────────────────
        Pass
        {
            Name "WaterInterior"
            Tags { "LightMode" = "UniversalForward" }

            Cull  Front
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float  _FillAmount;
                float  _FillMin;
                float  _FillMax;
                float4 _DeepColor;
                float4 _SurfaceColor;
                float4 _FoamColor;
                float  _TextureTiling;
                float  _TextureScrollX;
                float  _TextureScrollZ;
                float  _TextureStrength;
                float  _WaveHeight;
                float  _WaveSpeed;
                float  _WaveFreqX;
                float  _WaveFreqZ;
                float  _FoamWidth;
                float  _SurfaceBandDepth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Two-axis sine × cosine wave in object space.
            float ComputeWave(float3 posOS)
            {
                float wx = sin(posOS.x * _WaveFreqX + _Time.y * _WaveSpeed);
                float wz = cos(posOS.z * _WaveFreqZ + _Time.y * _WaveSpeed * 0.73);
                return wx * wz * _WaveHeight;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionOS = IN.positionOS.xyz;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float fillHeight   = lerp(_FillMin, _FillMax, _FillAmount);
                float wave         = ComputeWave(IN.positionOS);
                float waterSurface = fillHeight + wave;

                // Discard everything above the animated fill line.
                clip(waterSurface - IN.positionOS.y);

                float depth        = waterSurface - IN.positionOS.y;
                float volumeHeight = max(_FillMax - _FillMin, 0.0001);

                // Depth gradient: surface color → deep color.
                float depthT     = saturate(depth / volumeHeight);
                half4 waterColor = lerp(_SurfaceColor, _DeepColor, depthT);

                // Top-down scrolling texture, visible near the surface.
                float2 texUV    = IN.positionOS.xz * _TextureTiling
                                + float2(_TextureScrollX, _TextureScrollZ) * _Time.y;
                float4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, texUV);
                float  surfaceT = 1.0 - smoothstep(0.0, _SurfaceBandDepth, depth);
                waterColor.rgb  = lerp(waterColor.rgb,
                                       waterColor.rgb * texColor.rgb * 2.0,
                                       _TextureStrength * surfaceT);

                // Foam band at the waterline.
                float foamMask = 1.0 - smoothstep(0.0, _FoamWidth, depth);
                waterColor     = lerp(waterColor, _FoamColor, foamMask * _FoamColor.a);

                return waterColor;
            }
            ENDHLSL
        }

        // ── Pass 2: Water surface cap (exterior front-faces near fill line) ─
        Pass
        {
            Name "WaterSurface"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull  Back
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float  _FillAmount;
                float  _FillMin;
                float  _FillMax;
                float4 _DeepColor;
                float4 _SurfaceColor;
                float4 _FoamColor;
                float  _TextureTiling;
                float  _TextureScrollX;
                float  _TextureScrollZ;
                float  _TextureStrength;
                float  _WaveHeight;
                float  _WaveSpeed;
                float  _WaveFreqX;
                float  _WaveFreqZ;
                float  _FoamWidth;
                float  _SurfaceBandDepth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float ComputeWave(float3 posOS)
            {
                float wx = sin(posOS.x * _WaveFreqX + _Time.y * _WaveSpeed);
                float wz = cos(posOS.z * _WaveFreqZ + _Time.y * _WaveSpeed * 0.73);
                return wx * wz * _WaveHeight;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionOS = IN.positionOS.xyz;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float fillHeight   = lerp(_FillMin, _FillMax, _FillAmount);
                float wave         = ComputeWave(IN.positionOS);
                float waterSurface = fillHeight + wave;

                // Only keep front-face pixels inside the surface band.
                clip(waterSurface - IN.positionOS.y);
                clip(IN.positionOS.y - (waterSurface - _SurfaceBandDepth));

                float depth    = waterSurface - IN.positionOS.y;
                float surfaceT = 1.0 - smoothstep(0.0, _SurfaceBandDepth, depth);

                // Scrolling texture on the XZ plane.
                float2 texUV    = IN.positionOS.xz * _TextureTiling
                                + float2(_TextureScrollX, _TextureScrollZ) * _Time.y;
                float4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, texUV);

                half4 color = _SurfaceColor;
                color.rgb   = lerp(color.rgb, color.rgb * texColor.rgb * 2.0, _TextureStrength);
                color.a    *= surfaceT;

                // Foam at the edge of the surface cap.
                float foamMask = 1.0 - smoothstep(0.0, _FoamWidth, depth);
                color          = lerp(color, _FoamColor, foamMask * _FoamColor.a);

                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
