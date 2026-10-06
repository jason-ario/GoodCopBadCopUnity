// Final-image brightness (user preference). Runs after all post-processing, so it never feeds
// bloom/tonemapping. Uses a gamma curve: lifts/lowers midtones while keeping pure black and
// pure white anchored, so it never crushes to black or blows out highlights.
Shader "GoodCopBadCop/ScreenBrightness"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "ScreenBrightness"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _BrightnessGamma;

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
                color.rgb = pow(max(color.rgb, 0.0h), _BrightnessGamma);
                return color;
            }
            ENDHLSL
        }
    }
}
