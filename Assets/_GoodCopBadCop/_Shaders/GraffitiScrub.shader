Shader "GoodCopBadCop/GraffitiScrub"
{
    Properties
    {
        [MainTexture] _BaseMap      ("Graffiti Texture",        2D)             = "white" {}
        [MainColor]   _BaseColor    ("Base Tint",               Color)          = (1, 1, 1, 1)

        // --- Scrub Controls ---
        // _ScrubProgress is driven at runtime by GraffitiInteractable via MaterialPropertyBlock.
        // Drag the slider here to preview the dissolve effect in the Editor.
        _ScrubProgress  ("Scrub Progress",      Range(0, 1))                    = 0.0
        _EdgeWidth      ("Edge Width",          Range(0, 0.3))                  = 0.07
        _FoamColor      ("Foam / Wet Edge Color", Color)                        = (0.92, 0.96, 1.0, 1)
        _FoamBrightness ("Foam Brightness",     Range(1, 3))                    = 1.6

        // --- Noise Controls ---
        _NoiseScale     ("Noise Scale",         Range(1, 30))                   = 10.0
        _NoiseSpeed     ("Noise Speed",         Range(0, 2))                    = 0.25
        _WarpStrength   ("Warp Strength",       Range(0, 0.3))                  = 0.08
    }

    SubShader
    {
        Tags
        {
            "Queue"         = "Transparent"
            "RenderType"    = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "GraffitiScrub"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            // Shadow and lighting keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ----------------------------------------------------------------
            // Properties
            // ----------------------------------------------------------------
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half4  _FoamColor;
                half   _ScrubProgress;
                half   _EdgeWidth;
                half   _FoamBrightness;
                half   _NoiseScale;
                half   _NoiseSpeed;
                half   _WarpStrength;
            CBUFFER_END

            // ----------------------------------------------------------------
            // Structs
            // ----------------------------------------------------------------
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ----------------------------------------------------------------
            // Procedural noise helpers (same family as BurningPaper)
            // ----------------------------------------------------------------

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);   // smooth Hermite

                float a = frac(sin(dot(i,               float2(127.1, 311.7))) * 43758.5453);
                float b = frac(sin(dot(i + float2(1,0), float2(127.1, 311.7))) * 43758.5453);
                float c = frac(sin(dot(i + float2(0,1), float2(127.1, 311.7))) * 43758.5453);
                float d = frac(sin(dot(i + float2(1,1), float2(127.1, 311.7))) * 43758.5453);

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Blends between two independent noise snapshots so shapes dissolve
            // and reform in place rather than scrolling — gives a 'pieces lifting off' look.
            float MorphNoise(float2 p, float t)
            {
                float ti = floor(t);
                float tf = frac(t);
                tf = tf * tf * (3.0 - 2.0 * tf);

                float2 o0 = float2(ti * 31.71, ti * 17.43);
                float2 o1 = float2((ti + 1.0) * 31.71, (ti + 1.0) * 17.43);
                return lerp(ValueNoise(p + o0), ValueNoise(p + o1), tf);
            }

            // 3-octave fractal; each octave morphs at a slightly different rate
            // so you get chunky macro-patches with fine crumbly detail.
            float MorphFractal(float2 p, float t)
            {
                float v   = 0.0;
                float amp = 0.5;
                float2 freq = p;
                for (int i = 0; i < 3; i++)
                {
                    v    += amp * MorphNoise(freq, t + float(i) * 0.41);
                    freq *= 2.1;
                    amp  *= 0.5;
                }
                return v;
            }

            // ----------------------------------------------------------------
            // Vertex
            // ----------------------------------------------------------------
            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS   = normalInputs.normalWS;
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            // ----------------------------------------------------------------
            // Fragment
            // ----------------------------------------------------------------
            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float  nt = _Time.y * _NoiseSpeed;

                // --- Domain warp: warps the noise lookup for more organic chunk shapes ---
                float2 warpUV = uv * _NoiseScale;
                float2 warp = float2(
                    MorphFractal(warpUV + float2(1.3, 0.0), nt * 0.65),
                    MorphFractal(warpUV + float2(0.0, 1.7), nt * 0.5)
                ) * 2.0 - 1.0;

                float2 noiseUV = uv * _NoiseScale + warp * _WarpStrength;
                float  noise   = MorphFractal(noiseUV, nt);

                // --- Sample base texture ---
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;

                // --- Scrub threshold ---
                // threshold < 0  → pixel is scrubbed away (transparent hole)
                // threshold in [0, EdgeWidth] → transition band (foam edge)
                // threshold > EdgeWidth → intact paint
                float threshold = noise - _ScrubProgress;

                // Normalized position in the edge band: 0 at hole boundary, 1 at fully intact.
                float edgeT = saturate(threshold / max(_EdgeWidth, 0.001));

                // Crisp alpha cutout: quickly steps from 0 (hole) to 1 (intact) at the boundary.
                half holeMask = saturate(saturate(threshold / max(_EdgeWidth * 0.1, 0.0001)) * 10.0);
                half alpha    = holeMask * texColor.a;

                // --- Foam / wet-edge color ---
                // foamMask peaks at the scrub boundary and fades to 0 on fully intact paint.
                half foamMask = (1.0 - edgeT) * holeMask;
                half3 albedo  = lerp(texColor.rgb, _FoamColor.rgb * _FoamBrightness, foamMask);

                // --- Lighting (Lambert diffuse + shadow + ambient SH) ---
                float3 normalWS = normalize(IN.normalWS);

                // Main directional light with shadow attenuation
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light  mainLight   = GetMainLight(shadowCoord);

                half NdotL    = saturate(dot(normalWS, mainLight.direction));
                half3 diffuse = mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation) * NdotL;

                // Spherical harmonics ambient
                half3 ambient = SampleSH(normalWS);

                half3 finalRGB = albedo * (diffuse + ambient);

                // --- Additional point/spot lights ---
#ifdef _ADDITIONAL_LIGHTS
                uint lightCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < lightCount; ++li)
                {
                    Light light  = GetAdditionalLight(li, IN.positionWS);
                    half  NdotLi = saturate(dot(normalWS, light.direction));
                    finalRGB += albedo * light.color * (light.shadowAttenuation * light.distanceAttenuation) * NdotLi;
                }
#endif

                return half4(finalRGB, alpha);
            }
            ENDHLSL
        }

    }

    FallBack "Hidden/InternalErrorShader"
}
