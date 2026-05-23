Shader "GoodCopBadCop/Smudge"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Smudge"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Parameters set from C#
            float2 _SmudgeOffset;
            float  _SmudgeBlend;

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                // Base sample at the original UV
                half4 base = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // Two ghost copies shifted in opposite directions
                half4 ghostA = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + _SmudgeOffset);
                half4 ghostB = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - _SmudgeOffset);

                // Blend: base at full weight, ghosts blended in by _SmudgeBlend
                half4 result = base + (ghostA + ghostB) * _SmudgeBlend;

                // Renormalize so overall brightness stays consistent
                result /= (1.0 + _SmudgeBlend * 2.0);

                return result;
            }
            ENDHLSL
        }
    }
}
