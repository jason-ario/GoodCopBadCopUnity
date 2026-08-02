Shader "GoodCopBadCop/GraffitiScrubDecal"
{
    // Depth-reconstruction projector decal — renders the backfaces of a box mesh,
    // reads the depth buffer to find the actual surface position inside the volume,
    // then applies the graffiti texture and scrub-dissolve effect at that point.
    //
    // Assign to a cube MeshRenderer.  Box scale = projection area.
    // _ScrubProgress is driven at runtime by GraffitiInteractable via MaterialPropertyBlock
    // (same as GraffitiScrub.shader — no script changes required).

    Properties
    {
        [MainTexture] _BaseMap      ("Graffiti Texture",          2D)            = "white" {}
        [MainColor]   _BaseColor    ("Base Tint",                 Color)         = (1, 1, 1, 1)

        // --- Scrub Controls ---
        _ScrubProgress  ("Scrub Progress",        Range(0, 1))                   = 0.0
        _EdgeWidth      ("Edge Width",            Range(0, 0.3))                 = 0.07
        _FoamColor      ("Foam / Wet Edge Color", Color)                         = (0.92, 0.96, 1.0, 1)
        _FoamBrightness ("Foam Brightness",       Range(1, 3))                   = 1.6

        // --- Noise Controls ---
        _NoiseScale     ("Noise Scale",           Range(1, 30))                  = 10.0
        _NoiseSpeed     ("Noise Speed",           Range(0, 2))                   = 0.25
        _WarpStrength   ("Warp Strength",         Range(0, 0.3))                 = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Opaque"
            // Drawn at the tail of the OPAQUE queue range (not the Transparent queue).
            // This is required for the volumetric fog (VolumetricFogAndMist2) to affect this
            // decal: that render feature composites into the color buffer at
            // RenderPassEvent.BeforeRenderingTransparents, i.e. AFTER the opaque queue draws
            // but BEFORE the transparent queue draws. Since this decal writes color (via alpha
            // blending) but never writes depth (ZWrite Off), leaving it in the opaque range lets
            // it paint onto the real surface's depth first, then the fog pass composites over it
            // using that same (unmodified) depth buffer — exactly like it does for ordinary
            // opaque geometry. If this stayed in the Transparent queue it would always draw
            // AFTER the fog composite and fully overwrite it, which is why the decal looked like
            // it ignored / sat in front of the fog.
            "Queue"          = "AlphaTest"
        }

        Pass
        {
            Name "GraffitiScrubDecal"

            // Render the inner (back) faces of the box so the decal is visible even
            // when the camera is inside the projection volume.
            Cull  Front
            // Override depth so we composite on top of any geometry inside the box.
            ZTest  Always
            ZWrite Off
            Blend  SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ----------------------------------------------------------------
            // Resources
            // ----------------------------------------------------------------
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // Depth texture — always available in URP (Camera > Depth Texture is on by default).
            #if defined(SHADER_API_GLES)
                TEXTURE2D(_CameraDepthTexture);
                SAMPLER(sampler_CameraDepthTexture);
            #else
                TEXTURE2D_X_FLOAT(_CameraDepthTexture);
            #endif

            // ----------------------------------------------------------------
            // Per-material constants  (MPB overrides _ScrubProgress per-instance)
            // ----------------------------------------------------------------
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenUV   : TEXCOORD0;
                // xyz = view-ray direction in object space; w = view-space Z of vertex
                float4 viewRayOS  : TEXCOORD1;
                float3 camPosOS   : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ----------------------------------------------------------------
            // Noise helpers  (identical to GraffitiScrub.shader)
            // ----------------------------------------------------------------
            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = frac(sin(dot(i,               float2(127.1, 311.7))) * 43758.5453);
                float b = frac(sin(dot(i + float2(1,0), float2(127.1, 311.7))) * 43758.5453);
                float c = frac(sin(dot(i + float2(0,1), float2(127.1, 311.7))) * 43758.5453);
                float d = frac(sin(dot(i + float2(1,1), float2(127.1, 311.7))) * 43758.5453);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float MorphNoise(float2 p, float t)
            {
                float ti = floor(t);
                float tf = frac(t);
                tf = tf * tf * (3.0 - 2.0 * tf);
                float2 o0 = float2(ti * 31.71, ti * 17.43);
                float2 o1 = float2((ti + 1.0) * 31.71, (ti + 1.0) * 17.43);
                return lerp(ValueNoise(p + o0), ValueNoise(p + o1), tf);
            }

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

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.screenUV   = ComputeScreenPos(OUT.positionCS);

                // Build a view-ray in object space so the fragment can reconstruct
                // the world-surface position that the box volume intersects.
                float4 positionVS     = mul(UNITY_MATRIX_MV, IN.positionOS);
                float4x4 viewToOS     = mul(GetWorldToObjectMatrix(), UNITY_MATRIX_I_V);
                OUT.viewRayOS.xyz     = mul((float3x3)viewToOS, -positionVS.xyz);
                OUT.viewRayOS.w       = positionVS.z;   // view-space depth of box vertex
                OUT.camPosOS          = viewToOS._m03_m13_m23;

                return OUT;
            }

            // ----------------------------------------------------------------
            // Fragment
            // ----------------------------------------------------------------
            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Normalize view ray (divide out the view-space Z stored in w).
                IN.viewRayOS.xyz *= rcp(IN.viewRayOS.w);

                float2 screenUV = IN.screenUV.xy / IN.screenUV.w;

                // --- Reconstruct the surface position inside the box from depth ---
                #if defined(SHADER_API_GLES)
                    float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(
                        _CameraDepthTexture, sampler_CameraDepthTexture, screenUV, 0);
                #else
                    float rawDepth = LOAD_TEXTURE2D_X(
                        _CameraDepthTexture, _ScaledScreenParams.xy * screenUV).x;
                #endif

                float  eyeDepth   = LinearEyeDepth(rawDepth, _ZBufferParams);
                float3 positionOS = IN.camPosOS + IN.viewRayOS.xyz * eyeDepth;

                // Discard fragments whose reconstructed surface falls outside the box.
                clip(float3(0.5, 0.5, 0.5) - abs(positionOS.xyz));

                // --- Projected UV ---
                // The box lives in [-0.5, 0.5] local space.
                // XZ maps to the face texture; apply _BaseMap_ST for tiling/offset.
                float2 uv = (positionOS.xz + float2(0.5, 0.5)) * _BaseMap_ST.xy + _BaseMap_ST.zw;

                // --- Noise dissolve (identical logic to GraffitiScrub.shader) ---
                float nt = _Time.y * _NoiseSpeed;

                float2 warpUV = uv * _NoiseScale;
                float2 warp = float2(
                    MorphFractal(warpUV + float2(1.3, 0.0), nt * 0.65),
                    MorphFractal(warpUV + float2(0.0, 1.7), nt * 0.5)
                ) * 2.0 - 1.0;

                float2 noiseUV = uv * _NoiseScale + warp * _WarpStrength;
                float  noise   = MorphFractal(noiseUV, nt);

                // --- Base texture ---
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;

                // --- Scrub threshold ---
                float threshold = noise - _ScrubProgress;
                float edgeT     = saturate(threshold / max(_EdgeWidth, 0.001));
                half  holeMask  = saturate(saturate(threshold / max(_EdgeWidth * 0.1, 0.0001)) * 10.0);
                half  alpha     = holeMask * texColor.a;

                // --- Foam / wet-edge color ---
                half  foamMask = (1.0 - edgeT) * holeMask;
                half3 albedo   = lerp(texColor.rgb, _FoamColor.rgb * _FoamBrightness, foamMask);

                // --- Lighting (Lambert diffuse + shadow + ambient SH) ---
                // Use the projector's local +Y as the approximate surface normal, since the
                // real surface normal isn't available for a depth-reconstructed decal.
                float3 normalWS   = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                float3 positionWS = TransformObjectToWorld(positionOS);

                // Main directional light with shadow attenuation
                float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                Light  mainLight   = GetMainLight(shadowCoord);

                half  NdotL    = saturate(dot(normalWS, mainLight.direction));
                half3 diffuse  = mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation) * NdotL;

                // Spherical harmonics ambient
                half3 ambient = SampleSH(normalWS);

                half3 finalRGB = albedo * (diffuse + ambient);

                // --- Additional point/spot lights ---
#ifdef _ADDITIONAL_LIGHTS
                uint lightCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < lightCount; ++li)
                {
                    Light light  = GetAdditionalLight(li, positionWS);
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
