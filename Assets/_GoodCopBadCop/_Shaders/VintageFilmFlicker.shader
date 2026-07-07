Shader "GoodCopBadCop/VintageFilmFlicker"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        // --- Flicker ---
        _FlickerSpeed     ("Flicker Speed",     Range(0, 30))  = 8.0
        _FlickerIntensity ("Flicker Intensity", Range(0, 1))   = 0.35
        _FlickerMinAlpha  ("Flicker Min Alpha", Range(0, 1))   = 0.05

        // --- Color Burn ---
        _BurnColor        ("Burn Color",        Color)         = (1, 0.45, 0.1, 1)
        _BurnIntensity    ("Burn Intensity",     Range(0, 1))   = 0.4
        _BurnSpeed        ("Burn Speed",         Range(0, 30))  = 5.0

        // --- Stencil (required for Canvas UI masking) ---
        _StencilComp      ("Stencil Comparison",  Float)       = 8
        _Stencil          ("Stencil ID",          Float)       = 0
        _StencilOp        ("Stencil Operation",   Float)       = 0
        _StencilWriteMask ("Stencil Write Mask",  Float)       = 255
        _StencilReadMask  ("Stencil Read Mask",   Float)       = 255
        _ColorMask        ("Color Mask",          Float)       = 15
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
            Name "VintageFilmFlicker"
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
                half   _FlickerSpeed;
                half   _FlickerIntensity;
                half   _FlickerMinAlpha;
                half4  _BurnColor;
                half   _BurnIntensity;
                half   _BurnSpeed;
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

            // Returns a pseudo-random float in [0,1] stepped at the given rate.
            // Offset seeds each call so opacity and color drift independently.
            float SteppedRand(float t, float speed, float seed)
            {
                return frac(sin(floor(t * speed) * 127.1 + seed) * 43758.5453);
            }

            // Three independently-stepped values combined for irregular cadence.
            float FilmFlicker(float t, float speed, float seed)
            {
                float f1 = SteppedRand(t, speed,        seed);
                float f2 = SteppedRand(t, speed * 0.71, seed + 17.3);
                float f3 = SteppedRand(t, speed * 1.39, seed + 53.7);
                return f1 * 0.5 + f2 * 0.3 + f3 * 0.2;
            }

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

            half4 frag(Varyings IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                texColor      *= IN.color;

                // --- Opacity flicker ---
                float alphaFlicker = FilmFlicker(_Time.y, _FlickerSpeed, 0.0);
                float minA         = lerp(1.0, _FlickerMinAlpha, _FlickerIntensity);
                texColor.a        *= lerp(minA, 1.0, alphaFlicker);

                // --- Color burn flicker ---
                // Independent signal so color shifts don't always sync with dips.
                float colorFlicker = FilmFlicker(_Time.y, _BurnSpeed, 311.7);
                texColor.rgb       = lerp(texColor.rgb,
                                          texColor.rgb * _BurnColor.rgb,
                                          colorFlicker * _BurnIntensity);

                return texColor;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/InternalErrorShader"
}
