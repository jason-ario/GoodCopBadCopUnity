Shader "GoodCopBadCop/GlassCrackOverlay"
{
    Properties
    {
        // Driven at runtime via MaterialPropertyBlock by BreakableGlassController.
        // Not marked [PerRendererData] so the slider stays editable in the Inspector for
        // previewing crack stages on the material asset directly; runtime MPB overrides still win.
        _CrackProgress ("Crack Progress", Range(0, 1)) = 0

        [NoScaleOffset] _CrackTex ("Crack Texture (RGBA, alpha = crack shape)", 2D) = "white" {}
        _CrackColor    ("Tint Color",          Color)               = (1, 1, 1, 1)
        _MaxRevealRadius ("Max Reveal Radius", Range(0.3, 1.0))      = 0.75
        _EdgeSoftness    ("Reveal Edge Softness", Range(0.001, 0.3)) = 0.05
    }

    SubShader
    {
        // Renders transparently on top of the existing glass material.
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent+1"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull   Off          // Double-sided so it shows from both interior and exterior.

        Pass
        {
            Name "GlassCrackOverlay"

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CrackTex);
            SAMPLER(sampler_CrackTex);

            CBUFFER_START(UnityPerMaterial)
                float  _CrackProgress;
                float4 _CrackColor;
                float  _MaxRevealRadius;
                float  _EdgeSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Early-out when undamaged: avoids any cost on the intact glass.
                if (_CrackProgress < 0.001)
                    return half4(0, 0, 0, 0);

                float2 d   = IN.uv - float2(0.5, 0.5);
                float  r   = length(d);

                // Reveal radius expands from the center as progress increases, until the
                // whole texture (including corners) is uncovered at progress = 1.
                float  revealR = _CrackProgress * _MaxRevealRadius;
                float  radialMask = 1.0 - smoothstep(revealR, revealR + _EdgeSoftness, r);

                half4 tex = SAMPLE_TEXTURE2D(_CrackTex, sampler_CrackTex, IN.uv);

                half3 color = tex.rgb * _CrackColor.rgb;
                half  alpha = tex.a * _CrackColor.a * radialMask;

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
