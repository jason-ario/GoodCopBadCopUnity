Shader "GoodCopBadCop/NewspaperHalftone"
{
    // Turns a (black & white) image into printed halftone dots. Light areas are culled to
    // full transparency so whatever is behind the image (the newspaper paper) shows through;
    // only the ink dots are drawn. Works on UI Images (world-space or overlay canvases).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        // --- Ink ---
        _InkColor       ("Ink Color",                     Color)            = (0.08, 0.08, 0.09, 1)
        _InkOpacity     ("Ink Opacity",                   Range(0, 1))      = 0.92

        // --- Halftone Grid ---
        _HalftoneAmount ("Halftone Amount (0 = solid ink, 1 = dots)", Range(0, 1)) = 1
        _DotsAcross     ("Dots Across Image Width",       Range(10, 400))   = 90
        _DotSoftness    ("Dot Edge Softness",             Range(0, 1))      = 0.35
        _MaxDotSize     ("Max Dot Size",                  Range(0.5, 1.2))  = 0.78
        _DotSpacing     ("Dot Spacing (min gap between dots)", Range(0, 0.8)) = 0.2

        // --- Tone ---
        _WhiteCutoff    ("White Cutoff (culls light tones)", Range(0, 1))   = 0.0
        _Contrast       ("Contrast",                      Range(0.25, 3))   = 1.25
        _Gamma          ("Tone Gamma",                    Range(0.3, 3))    = 1.0

        // --- Print Imperfection ---
        _InkNoise       ("Ink Unevenness",                Range(0, 1))      = 0.3
        _InkNoiseScale  ("Ink Unevenness Scale",          Range(1, 200))    = 60
        _Misregister    ("Misregistration (dot jitter)",  Range(0, 0.4))    = 0.06

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
            Name "NewspaperHalftone"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                half4  _InkColor;
                half   _InkOpacity;
                half   _HalftoneAmount;
                float  _DotsAcross;
                half   _DotSoftness;
                half   _MaxDotSize;
                half   _DotSpacing;
                half   _WhiteCutoff;
                half   _Contrast;
                half   _Gamma;
                half   _InkNoise;
                float  _InkNoiseScale;
                half   _Misregister;
            CBUFFER_END

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

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color      = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half3 rgb = tex.rgb * IN.color.rgb;
                half  srcAlpha = tex.a * IN.color.a;

                // --- Tone: darkness = how much ink this area needs ---
                half lum  = dot(rgb, half3(0.299, 0.587, 0.114));
                half dark = saturate(1.0 - lum);
                dark = saturate((dark - 0.5) * _Contrast + 0.5);
                dark = pow(dark, _Gamma);
                // Cull light tones entirely, then re-normalise so the remaining range stays smooth.
                dark = saturate((dark - _WhiteCutoff) / max(1.0 - _WhiteCutoff, 1e-3));

                // --- Halftone grid (aspect-corrected UV space, axis-aligned) ---
                float aspect = _MainTex_TexelSize.z / max(_MainTex_TexelSize.w, 1.0);
                float2 p = IN.uv * float2(_DotsAcross, _DotsAcross / max(aspect, 1e-3));

                float2 cell   = floor(p);
                float2 jitter = (float2(Hash21(cell), Hash21(cell + 17.13)) - 0.5) * _Misregister;
                float2 local  = frac(p) - 0.5 - jitter;
                float  dist   = length(local);

                // Anti-aliased dot edge (fwidth keeps it crisp at any distance/resolution).
                float aa   = max(fwidth(dist), 1e-4);
                float soft = aa * (1.0 + _DotSoftness * 3.0);

                // Dot radius from coverage. _DotSpacing caps the radius so neighbouring dots
                // (one cell apart) always keep a clear gap of _DotSpacing cells between them,
                // even in solid blacks. At 0 spacing, _MaxDotSize ~0.71+ merges blacks into full ink.
                float maxRadius = (_DotSpacing > 0.0) ? (0.5 - _DotSpacing * 0.5 - soft) : 2.0;
                float radius = min(sqrt(dark) * _MaxDotSize, maxRadius);
                float ink  = 1.0 - smoothstep(radius - soft, radius + soft, dist);
                ink *= step(1e-3, dark); // no stray specks in culled whites

                // Blend between the dot screen and solid continuous-tone ink.
                ink = lerp(dark, ink, _HalftoneAmount);

                // --- Uneven ink density, like a cheap press ---
                float n = ValueNoise(IN.uv * _InkNoiseScale) * 0.65 + ValueNoise(IN.uv * _InkNoiseScale * 3.1) * 0.35;
                ink *= lerp(1.0, saturate(0.55 + n * 0.7), _InkNoise);

                half alpha = saturate(ink * _InkOpacity * _InkColor.a * srcAlpha);
                return half4(_InkColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
