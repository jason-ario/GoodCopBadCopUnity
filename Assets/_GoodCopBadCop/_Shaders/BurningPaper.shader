Shader "GoodCopBadCop/BurningPaper"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        // --- Burn Controls ---
        _BurnProgress   ("Burn Progress",          Range(0, 1))    = 0.0
        _BurnEdgeWidth  ("Burn Edge Width",        Range(0, 0.15)) = 0.04
        _BurnColor      ("Burn Edge Color",        Color)          = (1, 0.35, 0.0, 1)
        _CharColor      ("Char / Ember Color",     Color)          = (0.08, 0.04, 0.02, 1)

        // --- Noise Controls ---
        _NoiseScale     ("Noise Tile Scale",       Range(1, 30))   = 8.0
        _NoiseSpeed     ("Noise Animation Speed",  Range(0, 4))    = 0.9
        _WarpStrength   ("Warp Strength",          Range(0, 0.3))  = 0.08

        // --- Wave Distortion ---
        _WaveAmplitude  ("Wave Amplitude",         Range(0, 0.05)) = 0.012
        _WaveFrequency  ("Wave Frequency",         Range(1, 40))   = 18.0
        _WaveSpeed      ("Wave Speed",             Range(0, 8))    = 3.5

        // --- Glitch Effect ---
        // _GlitchStrength is driven at runtime by LogoMaterialController.
        // Set it directly on the material to preview the effect in the Editor.
        _GlitchStrength     ("Glitch Strength",          Range(0, 1))    = 0.0

        // --- Stencil (required for Canvas UI masking) ---
        _StencilComp    ("Stencil Comparison",     Float)          = 8
        _Stencil        ("Stencil ID",             Float)          = 0
        _StencilOp      ("Stencil Operation",      Float)          = 0
        _StencilWriteMask ("Stencil Write Mask",   Float)          = 255
        _StencilReadMask  ("Stencil Read Mask",    Float)          = 255
        _ColorMask      ("Color Mask",             Float)          = 15
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
            Ref   [_Stencil]
            Comp  [_StencilComp]
            Pass  [_StencilOp]
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
            Name "BurningPaper"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ----------------------------------------------------------------
            // Properties
            // ----------------------------------------------------------------
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _BurnColor;
                half4  _CharColor;
                half   _BurnProgress;
                half   _BurnEdgeWidth;
                half   _NoiseScale;
                half   _NoiseSpeed;
                half   _WarpStrength;
                half   _WaveAmplitude;
                half   _WaveFrequency;
                half   _WaveSpeed;
                half   _GlitchStrength;
            CBUFFER_END

            // ----------------------------------------------------------------
            // Structs
            // ----------------------------------------------------------------
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
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ----------------------------------------------------------------
            // Procedural noise helpers
            // ----------------------------------------------------------------

            // Low-cost hash → pseudo-random float2 from float2
            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                           dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453123);
            }

            // Gradient noise (value noise variant) — returns [0,1]
            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);   // smoothstep

                float a = frac(sin(dot(i,               float2(127.1, 311.7))) * 43758.5453);
                float b = frac(sin(dot(i + float2(1,0), float2(127.1, 311.7))) * 43758.5453);
                float c = frac(sin(dot(i + float2(0,1), float2(127.1, 311.7))) * 43758.5453);
                float d = frac(sin(dot(i + float2(1,1), float2(127.1, 311.7))) * 43758.5453);

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 3-octave fractal noise for organic burn shapes
            float FractalNoise(float2 p)
            {
                float v   = 0.0;
                float amp = 0.5;
                float2 freq = p;
                for (int i = 0; i < 3; i++)
                {
                    v    += amp * ValueNoise(freq);
                    freq *= 2.1;
                    amp  *= 0.5;
                }
                return v;
            }

            // Morphing noise: blends between two fully different noise states at
            // the same UV position, so shapes dissolve and reform rather than scroll.
            // 't' is a continuous time value; each integer crossing swaps to a new target.
            float MorphNoise(float2 p, float t)
            {
                float ti = floor(t);
                float tf = frac(t);
                tf = tf * tf * (3.0 - 2.0 * tf);   // smoothstep interpolation

                // Large irrational offsets push each time step into a completely
                // distinct region of noise space — no repetition.
                float2 o0 = float2(ti * 31.71, ti * 17.43);
                float2 o1 = float2((ti + 1.0) * 31.71, (ti + 1.0) * 17.43);
                return lerp(ValueNoise(p + o0), ValueNoise(p + o1), tf);
            }

            // 3-octave fractal built on MorphNoise — each octave morphs at a
            // slightly different rate, giving richer independent shape variation.
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
            // Glitch helpers
            // ----------------------------------------------------------------

            // Returns a pseudo-random float in [0,1] for a given integer seed.
            // Used only for per-frame visual pattern variation (scanlines, chroma).
            float GlitchRand(float seed)
            {
                return frac(sin(seed * 127.1 + 311.7) * 43758.5453123);
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
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color      = IN.color;
                return OUT;
            }

            // ----------------------------------------------------------------
            // Fragment
            // ----------------------------------------------------------------
            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                float  t  = _Time.y;
                float  nt = t * _NoiseSpeed;

                // --- Glitch: strength is written each frame by LogoMaterialController ---
                float  glitch   = _GlitchStrength;

                // Sub-frame seeds so each glitch characteristic looks independent
                float  gSeed0   = floor(t * 23.0);   // scanline band seed (fast)
                float  gSeed1   = floor(t * 11.0);   // block shift seed (medium)
                float  gSeed2   = floor(t * 5.0);    // chromatic seed (slow)

                // --- Wave distortion: shimmering heat-haze on the texture sample UV ---
                float2 waveUV = uv;
                waveUV.x += sin(uv.y * _WaveFrequency + t * _WaveSpeed)             * _WaveAmplitude;
                waveUV.y += sin(uv.x * _WaveFrequency * 0.7 + t * _WaveSpeed * 1.3) * _WaveAmplitude * 0.6;

                // --- Glitch: horizontal scanline block shift ---
                // Divide UV-Y into coarse bands; each band independently shifts in X.
                float  bandSize   = lerp(0.15, 0.04, GlitchRand(gSeed1));
                float  bandIndex  = floor(uv.y / bandSize);
                float  bandShift  = (GlitchRand(bandIndex + gSeed1 * 7.3) * 2.0 - 1.0) * 0.08;
                // Only a subset of bands actually shift (threshold > 0.6 → ~40% of bands)
                float  bandActive = step(0.6, GlitchRand(bandIndex + gSeed0 * 3.7));
                waveUV.x += bandShift * bandActive * glitch;

                // --- Domain warp: morphing warp field, no UV translation ---
                float2 warpUV = uv * _NoiseScale;
                float2 warp   = float2(
                    MorphFractal(warpUV + float2(1.3, 0.0), nt * 0.65),
                    MorphFractal(warpUV + float2(0.0, 1.7), nt * 0.5)
                ) * 2.0 - 1.0;

                // Burn noise: shapes morph in place — no positional drift
                float2 noiseUV = uv * _NoiseScale + warp * _WarpStrength;
                float  noise   = MorphFractal(noiseUV, nt);

                // --- Sample base texture with wave-displaced UV ---
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, waveUV);
                texColor      *= IN.color;  // UI vertex color tint

                // --- Glitch: chromatic aberration on RGB channels ---
                float  chromaShift = (GlitchRand(gSeed2) * 2.0 - 1.0) * 0.025 * glitch;
                half   rChannel    = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, waveUV + float2( chromaShift, 0.0)).r;
                half   bChannel    = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, waveUV + float2(-chromaShift, 0.0)).b;
                texColor.r = lerp(texColor.r, rChannel * IN.color.r, glitch);
                texColor.b = lerp(texColor.b, bChannel * IN.color.b, glitch);

                // --- Glitch: brief brightness flicker ---
                float  flicker  = lerp(1.0, GlitchRand(gSeed0 + 0.5) * 0.6 + 0.7, glitch);
                texColor.rgb   *= flicker;

                // --- Burn mask ---
                float burnThreshold = noise - _BurnProgress;

                // Normalized position within the burn edge band [0=hole edge, 1=unburned]
                float edgeT = saturate(burnThreshold / max(_BurnEdgeWidth, 0.001));

                // Alpha: punch a transparent hole where burnThreshold < 0
                half holeMask = saturate(burnThreshold / max(_BurnEdgeWidth * 0.1, 0.0001));
                holeMask      = saturate(holeMask * 10.0);  // crisp cutoff
                half alpha    = holeMask * texColor.a;

                // Only apply edge coloring within the burn band; beyond it use original
                half3 charZone  = _CharColor.rgb;
                half3 glowZone  = _BurnColor.rgb;
                half3 origZone  = texColor.rgb;

                half  glowBlend = saturate(edgeT * 2.0);
                half3 edgeColor = lerp(charZone, glowZone, glowBlend);
                half  origBlend = saturate((edgeT - 0.5) * 2.0);
                half3 finalRGB  = lerp(edgeColor, origZone, origBlend);

                // Additive ember glow brightest at the mid-edge
                half glowIntensity = sin(saturate(edgeT) * PI) * 0.55;
                // Only show glow where there is a burn edge (not on fully unburned pixels)
                glowIntensity     *= saturate(1.0 - edgeT);
                finalRGB += _BurnColor.rgb * glowIntensity;

                return half4(finalRGB, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/InternalErrorShader"
}
