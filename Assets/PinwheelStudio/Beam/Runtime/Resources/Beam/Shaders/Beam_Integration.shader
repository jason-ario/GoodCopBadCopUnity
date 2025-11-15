Shader "Pinwheel/Beam/Integration"
{
	HLSLINCLUDE

#pragma target 2.0
#pragma editor_sync_compilation
		// Core.hlsl for XR dependencies
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DynamicScaling.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
#include "Beam_Utils.hlsl"

		uniform float4 _BlitScaleBias;
	struct Attributes
	{
		uint vertexID : SV_VertexID;
		UNITY_VERTEX_INPUT_INSTANCE_ID
	};

	struct Varyings
	{
		float4 positionCS : SV_POSITION;
		float2 texcoord : TEXCOORD0;
		UNITY_VERTEX_OUTPUT_STEREO
	};

	Varyings Vert(Attributes input)
	{
		Varyings output;
		UNITY_SETUP_INSTANCE_ID(input);
		UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

		float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
		float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);

		output.positionCS = pos;
		output.texcoord = DYNAMIC_SCALING_APPLY_SCALEBIAS(uv);

		return output;
	}
	ENDHLSL

		SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" }

		Pass
		{
			ZWrite Off ZTest Always Blend Off Cull Off

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment FragCombine

			TEXTURE2D(_BlitTexture);
			float4 _BlitTexture_TexelSize;
			TEXTURE2D(_AccumulationTexture);
			float4 _AccumulationTexture_TexelSize;
			float _Dark;
			float _Bright;

			float4 FragCombine(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				float4 dstColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord.xy, 0);
				float4 srcColor = SAMPLE_TEXTURE2D_X_LOD(_AccumulationTexture, sampler_LinearClamp, input.texcoord.xy, 0);

				float inLow = _Dark * 0.04;
				float inMid = 0.5;
				float inHigh = 1 - _Bright * 0.9;
				srcColor = FilterLevels(srcColor, inLow, inMid, inHigh);
				srcColor = max(float4(0,0,0,0), srcColor);

				float4 color = dstColor + srcColor;
				return color;
			}

			ENDHLSL
		}
	}
		Fallback Off
}