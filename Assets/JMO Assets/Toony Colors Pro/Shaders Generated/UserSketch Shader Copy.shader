// Toony Colors Pro+Mobile 2
// (c) 2014-2025 Jean Moreno

Shader "Toony Colors Pro 2/User/Sketch Shader Copy"
{
	Properties
	{
		[TCP2HeaderHelp(Base)]
		_BaseColor ("Color", Color) = (1,1,1,1)
		[TCP2ColorNoAlpha] _HColor ("Highlight Color", Color) = (0.75,0.75,0.75,1)
		[TCP2ColorNoAlpha] _SColor ("Shadow Color", Color) = (0.2,0.2,0.2,1)
		[MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
		[Toggle(TCP2_NORMAL_MAP)] _UseNormalMap ("Enable Normal Map", Float) = 0
		[NoScaleOffset] _BumpMap ("Normal Map", 2D) = "bump" {}
		_BumpScale ("Normal Map Scale", Float) = 1.0
		[TCP2Separator]

		[TCP2Header(Ramp Shading)]
		
		_RampThreshold ("Threshold", Range(0.01,1)) = 0.5
		_RampSmoothing ("Smoothing", Range(0.001,1)) = 0.5
		[TCP2Separator]
		
		_StylizedThreshold ("Stylized Threshold", 2D) = "gray" {}
		[TCP2Separator]
		
		[TCP2HeaderHelp(Sketch)]
		[Toggle(TCP2_SKETCH)] _UseSketch ("Enable Sketch Effect", Float) = 0
		_SketchTexture ("Sketch Texture", 2D) = "black" {}
		_SketchTexture_OffsetSpeed ("Sketch Texture UV Offset Speed", Float) = 120
		[TCP2Separator]
		
		[TCP2HeaderHelp(Outline)]
		_OutlineWidth ("Width", Range(0.1,4)) = 1
		_OutlineColorVertex ("Color", Color) = (0,0,0,1)
		// Outline Normals
		[TCP2MaterialKeywordEnumNoPrefix(Regular, _, Vertex Colors, TCP2_COLORS_AS_NORMALS, Tangents, TCP2_TANGENT_AS_NORMALS, UV1, TCP2_UV1_AS_NORMALS, UV2, TCP2_UV2_AS_NORMALS, UV3, TCP2_UV3_AS_NORMALS, UV4, TCP2_UV4_AS_NORMALS)]
		_NormalsSource ("Outline Normals Source", Float) = 0
		[TCP2MaterialKeywordEnumNoPrefix(Full XYZ, TCP2_UV_NORMALS_FULL, Compressed XY, _, Compressed ZW, TCP2_UV_NORMALS_ZW)]
		_NormalsUVType ("UV Data Type", Float) = 0
		[TCP2Separator]
		
		[Enum(ToonyColorsPro.ShaderGenerator.Culling)] _faceCulling ("Face Culling", Float) = 2

		[ToggleOff(_RECEIVE_SHADOWS_OFF)] _ReceiveShadowsOff ("Receive Shadows", Float) = 1

		// Avoid compile error if the properties are ending with a drawer
		[HideInInspector] __dummy__ ("unused", Float) = 0
	}

	SubShader
	{
		Tags
		{
			"RenderPipeline" = "UniversalPipeline"
			"RenderType"="Opaque"
		}

		HLSLINCLUDE
		#define fixed half
		#define fixed2 half2
		#define fixed3 half3
		#define fixed4 half4

		#if UNITY_VERSION >= 202020
			#define URP_10_OR_NEWER
		#endif
		#if UNITY_VERSION >= 202120
			#define URP_12_OR_NEWER
		#endif
		#if UNITY_VERSION >= 202220
			#define URP_14_OR_NEWER
		#endif

		// Texture/Sampler abstraction
		#define TCP2_TEX2D_WITH_SAMPLER(tex)						TEXTURE2D(tex); SAMPLER(sampler##tex)
		#define TCP2_TEX2D_NO_SAMPLER(tex)							TEXTURE2D(tex)
		#define TCP2_TEX2D_SAMPLE(tex, samplertex, coord)			SAMPLE_TEXTURE2D(tex, sampler##samplertex, coord)
		#define TCP2_TEX2D_SAMPLE_LOD(tex, samplertex, coord, lod)	SAMPLE_TEXTURE2D_LOD(tex, sampler##samplertex, coord, lod)

		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

		// Uniforms

		// Shader Properties
		TCP2_TEX2D_WITH_SAMPLER(_BaseMap);
		TCP2_TEX2D_WITH_SAMPLER(_BumpMap);
		TCP2_TEX2D_WITH_SAMPLER(_StylizedThreshold);
		TCP2_TEX2D_WITH_SAMPLER(_SketchTexture);

		CBUFFER_START(UnityPerMaterial)
			
			// Shader Properties
			float _OutlineWidth;
			fixed4 _OutlineColorVertex;
			float4 _BaseMap_ST;
			fixed4 _BaseColor;
			float4 _StylizedThreshold_ST;
			float _RampThreshold;
			float _RampSmoothing;
			float4 _SketchTexture_ST;
			half _SketchTexture_OffsetSpeed;
			fixed4 _SColor;
			fixed4 _HColor;
			float _BumpScale;
		CBUFFER_END

		// Hash without sin and uniform across platforms
		// Adapted from: https://www.shadertoy.com/view/4djSRW (c) 2014 - Dave Hoskins - CC BY-SA 4.0 License
		float2 hash22(float2 p)
		{
			float3 p3 = frac(p.xyx * float3(443.897, 441.423, 437.195));
			p3 += dot(p3, p3.yzx + 19.19);
			return frac((p3.xx+p3.yz)*p3.zy);
		}
		
		// Built-in renderer (CG) to SRP (HLSL) bindings
		#define UnityObjectToClipPos TransformObjectToHClip
		#define _WorldSpaceLightPos0 _MainLightPosition
		
		ENDHLSL

		// Outline Include
		HLSLINCLUDE

		#pragma multi_compile_fog

		struct appdata_outline
		{
			float4 vertex : POSITION;
			float3 normal : NORMAL;
			#if TCP2_UV1_AS_NORMALS
			float4 texcoord0 : TEXCOORD0;
		#elif TCP2_UV2_AS_NORMALS
			float4 texcoord1 : TEXCOORD1;
		#elif TCP2_UV3_AS_NORMALS
			float4 texcoord2 : TEXCOORD2;
		#elif TCP2_UV4_AS_NORMALS
			float4 texcoord3 : TEXCOORD3;
		#endif
		#if TCP2_COLORS_AS_NORMALS
			float4 vertexColor : COLOR;
		#endif
		#if TCP2_TANGENT_AS_NORMALS
			float4 tangent : TANGENT;
		#endif
			UNITY_VERTEX_INPUT_INSTANCE_ID
		};

		struct v2f_outline
		{
			float4 vertex : SV_POSITION;
			float4 vcolor : TEXCOORD0;
			float pack1 : TEXCOORD1; /* pack1.x = fogFactor */
			UNITY_VERTEX_INPUT_INSTANCE_ID
			UNITY_VERTEX_OUTPUT_STEREO
		};

		v2f_outline vertex_outline (appdata_outline v)
		{
			v2f_outline output = (v2f_outline)0;

			UNITY_SETUP_INSTANCE_ID(v);
			UNITY_TRANSFER_INSTANCE_ID(v, output);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

			// Shader Properties Sampling
			float __outlineWidth = ( _OutlineWidth );
			float4 __outlineColorVertex = ( _OutlineColorVertex.rgba );

		#ifdef TCP2_COLORS_AS_NORMALS
			//Vertex Color for Normals
			float3 normal = (v.vertexColor.xyz*2) - 1;
		#elif TCP2_TANGENT_AS_NORMALS
			//Tangent for Normals
			float3 normal = v.tangent.xyz;
		#elif TCP2_UV1_AS_NORMALS || TCP2_UV2_AS_NORMALS || TCP2_UV3_AS_NORMALS || TCP2_UV4_AS_NORMALS
			#if TCP2_UV1_AS_NORMALS
				#define uvChannel texcoord0
			#elif TCP2_UV2_AS_NORMALS
				#define uvChannel texcoord1
			#elif TCP2_UV3_AS_NORMALS
				#define uvChannel texcoord2
			#elif TCP2_UV4_AS_NORMALS
				#define uvChannel texcoord3
			#endif
		
			#if TCP2_UV_NORMALS_FULL
			//UV for Normals, full
			float3 normal = v.uvChannel.xyz;
			#else
			//UV for Normals, compressed
			#if TCP2_UV_NORMALS_ZW
				#define ch1 z
				#define ch2 w
			#else
				#define ch1 x
				#define ch2 y
			#endif
			float3 n;
			//unpack uvs
			v.uvChannel.ch1 = v.uvChannel.ch1 * 255.0/16.0;
			n.x = floor(v.uvChannel.ch1) / 15.0;
			n.y = frac(v.uvChannel.ch1) * 16.0 / 15.0;
			//- get z
			n.z = v.uvChannel.ch2;
			//- transform
			n = n*2 - 1;
			float3 normal = n;
			#endif
		#else
			float3 normal = v.normal;
		#endif
		
		#if TCP2_ZSMOOTH_ON
			//Correct Z artefacts
			normal = UnityObjectToViewPos(normal);
			normal.z = -_ZSmooth;
		#endif
			float size = 1;
		
		#if !defined(SHADOWCASTER_PASS)
			output.vertex = UnityObjectToClipPos(v.vertex.xyz);
			normal = mul(UNITY_MATRIX_M, float4(normal, 0)).xyz;
			float2 clipNormals = normalize(mul(UNITY_MATRIX_VP, float4(normal,0)).xy);
			half2 screenRatio = half2(1.0, _ScreenParams.x / _ScreenParams.y);
			half2 outlineWidth = (__outlineWidth / 100) * screenRatio;
			output.vertex.xy += clipNormals.xy * outlineWidth;
			
		#else
			v.vertex = v.vertex + float4(normal,0) * __outlineWidth * size * 0.01;
		#endif
		
			output.vcolor.xyzw = __outlineColorVertex;
			output.pack1.x = ComputeFogFactor(output.vertex.z);

			return output;
		}

		float4 fragment_outline (v2f_outline input) : SV_Target
		{

			UNITY_SETUP_INSTANCE_ID(input);
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

			// Shader Properties Sampling
			float4 __outlineColor = ( float4(1,1,1,1) );

			half4 outlineColor = __outlineColor * input.vcolor.xyzw;
			outlineColor.rgb = MixFog(outlineColor.rgb, input.pack1.x);

			return outlineColor;
		}

		ENDHLSL
		// Outline Include End
		Pass
		{
			Name "Main"
			Tags
			{
				"LightMode"="UniversalForward"
			}
			Cull [_faceCulling]

			HLSLPROGRAM
			// Required to compile gles 2.0 with standard SRP library
			// All shaders must be compiled with HLSLcc and currently only gles is not using HLSLcc by default
			#pragma prefer_hlslcc gles
			#pragma exclude_renderers d3d11_9x
			#pragma target 3.0

			// -------------------------------------
			// Material keywords
			#pragma shader_feature_local _ _RECEIVE_SHADOWS_OFF

			// -------------------------------------
			// Universal Render Pipeline keywords
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

			#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			#pragma multi_compile _ SHADOWS_SHADOWMASK
			#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"

			// -------------------------------------
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

			//--------------------------------------
			// GPU Instancing
			#pragma multi_compile_instancing

			#pragma vertex Vertex
			#pragma fragment Fragment

			//--------------------------------------
			// Toony Colors Pro 2 keywords
			#pragma shader_feature_local_fragment TCP2_SKETCH
			#pragma shader_feature_local TCP2_NORMAL_MAP

			// vertex input
			struct Attributes
			{
				float4 vertex       : POSITION;
				float3 normal       : NORMAL;
				float4 tangent      : TANGENT;
				float4 texcoord0 : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			// vertex output / fragment input
			struct Varyings
			{
				float4 positionCS     : SV_POSITION;
				float3 normal         : NORMAL;
				float4 worldPosAndFog : TEXCOORD0;
			#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
				float4 shadowCoord    : TEXCOORD1; // compute shadow coord per-vertex for the main light
			#endif
			#ifdef _ADDITIONAL_LIGHTS_VERTEX
				half3 vertexLights : TEXCOORD2;
			#endif
				float4 screenPosition : TEXCOORD3;
				float2 pack1 : TEXCOORD4; /* pack1.xy = texcoord0 */
				float pack2 : TEXCOORD5; /* pack2.x = fogFactor */
			#if defined(TCP2_NORMAL_MAP)
				float3 tangentWS   : TEXCOORD6;
				float3 bitangentWS : TEXCOORD7;
			#endif
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			#if USE_FORWARD_PLUS || USE_CLUSTER_LIGHT_LOOP
				// Fake InputData struct needed for Forward+ macro
				struct InputDataForwardPlusDummy
				{
					float3  positionWS;
					float2  normalizedScreenSpaceUV;
				};
			#endif

			Varyings Vertex(Attributes input)
			{
				Varyings output = (Varyings)0;

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				// Texture Coordinates
				output.pack1.xy.xy = input.texcoord0.xy * _BaseMap_ST.xy + _BaseMap_ST.zw;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.vertex.xyz);
			#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
				output.shadowCoord = GetShadowCoord(vertexInput);
			#endif
				float4 clipPos = vertexInput.positionCS;

				float4 screenPos = ComputeScreenPos(clipPos);
				output.screenPosition.xyzw = screenPos;

				VertexNormalInputs vertexNormalInput = GetVertexNormalInputs(input.normal, input.tangent);
		#if defined(TCP2_NORMAL_MAP)
				output.tangentWS = vertexNormalInput.tangentWS;
				output.bitangentWS = vertexNormalInput.bitangentWS;
			#endif
			#ifdef _ADDITIONAL_LIGHTS_VERTEX
				// Vertex lighting
				output.vertexLights = VertexLighting(vertexInput.positionWS, vertexNormalInput.normalWS);
			#endif

				// world position
				output.worldPosAndFog = float4(vertexInput.positionWS.xyz, 0);

				// Computes fog factor per-vertex
				output.worldPosAndFog.w = ComputeFogFactor(vertexInput.positionCS.z);

				// normal
				output.normal = normalize(vertexNormalInput.normalWS);

				// clip position
				output.positionCS = vertexInput.positionCS;

				return output;
			}

			half4 Fragment(Varyings input
			) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float3 positionWS = input.worldPosAndFog.xyz;
				float3 normalWS = normalize(input.normal);
			#if defined(TCP2_NORMAL_MAP)
				half3 normalTS = UnpackNormalScale(TCP2_TEX2D_SAMPLE(_BumpMap, _BumpMap, input.pack1.xy), _BumpScale);
				float3x3 tangentToWorld = float3x3(normalize(input.tangentWS), normalize(input.bitangentWS), normalWS);
				normalWS = normalize(mul(normalTS, tangentToWorld));
			#endif

				//Screen Space UV
				float2 screenUV = input.screenPosition.xyzw.xy / input.screenPosition.xyzw.w;
				
				// Shader Properties Sampling
				float4 __albedo = ( TCP2_TEX2D_SAMPLE(_BaseMap, _BaseMap, input.pack1.xy).rgba );
				float4 __mainColor = ( _BaseColor.rgba );
				float __alpha = ( __albedo.a * __mainColor.a );
				float __ambientIntensity = ( 1.0 );
				float __stylizedThreshold = ( TCP2_TEX2D_SAMPLE(_StylizedThreshold, _StylizedThreshold, input.pack1.xy * _StylizedThreshold_ST.xy + _StylizedThreshold_ST.zw).a );
				float __stylizedThresholdScale = ( 1.0 );
				float __rampThreshold = ( _RampThreshold );
				float __rampSmoothing = ( _RampSmoothing );
				float3 __sketchColor = ( float3(0,0,0) );
				float3 __sketchTexture = ( TCP2_TEX2D_SAMPLE(_SketchTexture, _SketchTexture, screenUV * _ScreenParams.zw * _SketchTexture_ST.xy + _SketchTexture_ST.zw + hash22(floor(_Time.xx * _SketchTexture_OffsetSpeed.xx) / _SketchTexture_OffsetSpeed.xx)).aaa );
				float __sketchThresholdScale = ( 1.0 );
				float3 __shadowColor = ( _SColor.rgb );
				float3 __highlightColor = ( _HColor.rgb );

				// main texture
				half3 albedo = __albedo.rgb;
				half alpha = __alpha;

				half3 emission = half3(0,0,0);
				
				albedo *= __mainColor.rgb;

				// main light: direction, color, distanceAttenuation, shadowAttenuation
			#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
				float4 shadowCoord = input.shadowCoord;
			#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
				float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
			#else
				float4 shadowCoord = float4(0, 0, 0, 0);
			#endif

			#if defined(URP_10_OR_NEWER)
				#if defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON)
					half4 shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
				#elif !defined (LIGHTMAP_ON)
					half4 shadowMask = unity_ProbesOcclusion;
				#else
					half4 shadowMask = half4(1, 1, 1, 1);
				#endif

				Light mainLight = GetMainLight(shadowCoord, positionWS, shadowMask);
			#else
				Light mainLight = GetMainLight(shadowCoord);
			#endif

			#if defined(_SCREEN_SPACE_OCCLUSION) || defined(USE_FORWARD_PLUS) || defined(USE_CLUSTER_LIGHT_LOOP)
				float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
			#endif

				// ambient or lightmap
				// Samples SH fully per-pixel. SampleSHVertex and SampleSHPixel functions
				// are also defined in case you want to sample some terms per-vertex.
				half3 bakedGI = SampleSH(normalWS);
				half occlusion = 1;

				half3 indirectDiffuse = bakedGI;
				indirectDiffuse *= occlusion * albedo * __ambientIntensity;

				half3 lightDir = mainLight.direction;
				half3 lightColor = mainLight.color.rgb;

				half atten = mainLight.shadowAttenuation * mainLight.distanceAttenuation;

				half ndl = dot(normalWS, lightDir);
				float stylizedThreshold = __stylizedThreshold;
				stylizedThreshold -= 0.5;
				stylizedThreshold *= __stylizedThresholdScale;
				ndl += stylizedThreshold;
				half3 ramp;
				
				half rampThreshold = __rampThreshold;
				half rampSmooth = __rampSmoothing * 0.5;
				ndl = saturate(ndl);
				ramp = smoothstep(rampThreshold - rampSmooth, rampThreshold + rampSmooth, ndl);

				// apply attenuation
				ramp *= atten;

				half3 color = half3(0,0,0);
				half3 accumulatedRamp = ramp * max(lightColor.r, max(lightColor.g, lightColor.b));
				half3 accumulatedColors = ramp * lightColor.rgb;

				// Additional lights loop
			#ifdef _ADDITIONAL_LIGHTS
				uint pixelLightCount = GetAdditionalLightsCount();

				#if USE_FORWARD_PLUS || USE_CLUSTER_LIGHT_LOOP
					// Additional directional lights in Forward+
					for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
					{
						CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK

						Light light = GetAdditionalLight(lightIndex, positionWS, shadowMask);

						#if defined(_LIGHT_LAYERS)
							if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
						#endif
						{
							half atten = light.shadowAttenuation * light.distanceAttenuation;

							#if defined(_LIGHT_LAYERS)
								half3 lightDir = half3(0, 1, 0);
								half3 lightColor = half3(0, 0, 0);
								if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
								{
									lightColor = light.color.rgb;
									lightDir = light.direction;
								}
							#else
								half3 lightColor = light.color.rgb;
								half3 lightDir = light.direction;
							#endif

							half ndl = dot(normalWS, lightDir);
							float stylizedThreshold = __stylizedThreshold;
							stylizedThreshold -= 0.5;
							stylizedThreshold *= __stylizedThresholdScale;
							ndl += stylizedThreshold;
							half3 ramp;
							
							ndl = saturate(ndl);
							ramp = smoothstep(rampThreshold - rampSmooth, rampThreshold + rampSmooth, ndl);

							// apply attenuation (shadowmaps & point/spot lights attenuation)
							ramp *= atten;

							accumulatedRamp += ramp * max(lightColor.r, max(lightColor.g, lightColor.b));
							accumulatedColors += ramp * lightColor.rgb;

						}
					}

					// Data with dummy struct used in Forward+ macro (LIGHT_LOOP_BEGIN)
					InputDataForwardPlusDummy inputData;
					inputData.normalizedScreenSpaceUV = normalizedScreenSpaceUV;
					inputData.positionWS = positionWS;
				#endif

				LIGHT_LOOP_BEGIN(pixelLightCount)
				{
					#if defined(URP_10_OR_NEWER)
						Light light = GetAdditionalLight(lightIndex, positionWS, shadowMask);
					#else
						Light light = GetAdditionalLight(lightIndex, positionWS);
					#endif
					half atten = light.shadowAttenuation * light.distanceAttenuation;

					#if defined(_LIGHT_LAYERS)
						half3 lightDir = half3(0, 1, 0);
						half3 lightColor = half3(0, 0, 0);
						if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
						{
							lightColor = light.color.rgb;
							lightDir = light.direction;
						}
					#else
						half3 lightColor = light.color.rgb;
						half3 lightDir = light.direction;
					#endif

					half ndl = dot(normalWS, lightDir);
					float stylizedThreshold = __stylizedThreshold;
					stylizedThreshold -= 0.5;
					stylizedThreshold *= __stylizedThresholdScale;
					ndl += stylizedThreshold;
					half3 ramp;
					
					ndl = saturate(ndl);
					ramp = smoothstep(rampThreshold - rampSmooth, rampThreshold + rampSmooth, ndl);

					// apply attenuation (shadowmaps & point/spot lights attenuation)
					ramp *= atten;

					accumulatedRamp += ramp * max(lightColor.r, max(lightColor.g, lightColor.b));
					accumulatedColors += ramp * lightColor.rgb;

				}
				LIGHT_LOOP_END
			#endif
			#ifdef _ADDITIONAL_LIGHTS_VERTEX
				color += input.vertexLights * albedo;
			#endif

				accumulatedRamp = saturate(accumulatedRamp);
				
				// Sketch
				#if defined(TCP2_SKETCH)
				half3 sketchColor = lerp(__sketchColor, half3(1,1,1), __sketchTexture);
				half3 sketch = lerp(sketchColor, half3(1,1,1), saturate(accumulatedRamp * __sketchThresholdScale));
				#endif
				half3 shadowColor = (1 - accumulatedRamp.rgb) * __shadowColor;
				accumulatedRamp = accumulatedColors.rgb * __highlightColor + shadowColor;
				color += albedo * accumulatedRamp;
				#if defined(TCP2_SKETCH)
				color.rgb *= sketch.rgb;
				#endif

				// apply ambient
				color += indirectDiffuse;

				color += emission;

				// Mix the pixel color with fogColor. You can optionally use MixFogColor to override the fogColor with a custom one.
				float fogFactor = input.worldPosAndFog.w;
				color = MixFog(color, fogFactor);

				return half4(color, alpha);
			}
			ENDHLSL
		}

		// Outline
		Pass
		{
			Name "Outline"
			Tags { "LightMode" = "Outline" }
			Tags
			{
			}
			Cull Front

			HLSLPROGRAM

			#pragma vertex vertex_outline
			#pragma fragment fragment_outline

			#pragma target 3.0

			#pragma multi_compile _ TCP2_COLORS_AS_NORMALS TCP2_TANGENT_AS_NORMALS TCP2_UV1_AS_NORMALS TCP2_UV2_AS_NORMALS TCP2_UV3_AS_NORMALS TCP2_UV4_AS_NORMALS
			#pragma multi_compile _ TCP2_UV_NORMALS_FULL TCP2_UV_NORMALS_ZW
			#pragma multi_compile_instancing

			ENDHLSL
		}
		// Depth & Shadow Caster Passes
		HLSLINCLUDE

		#if defined(SHADOW_CASTER_PASS) || defined(DEPTH_ONLY_PASS)

			#define fixed half
			#define fixed2 half2
			#define fixed3 half3
			#define fixed4 half4

			float3 _LightDirection;
			float3 _LightPosition;

			struct Attributes
			{
				float4 vertex   : POSITION;
				float3 normal   : NORMAL;
				float4 texcoord0 : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS     : SV_POSITION;
				float2 pack0 : TEXCOORD1; /* pack0.xy = texcoord0 */
			#if defined(DEPTH_ONLY_PASS)
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			#endif
			};

			float4 GetShadowPositionHClip(Attributes input)
			{
				float3 positionWS = TransformObjectToWorld(input.vertex.xyz);
				float3 normalWS = TransformObjectToWorldNormal(input.normal);

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

			Varyings ShadowDepthPassVertex(Attributes input)
			{
				Varyings output = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				#if defined(DEPTH_ONLY_PASS)
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				#endif

				// Texture Coordinates
				output.pack0.xy.xy = input.texcoord0.xy * _BaseMap_ST.xy + _BaseMap_ST.zw;

				#if defined(DEPTH_ONLY_PASS)
					output.positionCS = TransformObjectToHClip(input.vertex.xyz);
				#elif defined(SHADOW_CASTER_PASS)
					output.positionCS = GetShadowPositionHClip(input);
				#else
					output.positionCS = float4(0,0,0,0);
				#endif

				return output;
			}

			half4 ShadowDepthPassFragment(
				Varyings input
			) : SV_TARGET
			{
				#if defined(DEPTH_ONLY_PASS)
					UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				#endif

				// Shader Properties Sampling
				float4 __albedo = ( TCP2_TEX2D_SAMPLE(_BaseMap, _BaseMap, input.pack0.xy).rgba );
				float4 __mainColor = ( _BaseColor.rgba );
				float __alpha = ( __albedo.a * __mainColor.a );

				half3 albedo = half3(1,1,1);
				half alpha = __alpha;
				half3 emission = half3(0,0,0);

				return 0;
			}

		#endif
		ENDHLSL

		Pass
		{
			Name "ShadowCaster"
			Tags
			{
				"LightMode" = "ShadowCaster"
			}

			ZWrite On
			ZTest LEqual
			Cull [_faceCulling]

			HLSLPROGRAM
			// Required to compile gles 2.0 with standard srp library
			#pragma prefer_hlslcc gles
			#pragma exclude_renderers d3d11_9x
			#pragma target 2.0

			// using simple #define doesn't work, we have to use this instead
			#pragma multi_compile SHADOW_CASTER_PASS

			//--------------------------------------
			// GPU Instancing
			#pragma multi_compile_instancing
			#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

			#pragma vertex ShadowDepthPassVertex
			#pragma fragment ShadowDepthPassFragment

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

			ENDHLSL
		}

		Pass
		{
			Name "DepthOnly"
			Tags
			{
				"LightMode" = "DepthOnly"
			}

			ZWrite On
			ColorMask 0
			Cull [_faceCulling]

			HLSLPROGRAM

			// Required to compile gles 2.0 with standard srp library
			#pragma prefer_hlslcc gles
			#pragma exclude_renderers d3d11_9x
			#pragma target 2.0

			//--------------------------------------
			// GPU Instancing
			#pragma multi_compile_instancing

			// using simple #define doesn't work, we have to use this instead
			#pragma multi_compile DEPTH_ONLY_PASS

			#pragma vertex ShadowDepthPassVertex
			#pragma fragment ShadowDepthPassFragment

			ENDHLSL
		}

	}

	FallBack "Hidden/InternalErrorShader"
	CustomEditor "ToonyColorsPro.ShaderGenerator.MaterialInspector_SG2"
}

/* TCP_DATA u config(ver:"2.9.20";unity:"6000.2.7f2";tmplt:"SG2_Template_URP";features:list["UNITY_5_4","UNITY_5_5","UNITY_5_6","UNITY_2017_1","UNITY_2018_1","UNITY_2018_2","UNITY_2018_3","UNITY_2019_1","UNITY_2019_2","UNITY_2019_3","UNITY_2019_4","UNITY_2020_1","UNITY_2021_1","UNITY_2021_2","UNITY_2022_2","UNITY_6000_2","UNITY_6000_1","UNITY_6000_0","ENABLE_FORWARD_PLUS","OUTLINE","SKETCH_SHADER_FEATURE","SKETCH","TEXTURED_THRESHOLD","OUTLINE_URP_FEATURE","OUTLINE_CLIP_SPACE","FOG","CULLING","TEMPLATE_LWRP"];flags:list[];flags_extra:dict[];keywords:dict[RENDER_TYPE="Opaque",RampTextureDrawer="[TCP2Gradient]",RampTextureLabel="Ramp Texture",SHADER_TARGET="3.0"];shaderProperties:list[,,,,,,,,,,,,,,,,sp(name:"Face Culling";imps:list[imp_enum(value_type:1;value:0;enum_type:"ToonyColorsPro.ShaderGenerator.Culling";guid:"926f85f0-e1fb-45a7-8fb2-0942b0e8f66f";op:Multiply;lbl:"Face Culling";gpu_inst:False;dots_inst:False;locked:False;impl_index:0)];layers:list[];unlocked:list[];layer_blend:dict[];custom_blend:dict[];clones:dict[];isClone:False)];customTextures:list[];codeInjection:codeInjection(injectedFiles:list[];mark:False);matLayers:list[]) */
/* TCP_HASH 9fb2e2a9f03390f30e94ad40596da9c2 */
