Shader "Pinwheel/Beam/Filter"
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
			Name "Blur Downsample"
			ZWrite Off ZTest Always Blend Off Cull Off

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment FragBlur

			TEXTURE2D(_BlitTexture);
			float4 _BlitTexture_TexelSize;
			float _BlurRadius;
			float2 _Offset;

			float4 FragBlur(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float radius = _BlurRadius;
				float2 t = _BlitTexture_TexelSize.xy;
				float4 cBL = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord.xy + radius * float2(-t.x, -t.y), 0);
				float4 cB_ = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord.xy + radius * float2(0, -t.y), 0);
				float4 cBR = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord.xy + radius * float2(t.x, -t.y), 0);
				float4 cL_ = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord.xy + radius * float2(-t.x, 0), 0);
				float4 cC_ = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord.xy + radius * float2(0, 0), 0);
				float4 cR_ = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord.xy + radius * float2(t.x, 0), 0);
				float4 cTL = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord.xy + radius * float2(-t.x, t.y), 0);
				float4 cT_ = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord.xy + radius * float2(0, t.y), 0);
				float4 cTR = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord.xy + radius * float2(t.x, t.y), 0);

				float4 color = (cBL + cBR + cTL + cTR) * 0.0625 +
				(cL_ + cT_ + cR_ + cB_) * 0.125 +
				cC_ * 0.25;

				return color;
			}

			ENDHLSL
		}
		
		Pass
		{
			Name "Combine"
			ZWrite Off ZTest Always Blend Off Cull Off

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment FragCombine

			TEXTURE2D(_AccumTexFull);
			TEXTURE2D(_AccumTexHalf);
			TEXTURE2D(_AccumTexFourth);
			TEXTURE2D(_AccumTexEighth);
			TEXTURE2D(_AccumTexSixteenth);

			float4 FragCombine(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				float4 acc0 = SAMPLE_TEXTURE2D_LOD(_AccumTexFull, sampler_LinearClamp, input.texcoord.xy, 0);
				float4 acc1 = SAMPLE_TEXTURE2D_LOD(_AccumTexHalf, sampler_LinearClamp, input.texcoord.xy, 0);
				float4 acc2 = SAMPLE_TEXTURE2D_LOD(_AccumTexFourth, sampler_LinearClamp, input.texcoord.xy, 0);
				float4 acc3 = SAMPLE_TEXTURE2D_LOD(_AccumTexEighth, sampler_LinearClamp, input.texcoord.xy, 0);
				float4 acc4 = SAMPLE_TEXTURE2D_LOD(_AccumTexSixteenth, sampler_LinearClamp, input.texcoord.xy, 0);
				float div = 1.0 / 5.0;
				float4 color =
				div * acc0 +
				div * acc1 +
				div * acc2 +
				div * acc3 +
				div * acc4;

				return color;
			}

			ENDHLSL
		}
	}
	Fallback Off
}