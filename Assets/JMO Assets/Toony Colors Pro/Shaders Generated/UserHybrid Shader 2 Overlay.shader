// Toony Colors Pro+Mobile 2
// (c) 2014-2025 Jean Moreno
// Modified: Render Texture Overlay variant of Hybrid Shader 2.
// Adds _OverlayMap (render texture) and _OverlayOpacity properties which are
// alpha-blended on top of the fully-lit surface colour, before fog.

Shader "Toony Colors Pro 2/User/Hybrid Shader 2 Overlay"
{
	Properties
	{
		[Enum(Front, 2, Back, 1, Both, 0)] _Cull ("Render Face", Float) = 2.0
		[TCP2ToggleNoKeyword] _ZWrite ("Depth Write", Float) = 1.0
		[Toggle(_ALPHATEST_ON)] _UseAlphaTest ("Alpha Clipping", Float) = 0
		_Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5

		[TCP2HeaderHelp(Base)]
		[MainColor] _BaseColor ("Color", Color) = (1,1,1,1)
		[MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
		[TCP2ColorNoAlpha] _HColor ("Highlight Color", Color) = (1,1,1,1)
		[TCP2ColorNoAlpha] _SColor ("Shadow Color", Color) = (0.2,0.2,0.2,1)
		[Toggle(TCP2_SHADOW_LIGHT_COLOR)] _ShadowColorLightAtten ("Main Light affects Shadow Color", Float) = 1
		[Toggle(TCP2_SHADOW_TEXTURE)] _UseShadowTexture ("Enable Shadow Albedo Texture", Float) = 0
		[NoScaleOffset] _ShadowBaseMap ("Shadow Albedo", 2D) = "gray" {}

		[TCP2Header(Ramp Shading)]
		[TCP2MaterialKeywordEnumNoPrefix(Default,_,Crisp,TCP2_RAMP_CRISP,Bands,TCP2_RAMP_BANDS,Bands Crisp,TCP2_RAMP_BANDS_CRISP,Texture,TCP2_RAMPTEXT)] _RampType ("Ramp Type", Float) = 0
		[TCP2Gradient] _Ramp ("Ramp Texture (RGB)", 2D) = "gray" {}
		_RampScale ("Scale", Float) = 1.0
		_RampOffset ("Offset", Float) = 0.0
		[PowerSlider(0.415)] _RampThreshold ("Threshold", Range(0.01,1)) = 0.75
		_RampSmoothing ("Smoothing", Range(0,1)) = 0.1
		[IntRange] _RampBands ("Bands Count", Range(1,20)) = 4
		_RampBandsSmoothing ("Bands Smoothing", Range(0,1)) = 0.1

		[TCP2HeaderToggle(_NORMALMAP)] _UseNormalMap ("Normal Mapping", Float) = 0
		_BumpMap ("Normal Map", 2D) = "bump" {}
		_BumpScale ("Scale", Range(-1,1)) = 1

		[TCP2HeaderToggle(TCP2_SPECULAR)] _UseSpecular ("Specular", Float) = 0
		[TCP2MaterialKeywordEnumNoPrefix(GGX,_,Stylized,TCP2_SPECULAR_STYLIZED,Crisp,TCP2_SPECULAR_CRISP)] _SpecularType ("Type", Float) = 0
		[TCP2ColorNoAlpha] [HDR] _SpecularColor ("Color", Color) = (0.75,0.75,0.75,1)
		[PowerSlider(5.0)] _SpecularToonSize ("Size", Range(0.001,1)) = 0.25
		_SpecularToonSmoothness ("Smoothing", Range(0,1)) = 0.05
		_SpecularRoughness ("Roughness", Range(0,1)) = 0.5
		[Enum(Disabled,0,Albedo Alpha,1,Custom R,2,Custom G,3,Custom B,4,Custom A,5)] _SpecularMapType ("Specular Map", Float) = 0
		[NoScaleOffset] _SpecGlossMap ("Specular Texture", 2D) = "white" {}

		[TCP2HeaderToggle(_EMISSION)] _UseEmission ("Emission", Float) = 0
		[Enum(No Texture,5,R,0,G,1,B,2,A,3,RGB,4)] _EmissionChannel ("Texture Channel", Float) = 4
		_EmissionMap ("Texture", 2D) = "white" {}
		[TCP2ColorNoAlpha(HDR)] _EmissionColor ("Color", Color) = (1,1,0,1)

		[TCP2HeaderToggle(TCP2_RIM_LIGHTING)] _UseRim ("Rim Lighting", Float) = 0
		[TCP2ColorNoAlpha] [HDR] _RimColor ("Color", Color) = (0.8,0.8,0.8,0.5)
		_RimMin ("Min", Range(0,2)) = 0.5
		_RimMax ("Max", Range(0,2)) = 1
		[Toggle(TCP2_RIM_LIGHTING_LIGHTMASK)] _UseRimLightMask ("Light-based Mask", Float) = 1

		[TCP2HeaderToggle(TCP2_MATCAP)] _UseMatCap ("MatCap", Float) = 0
		[Enum(Additive,0,Replace,1)] _MatCapType ("MatCap Blending", Float) = 0
		[NoScaleOffset] _MatCapTex ("Texture", 2D) = "black" {}
		[HDR] [TCP2ColorNoAlpha] _MatCapColor ("Color", Color) = (1,1,1,1)
		[Toggle(TCP2_MATCAP_MASK)] _UseMatCapMask ("Enable Mask", Float) = 0
		[NoScaleOffset] _MatCapMask ("Mask Texture", 2D) = "black" {}
		[Enum(R,0,G,1,B,2,A,3)] _MatCapMaskChannel ("Texture Channel", Float) = 0

		_IndirectIntensity ("Indirect Intensity", Range(0,1)) = 1
		[TCP2ToggleNoKeyword] _SingleIndirectColor ("Single Indirect Color", Float) = 0

		[TCP2HeaderToggle(TCP2_REFLECTIONS)] _UseReflections ("Indirect Specular (Environment Reflections)", Float) = 0
		[TCP2ColorNoAlpha] _ReflectionColor ("Color", Color) = (1,1,1,1)
		_ReflectionSmoothness ("Smoothness", Range(0,1)) = 0.5
		[TCP2Enum(Disabled,0,Albedo Alpha (Smoothness),1,Custom R (Smoothness),2,Custom G (Smoothness),3,Custom B (Smoothness),4,Custom A (Smoothness),5,Albedo Alpha (Mask),6,Custom R (Mask),7,Custom G (Mask),8,Custom B (Mask),9,Custom A (Mask),10)]
		_ReflectionMapType ("Reflection Map", Float) = 0
		[NoScaleOffset] _ReflectionTex ("Reflection Texture", 2D) = "white" {}
		[Toggle(TCP2_REFLECTIONS_FRESNEL)] _UseFresnelReflections ("Fresnel Reflections", Float) = 1
		_FresnelMin ("Fresnel Min", Range(0,2)) = 0
		_FresnelMax ("Fresnel Max", Range(0,2)) = 1.5

		[TCP2HeaderToggle(TCP2_OCCLUSION)] _UseOcclusion ("Occlusion", Float) = 0
		_OcclusionStrength ("Strength", Range(0.0, 1.0)) = 1.0
		[NoScaleOffset] _OcclusionMap ("Texture", 2D) = "white" {}
		[Enum(Albedo Alpha,0,Custom R,1,Custom G,2,Custom B,3,Custom A,4)] _OcclusionChannel ("Texture Channel", Float) = 0

		[TCP2HeaderHelp(Render Texture Overlay)]
		_OverlayMap ("Overlay (Render Texture)", 2D) = "black" {}
		_OverlayOpacity ("Overlay Opacity", Range(0,1)) = 1.0
		[Toggle] _OverlayFlipX ("Flip Horizontal", Float) = 0
		[Toggle] _OverlayFlipY ("Flip Vertical", Float) = 0

		[TCP2HeaderToggle] _UseOutline ("Outline", Float) = 0
		[HDR] _OutlineColor ("Color", Color) = (0,0,0,1)
		[TCP2MaterialKeywordEnumNoPrefix(Disabled,_,Vertex Shader,TCP2_OUTLINE_TEXTURED_VERTEX,Pixel Shader,TCP2_OUTLINE_TEXTURED_FRAGMENT)] _OutlineTextureType ("Texture", Float) = 0
		_OutlineTextureLOD ("Texture LOD", Range(0,8)) = 5
		_OutlineWidth ("Width", Range(0,10)) = 1
		[TCP2MaterialKeywordEnumNoPrefix(Disabled,_,Constant,TCP2_OUTLINE_CONST_SIZE,Minimum,TCP2_OUTLINE_MIN_SIZE,Min Max,TCP2_OUTLINE_MIN_MAX_SIZE)] _OutlinePixelSizeType ("Pixel Size", Float) = 0
		_OutlineMinWidth ("Minimum Width (Pixels)", Float) = 1
		_OutlineMaxWidth ("Maximum Width (Pixels)", Float) = 1
		[TCP2MaterialKeywordEnumNoPrefix(Normal, _, Vertex Colors, TCP2_COLORS_AS_NORMALS, Tangents, TCP2_TANGENT_AS_NORMALS, UV1, TCP2_UV1_AS_NORMALS, UV2, TCP2_UV2_AS_NORMALS, UV3, TCP2_UV3_AS_NORMALS, UV4, TCP2_UV4_AS_NORMALS)]
		_NormalsSource ("Outline Normals Source", Float) = 0
		[TCP2MaterialKeywordEnumNoPrefix(Full XYZ, TCP2_UV_NORMALS_FULL, Compressed XY, _, Compressed ZW, TCP2_UV_NORMALS_ZW)]
		_NormalsUVType ("UV Data Type", Float) = 0
		[TCP2MaterialKeywordEnumNoPrefix(Disabled,_,Main Directional Light,TCP2_OUTLINE_LIGHTING_MAIN,All Lights,TCP2_OUTLINE_LIGHTING_ALL,Indirect Only, TCP2_OUTLINE_LIGHTING_INDIRECT)] _OutlineLightingTypeURP ("Lighting", Float) = 0
		_DirectIntensityOutline ("Direct Strength", Range(0,1)) = 1
		_IndirectIntensityOutline ("Indirect Strength", Range(0,1)) = 0

		[ToggleOff(_RECEIVE_SHADOWS_OFF)] _ReceiveShadowsOff ("Receive Shadows", Float) = 1

		[HideInInspector] _RenderingMode ("rendering mode", Float) = 0.0
		[HideInInspector] _SrcBlend ("blending source", Float) = 1.0
		[HideInInspector] _DstBlend ("blending destination", Float) = 0.0
		[HideInInspector] _UseMobileMode ("Mobile mode", Float) = 0

		// Avoid compile error if the properties are ending with a drawer
		[HideInInspector] __dummy__ ("unused", Float) = 0
	}

	SubShader
	{
		Tags
		{
			"RenderPipeline" = "UniversalPipeline"
			"IgnoreProjector" = "True"
			"RenderType" = "Opaque"
			"Queue" = "Geometry"
		}

		Blend [_SrcBlend] [_DstBlend]
		ZWrite [_ZWrite]
		Cull [_Cull]

		HLSLINCLUDE
			#define fixed half
			#define fixed2 half2
			#define fixed3 half3
			#define fixed4 half4

			#define TCP2_HYBRID_URP
		ENDHLSL

		Pass
		{
			Name "Main"
			Tags { "LightMode" = "UniversalForward" }

			HLSLPROGRAM

			#pragma prefer_hlslcc gles
			#pragma exclude_renderers d3d11_9x
			#pragma target 3.0

			#pragma vertex Vertex
			#pragma fragment Fragment

			#pragma multi_compile_fog
			#pragma multi_compile_instancing

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

			// Material keywords
			#pragma shader_feature_local _ _RECEIVE_SHADOWS_OFF

			// URP keywords
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
			#pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
			#pragma multi_compile_fragment _ _LIGHT_LAYERS
			#pragma multi_compile_fragment _ _LIGHT_COOKIES
			#pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#pragma multi_compile _ _CLUSTER_LIGHT_LOOP

			// Unity keywords
			#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			#pragma multi_compile _ SHADOWS_SHADOWMASK
			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ LIGHTMAP_ON
			#pragma multi_compile _ DYNAMICLIGHTMAP_ON
			#pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
			#pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
			#pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

			// TCP2 keywords
			#pragma shader_feature_local TCP2_MOBILE
			#pragma shader_feature_local_fragment _ TCP2_RAMPTEXT TCP2_RAMP_CRISP TCP2_RAMP_BANDS TCP2_RAMP_BANDS_CRISP
			#pragma shader_feature_local_fragment TCP2_SHADOW_LIGHT_COLOR
			#pragma shader_feature_local_fragment TCP2_SHADOW_TEXTURE
			#pragma shader_feature_local_fragment TCP2_SPECULAR
			#pragma shader_feature_local_fragment _ TCP2_SPECULAR_STYLIZED TCP2_SPECULAR_CRISP
			#pragma shader_feature_local TCP2_RIM_LIGHTING
			#pragma shader_feature_local TCP2_RIM_LIGHTING_LIGHTMASK
			#pragma shader_feature_local TCP2_REFLECTIONS
			#pragma shader_feature_local TCP2_REFLECTIONS_FRESNEL
			#pragma shader_feature_local TCP2_MATCAP
			#pragma shader_feature_local_fragment TCP2_MATCAP_MASK
			#pragma shader_feature_local_fragment TCP2_OCCLUSION
			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local_fragment _ALPHATEST_ON
			#pragma shader_feature_local_fragment _EMISSION
			#pragma shader_feature_local_fragment _ _ALPHAPREMULTIPLY_ON

			#define URP_VERSION 171
			#include "../Shaders/Hybrid 2/TCP2 Hybrid 2 Overlay Include.cginc"

			ENDHLSL
		}

		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }

			ZWrite On
			ZTest LEqual
			Cull [_Cull]

			HLSLPROGRAM
			#pragma prefer_hlslcc gles
			#pragma exclude_renderers d3d11_9x
			#pragma target 2.0

			#pragma vertex ShadowPassVertex
			#pragma fragment ShadowPassFragment

			#pragma multi_compile_instancing
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

			#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
			#pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

			#pragma shader_feature_local_fragment _ALPHATEST_ON

			#define SHADOW_CASTER_PASS
			#define URP_VERSION 171
			#include "../Shaders/Hybrid 2/TCP2 Hybrid 2 Overlay Include.cginc"

			float3 _LightDirection;
			float3 _LightPosition;

			struct Attributes_Shadow
			{
				float4 positionOS   : POSITION;
				float3 normalOS     : NORMAL;
				float2 texcoord     : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings_Shadow
			{
				float2 uv           : TEXCOORD0;
				float4 positionCS   : SV_POSITION;
			};

			float4 GetShadowPositionHClip(Attributes_Shadow input)
			{
				float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
				float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

				#if _CASTING_PUNCTUAL_LIGHT_SHADOW
					float3 lightDirectionWS = normalize(_LightPosition - positionWS);
				#else
					float3 lightDirectionWS = _LightDirection;
				#endif

				float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

				#if UNITY_REVERSED_Z
					positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
				#else
					positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
				#endif

				return positionCS;
			}

			Varyings_Shadow ShadowPassVertex(Attributes_Shadow input)
			{
				Varyings_Shadow output = (Varyings_Shadow)0;
				UNITY_SETUP_INSTANCE_ID(input);

				output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
				output.positionCS = GetShadowPositionHClip(input);
				return output;
			}

			half4 ShadowPassFragment(Varyings_Shadow input) : SV_TARGET
			{
				#if defined(_ALPHATEST_ON)
					half4 albedo = tex2D(_BaseMap, input.uv.xy).rgba;
					albedo.rgb *= _BaseColor.rgb;
					half alpha = albedo.a * _BaseColor.a;
					clip(alpha - _Cutoff);
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					const float dither = Dither4x4(input.positionCS.xy);
					const float ditherThreshold = unity_LODFade.x - CopySign(dither, unity_LODFade.x);
					clip(ditherThreshold);
				#endif

				return 0;
			}

			ENDHLSL
		}

		Pass
		{
			Name "DepthOnly"
			Tags { "LightMode" = "DepthOnly" }

			ZWrite On
			ColorMask 0
			Cull [_Cull]

			HLSLPROGRAM

			#pragma prefer_hlslcc gles
			#pragma exclude_renderers d3d11_9x
			#pragma target 2.0

			#pragma vertex DepthOnlyVertex
			#pragma fragment DepthOnlyFragment

			#pragma multi_compile_instancing
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

			#pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

			#pragma shader_feature_local_fragment _ALPHATEST_ON

			#define URP_VERSION 171
			#include "../Shaders/Hybrid 2/TCP2 Hybrid 2 Overlay Include.cginc"

			struct Attributes_Depth
			{
				float4 position     : POSITION;
				float2 texcoord     : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings_Depth
			{
				float2 uv           : TEXCOORD0;
				float4 positionCS   : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings_Depth DepthOnlyVertex(Attributes_Depth input)
			{
				Varyings_Depth output = (Varyings_Depth)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
				output.positionCS = TransformObjectToHClip(input.position.xyz);
				return output;
			}

			half4 DepthOnlyFragment(Varyings_Depth input) : SV_TARGET
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				#if defined(LOD_FADE_CROSSFADE)
					const float dither = Dither4x4(input.positionCS.xy);
					const float ditherThreshold = unity_LODFade.x - CopySign(dither, unity_LODFade.x);
					clip(ditherThreshold);
				#endif

				half4 albedo = tex2D(_BaseMap, input.uv.xy).rgba;
				albedo.rgb *= _BaseColor.rgb;
				half alpha = albedo.a * _BaseColor.a;

				#if defined(_ALPHATEST_ON)
					clip(alpha - _Cutoff);
				#endif

				return 0;
			}

			ENDHLSL
		}

		Pass
		{
			Name "DepthNormals"
			Tags { "LightMode" = "DepthNormals" }

			ZWrite On
			Cull [_Cull]

			HLSLPROGRAM

			#pragma prefer_hlslcc gles
			#pragma exclude_renderers d3d11_9x
			#pragma target 2.0

			#pragma vertex DepthNormalsVertex
			#pragma fragment DepthNormalsFragment

			#pragma multi_compile_instancing
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

			#pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
			#pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS

			#pragma shader_feature_local_fragment _ALPHATEST_ON

			#define DEPTH_NORMALS_PASS
			#define URP_VERSION 171
			#include "../Shaders/Hybrid 2/TCP2 Hybrid 2 Overlay Include.cginc"

			struct Attributes_Depth
			{
				float4 position     : POSITION;
				float2 texcoord     : TEXCOORD0;
				float3 normal       : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings_Depth
			{
				float2 uv           : TEXCOORD0;
				float4 positionCS   : SV_POSITION;
				float3 normalWS     : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings_Depth DepthNormalsVertex(Attributes_Depth input)
			{
				Varyings_Depth output = (Varyings_Depth)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
				output.positionCS = TransformObjectToHClip(input.position.xyz);
				float3 normalWS = TransformObjectToWorldNormal(input.normal);
				output.normalWS = NormalizeNormalPerVertex(normalWS);
				return output;
			}

			half4 DepthNormalsFragment(
				Varyings_Depth input
#ifdef _WRITE_RENDERING_LAYERS
	#if UNITY_VERSION >= 60020000
				, out uint outRenderingLayers : SV_Target1
	#else
				, out float4 outRenderingLayers : SV_Target1
	#endif
#endif
				) : SV_TARGET
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				#if defined(LOD_FADE_CROSSFADE)
					const float dither = Dither4x4(input.positionCS.xy);
					const float ditherThreshold = unity_LODFade.x - CopySign(dither, unity_LODFade.x);
					clip(ditherThreshold);
				#endif

				half4 albedo = tex2D(_BaseMap, input.uv.xy).rgba;
				half alpha = albedo.a * _BaseColor.a;

				#if defined(_ALPHATEST_ON)
					clip(alpha - _Cutoff);
				#endif

				#if URP_VERSION >= 14 && defined(_WRITE_RENDERING_LAYERS)
					#if UNITY_VERSION >= 60020000
						outRenderingLayers = EncodeMeshRenderingLayer();
					#else
						outRenderingLayers = float4(EncodeMeshRenderingLayer(GetMeshRenderingLayer()), 0, 0, 0);
					#endif
				#endif

				#if URP_VERSION >= 12
					return float4(input.normalWS.xyz, 0.0);
				#else
					return float4(PackNormalOctRectEncode(TransformWorldToViewDir(input.normalWS, true)), 0.0, 0.0);
				#endif
			}

			ENDHLSL
		}

		Pass
		{
			Name "Meta"
			Tags { "LightMode"="Meta" }

			Cull Off

			HLSLPROGRAM
			#pragma prefer_hlslcc gles
			#pragma exclude_renderers d3d11_9x
			#pragma target 3.0

			#pragma vertex Vertex
			#pragma fragment Fragment

			#pragma shader_feature_local TCP2_MOBILE
			#pragma shader_feature_local_fragment TCP2_SPECULAR
			#pragma shader_feature_local_fragment _ALPHATEST_ON
			#pragma shader_feature_local_fragment _EMISSION

			#undef UNITY_SHOULD_SAMPLE_SH
			#define UNITY_SHOULD_SAMPLE_SH 0

			#ifndef UNITY_PASS_META
				#define UNITY_PASS_META
			#endif

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"
			#define URP_VERSION 171
			#include "../Shaders/Hybrid 2/TCP2 Hybrid 2 Overlay Include.cginc"

			ENDHLSL
		}
	}

	FallBack "Hidden/InternalErrorShader"
	CustomEditor "ToonyColorsPro.ShaderGenerator.MaterialInspector_Hybrid"
}
