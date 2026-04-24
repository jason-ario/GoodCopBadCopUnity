Shader "Vefects/SH_Vefects_URP_VFX_Vignette_Drunk_02"
{
	Properties
	{
		_Color01("Color 01", Color) = (0.2941177,0.2666667,0.1294118,0)
		_Color02("Color 02", Color) = (0.1960784,0.2627451,0.1490196,0)

		_OpacityNoiseLerp("Opacity Noise Lerp", Float) = 0
		_OpacityMultiply("Opacity Multiply", Float) = 1

		_VignetteInner("Vignette Inner", Range(0,1)) = 0.2
		_VignetteOuter("Vignette Outer", Range(0,1)) = 0.75
		_VignettePower("Vignette Power", Range(0.1,5)) = 1

		_NormalDistortionMultiply("Normal Distortion Multiply", Float) = 1
		_DistortionAxis("Distortion Axis", Vector) = (0,1,0,0)

		[Header(Noise)]
		_NoiseTexture("Noise Texture", 2D) = "white" {}
		_NoiseTextureIndividualScale("Noise Texture Individual Scale", Vector) = (1,1,0,0)
		_NoiseTextureUniformScale("Noise Texture Uniform Scale", Float) = 1
		_NoiseTextureChaosSpeed("Noise Texture Chaos Speed", Float) = 1

		[Header(Normal)]
		_NormalTexture("Normal Texture", 2D) = "bump" {}
		_NormalTextureIndividualScale("Normal Texture Individual Scale", Vector) = (1,1,0,0)
		_NormalTextureUniformScale("Normal Texture Uniform Scale", Float) = 1
		_NormalTextureChaosSpeed("Normal Texture Chaos Speed", Float) = 1

		[Header(Rendering)]
		_Cull1("Cull", Float) = 2
		_ZTest1("ZTest", Float) = 2
	}

	SubShader
	{
		Tags
		{
			"RenderPipeline"="UniversalPipeline"
			"RenderType"="Transparent"
			"Queue"="Transparent"
			"UniversalMaterialType"="Unlit"
		}

		Cull [_Cull1]
		ZWrite Off
		ZTest [_ZTest1]
		Blend SrcAlpha OneMinusSrcAlpha

		Pass
		{
			Name "Forward"
			Tags { "LightMode"="UniversalForwardOnly" }

			HLSLPROGRAM

			#pragma target 3.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			#define REQUIRE_OPAQUE_TEXTURE 1

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
				float4 uv0 : TEXCOORD0;
				float4 uv1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float4 screenPos : TEXCOORD0;
				float4 uv0 : TEXCOORD1;
				float4 uv1 : TEXCOORD2;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
				float4 _Color01;
				float4 _Color02;

				float _OpacityNoiseLerp;
				float _OpacityMultiply;

				float _VignetteInner;
				float _VignetteOuter;
				float _VignettePower;

				float _NormalDistortionMultiply;
				float2 _DistortionAxis;

				float2 _NoiseTextureIndividualScale;
				float _NoiseTextureUniformScale;
				float _NoiseTextureChaosSpeed;

				float2 _NormalTextureIndividualScale;
				float _NormalTextureUniformScale;
				float _NormalTextureChaosSpeed;

				float _Cull1;
				float _ZTest1;
			CBUFFER_END

			sampler2D _NoiseTexture;
			sampler2D _NormalTexture;

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);

				output.positionCS = vertexInput.positionCS;
				output.screenPos = ComputeScreenPos(vertexInput.positionCS);
				output.uv0 = input.uv0;
				output.uv1 = input.uv1;

				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float2 screenUV = input.screenPos.xy / input.screenPos.w;

				float2 baseUV = input.uv0.xy;
				float randomUV = input.uv1.z;

				// -------------------------
				// ORIGINAL ANIMATED NORMAL DISTORTION
				// -------------------------

				float normalTime = _NormalTextureChaosSpeed * _TimeParameters.x;

				float2 normalUV =
					((baseUV * _NormalTextureIndividualScale) * _NormalTextureUniformScale)
					+ randomUV;

				float2 normalPanner1 = normalTime * float2(0.1, 0.1) + normalUV;
				float2 normalPanner2 = normalTime * float2(-0.1, -0.1) + normalUV + float2(0.423, 0.377);
				float2 normalPanner3 = normalTime * float2(-0.1, 0.1) + normalUV + float2(0.777, 0.123);
				float2 normalPanner4 = normalTime * float2(0.1, -0.1) + normalUV + float2(0.651, 0.123);

				float4 normalNoise =
				(
					tex2D(_NormalTexture, normalPanner1) +
					tex2D(_NormalTexture, normalPanner2) +
					tex2D(_NormalTexture, normalPanner3) +
					tex2D(_NormalTexture, normalPanner4)
				) * 0.25;

				float2 distortion =
					((normalNoise.rg - 0.5) * 2.0)
					* (_NormalDistortionMultiply * saturate(input.uv0.z))
					* _DistortionAxis;

				float2 sceneUV = screenUV + distortion;

				float4 sceneColor = float4(SampleSceneColor(sceneUV), 1.0);

				// -------------------------
				// ORIGINAL ANIMATED COLOR NOISE
				// -------------------------

				float noiseTime = _NoiseTextureChaosSpeed * _TimeParameters.x;

				float2 noiseUV =
					((baseUV * _NoiseTextureIndividualScale) * _NoiseTextureUniformScale)
					+ randomUV;

				float2 noisePanner1 = noiseTime * float2(0.1, 0.1) + noiseUV;
				float2 noisePanner2 = noiseTime * float2(-0.1, -0.1) + noiseUV + float2(0.423, 0.377);
				float2 noisePanner3 = noiseTime * float2(-0.1, 0.1) + noiseUV + float2(0.777, 0.123);
				float2 noisePanner4 = noiseTime * float2(0.1, -0.1) + noiseUV + float2(0.651, 0.123);

				float4 noiseValue = saturate(
				(
					tex2D(_NoiseTexture, noisePanner1) +
					tex2D(_NoiseTexture, noisePanner2) +
					tex2D(_NoiseTexture, noisePanner3) +
					tex2D(_NoiseTexture, noisePanner4)
				) * 0.25);

				float4 effectColor = lerp(_Color01, _Color02, noiseValue);

				float4 finalColor = lerp(
					sceneColor,
					effectColor,
					noiseValue * saturate(input.uv0.z)
				);

				// -------------------------
				// VIGNETTE ALPHA
				// Center = transparent
				// Edges = visible
				// -------------------------

				float2 centeredUV = screenUV - 0.5;
				centeredUV.x *= _ScreenParams.x / _ScreenParams.y;

				float vignetteDistance = length(centeredUV);

				float vignetteMask = smoothstep(
					_VignetteInner,
					_VignetteOuter,
					vignetteDistance
				);

				vignetteMask = pow(vignetteMask, _VignettePower);

				float4 opacityNoise = lerp(
					(1.0).xxxx,
					noiseValue,
					_OpacityNoiseLerp
				);

				float alpha = saturate((opacityNoise * _OpacityMultiply).r);
				alpha *= vignetteMask;

				return half4(finalColor.rgb, alpha);
			}

			ENDHLSL
		}
	}

	Fallback Off
}