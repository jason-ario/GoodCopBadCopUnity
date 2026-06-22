Shader "GoodCopBadCop/LowResolution"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "LowResolution"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Size of each pixel block in screen pixels. 1 = native, 2 = half-res, 4 = quarter-res, etc.
            float _PixelSize;

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv         = input.texcoord;
                float  blockSize  = max(_PixelSize, 1.0);
                float2 pixelCount = floor(_ScreenParams.xy / blockSize);

                // Snap UV to the nearest pixel-block centre
                float2 snappedUV = (floor(uv * pixelCount) + 0.5) / pixelCount;

                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, snappedUV);
            }
            ENDHLSL
        }
    }
}
