// Animated TV-static effect for UI Images (uGUI). Drop-in replacement for UI/Default:
// supports Mask/stencil, RectMask2D clipping, vertex color and CanvasGroup alpha.
//
// The sprite is broken up by stepped white noise (screen-space cells), rolling scanlines,
// per-row horizontal jitter, occasional tear bands, an RGB split, and brightness flicker.
// An optional soft "snow" patch can be rendered behind the sprite inside the rect.
Shader "GoodCopBadCop/UIStaticNoise"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Noise)]
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.55
        _NoiseAlphaStrength ("Noise Alpha Breakup", Range(0, 1)) = 0.35
        _NoisePixelSize ("Noise Pixel Size (screen px)", Range(1, 16)) = 2
        _NoiseFPS ("Noise Frames Per Second", Range(1, 60)) = 24
        _NoiseTint ("Noise Tint", Color) = (1,1,1,1)

        [Header(Scanlines)]
        _ScanlineCount ("Scanline Count", Range(0, 600)) = 220
        _ScanlineStrength ("Scanline Strength", Range(0, 1)) = 0.25
        _ScanlineSpeed ("Scanline Scroll Speed", Range(-20, 20)) = 3

        [Header(Distortion)]
        _JitterAmount ("Row Jitter (UV)", Range(0, 0.05)) = 0.006
        _JitterRows ("Jitter Row Count", Range(1, 400)) = 120
        _TearChance ("Tear Chance Per Frame", Range(0, 1)) = 0.12
        _TearHeight ("Tear Band Height (UV)", Range(0, 0.5)) = 0.07
        _TearOffset ("Tear Offset (UV)", Range(0, 0.2)) = 0.04
        _ChromaticOffset ("RGB Split (UV)", Range(0, 0.02)) = 0.004

        [Header(Flicker)]
        _Flicker ("Brightness Flicker", Range(0, 1)) = 0.2

        [Header(Background Snow)]
        _SnowAlpha ("Snow Behind Sprite Alpha", Range(0, 1)) = 0.0
        _SnowSoftness ("Snow Edge Softness", Range(0.01, 0.5)) = 0.25

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            float _NoiseStrength;
            float _NoiseAlphaStrength;
            float _NoisePixelSize;
            float _NoiseFPS;
            fixed4 _NoiseTint;

            float _ScanlineCount;
            float _ScanlineStrength;
            float _ScanlineSpeed;

            float _JitterAmount;
            float _JitterRows;
            float _TearChance;
            float _TearHeight;
            float _TearOffset;
            float _ChromaticOffset;

            float _Flicker;

            float _SnowAlpha;
            float _SnowSoftness;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Stepped time so the static changes in discrete "frames" like a real signal.
                float frame = fmod(floor(_Time.y * _NoiseFPS), 4096.0);
                float2 uv = IN.texcoord;

                // Per-row horizontal jitter.
                float row = floor(uv.y * _JitterRows);
                float jitter = (Hash21(float2(row, frame)) - 0.5) * 2.0 * _JitterAmount;

                // Occasional tear band that shoves a horizontal slice sideways.
                float tearRoll = Hash21(float2(frame, 7.31));
                float tearY = Hash21(float2(frame, 3.17));
                float inTear = step(tearRoll, _TearChance) * step(abs(uv.y - tearY), _TearHeight * 0.5);
                float tear = inTear * (Hash21(float2(frame, 11.13)) - 0.5) * 2.0 * _TearOffset;

                uv.x += jitter + tear;

                // RGB split, flipping direction per frame for a nervous signal feel.
                float split = _ChromaticOffset * (Hash21(float2(frame, 5.71)) > 0.5 ? 1.0 : -1.0);
                half4 texG = tex2D(_MainTex, uv) + _TextureSampleAdd;
                half texR = (tex2D(_MainTex, uv + float2(split, 0)) + _TextureSampleAdd).r;
                half texB = (tex2D(_MainTex, uv - float2(split, 0)) + _TextureSampleAdd).b;
                half aR = (tex2D(_MainTex, uv + float2(split, 0)) + _TextureSampleAdd).a;
                half aB = (tex2D(_MainTex, uv - float2(split, 0)) + _TextureSampleAdd).a;

                half4 tex = half4(texR, texG.g, texB, max(texG.a, max(aR, aB)));
                half4 color = tex * IN.color;

                // Screen-space white noise cells.
                float2 cell = floor(IN.vertex.xy / max(_NoisePixelSize, 1.0));
                float n = Hash21(cell + frame * 17.17);

                // Rolling scanlines.
                float scan = 1.0 - _ScanlineStrength * (0.5 + 0.5 * sin((IN.texcoord.y * _ScanlineCount + _Time.y * _ScanlineSpeed) * 6.28318));

                // Global brightness flicker.
                float flicker = 1.0 - _Flicker * Hash21(float2(frame, 1.23));

                color.rgb = lerp(color.rgb, color.rgb * n * _NoiseTint.rgb * 1.6, _NoiseStrength);
                color.rgb *= scan * flicker;
                color.a *= lerp(1.0, saturate(n * 1.5), _NoiseAlphaStrength);

                // Optional soft snow patch behind the sprite, faded toward the rect edges.
                float2 centered = abs(IN.texcoord - 0.5) * 2.0;
                float edge = 1.0 - smoothstep(1.0 - _SnowSoftness * 2.0, 1.0, max(centered.x, centered.y));
                float snowA = _SnowAlpha * edge * n * scan * IN.color.a;
                half3 snowRGB = _NoiseTint.rgb * n;
                color.rgb = color.rgb * color.a + snowRGB * snowA * (1.0 - color.a);
                color.a = color.a + snowA * (1.0 - color.a);

                #ifdef UNITY_UI_CLIP_RECT
                half clipMask = UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                color.rgb *= clipMask;
                color.a *= clipMask;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                // Output is premultiplied (Blend One OneMinusSrcAlpha).
                return color;
            }
        ENDCG
        }
    }
}
