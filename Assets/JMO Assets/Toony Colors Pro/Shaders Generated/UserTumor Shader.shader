// Toony Colors Pro+Mobile 2
// (c) 2014-2025 Jean Moreno
// Tumor variant with organic undulation — custom modification

Shader "Toony Colors Pro 2/User/Tumor Shader"
{
	Properties
	{
		[Enum(Front, 2, Back, 1, Both, 0)] _Cull ("Render Face", Float) = 2.0
		[TCP2ToggleNoKeyword] _ZWrite ("Depth Write", Float) = 1.0
		[HideInInspector] _RenderingMode ("rendering mode", Float) = 0.0
		[HideInInspector] _SrcBlend ("blending source", Float) = 1.0
		[HideInInspector] _DstBlend ("blending destination", Float) = 0.0
		[TCP2Separator]

		[TCP2HeaderHelp(Base)]
		_BaseColor ("Color", Color) = (1,1,1,1)
		[TCP2ColorNoAlpha] _HColor ("Highlight Color", Color) = (0.75,0.75,0.75,1)
		[TCP2ColorNoAlpha] _SColor ("Shadow Color", Color) = (0.2,0.2,0.2,1)
		[MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
		[Toggle(TCP2_NORMAL_MAP)] _UseNormalMap ("Enable Normal Map", Float) = 0
		[NoScaleOffset] _BumpMap ("Normal Map", 2D) = "bump" {}
		_BumpScale ("Normal Map Scale", Float) = 1.0
		[Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clipping", Float) = 0
		_Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
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
		[TCP2MaterialKeywordEnumNoPrefix(Regular, _, Vertex Colors, TCP2_COLORS_AS_NORMALS, Tangents, TCP2_TANGENT_AS_NORMALS, UV1, TCP2_UV1_AS_NORMALS, UV2, TCP2_UV2_AS_NORMALS, UV3, TCP2_UV3_AS_NORMALS, UV4, TCP2_UV4_AS_NORMALS)]
		_NormalsSource ("Outline Normals Source", Float) = 0
		[TCP2MaterialKeywordEnumNoPrefix(Full XYZ, TCP2_UV_NORMALS_FULL, Compressed XY, _, Compressed ZW, TCP2_UV_NORMALS_ZW)]
		_NormalsUVType ("UV Data Type", Float) = 0
		[TCP2Separator]

		[Enum(ToonyColorsPro.ShaderGenerator.Culling)] _faceCulling ("Face Culling", Float) = 2

		[ToggleOff(_RECEIVE_SHADOWS_OFF)] _ReceiveShadowsOff ("Receive Shadows", Float) = 1

		[TCP2Header(Tumor Undulation)]
		_UndulateAmplitude ("Undulate Amplitude", Float) = 0.05
		_UndulateFrequency ("Undulate Frequency", Float) = 3.0
		_UndulateSpeed ("Undulate Speed", Float) = 1.5

		[TCP2Header(Tumor Pulse)]
		_PulseColor ("Pulse Emissive Color", Color) = (0.8, 0.05, 0.05, 1)
		_PulseIntensity ("Pulse Intensity", Float) = 0.3
		_PulseSpeed ("Pulse Speed", Float) = 1.2
		_EmissionNoise ("Emission Noise Texture", 2D) = "white" {}
		_EmissionNoiseScroll ("Noise Scroll Speed (XY)", Vector) = (0.08, 0.05, 0, 0)
		_EmissionNoiseContrast ("Noise Contrast", Range(1, 8)) = 3.0

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
		#if defined(URP_12_OR_NEWER)
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
		#endif

		// Shader Properties
		TCP2_TEX2D_WITH_SAMPLER(_BaseMap);
		TCP2_TEX2D_WITH_SAMPLER(_BumpMap);
		TCP2_TEX2D_WITH_SAMPLER(_StylizedThreshold);
		TCP2_TEX2D_WITH_SAMPLER(_SketchTexture);
		TCP2_TEX2D_WITH_SAMPLER(_EmissionNoise);

		CBUFFER_START(UnityPerMaterial)
			float _OutlineWidth;
			fixed4 _OutlineColorVertex;
			float4 _BaseMap_ST;
			fixed4 _BaseColor;
			float _Cutoff;
			float4 _StylizedThreshold_ST;
			float _RampThreshold;
			float _RampSmoothing;
			float4 _SketchTexture_ST;
			half _SketchTexture_OffsetSpeed;
			fixed4 _SColor;
			fixed4 _HColor;
			float _BumpScale;
			// Tumor undulation
			float _UndulateAmplitude;
			float _UndulateFrequency;
			float _UndulateSpeed;
			// Tumor pulse
			fixed4 _PulseColor;
			float _PulseIntensity;
			float _PulseSpeed;
			float4 _EmissionNoise_ST;
			float4 _EmissionNoiseScroll;
			float _EmissionNoiseContrast;
		CBUFFER_END

		#define _SnapResolution 512

		// Hash without sin (Dave Hoskins - CC BY-SA 4.0)
		float2 hash22(float2 p)
		{
			float3 p3 = frac(p.xyx * float3(443.897, 441.423, 437.195));
			p3 += dot(p3, p3.yzx + 19.19);
			return frac((p3.xx+p3.yz)*p3.zy);
		}

		// Organic multi-wave undulation — returns object-space displacement along the normal
		// worldPos: world-space position of the vertex
		// objectScaleMax: max object scale axis (to keep amplitude scale-independent)
		float TumorUndulateDisplacement(float3 worldPos, float objectScaleMax)
		{
			float t = _Time.y;
			// Three overlapping spatial phases for organic variation
			float phase1 = dot(worldPos, float3(1.37,  2.11,  0.73));
			float phase2 = dot(worldPos, float3(0.59,  1.43,  2.07));
			float phase3 = dot(worldPos, float3(2.31,  0.87,  1.19));
			// Waves at slightly incommensurate frequencies so they never fully cancel or reinforce
			float wave1 = sin(t * _UndulateSpeed        + phase1 * _UndulateFrequency);
			float wave2 = sin(t * _UndulateSpeed * 1.31 + phase2 * _UndulateFrequency * 0.73) * 0.5;
			float wave3 = sin(t * _UndulateSpeed * 0.67 + phase3 * _UndulateFrequency * 1.47) * 0.3;
			float combined = (wave1 + wave2 + wave3) / 1.8; // roughly normalise to [-1, 1]
			return combined * _UndulateAmplitude / max(objectScaleMax, 0.0001);
		}

		// Built-in renderer (CG) to SRP (HLSL) bindings
		#define UnityObjectToClipPos TransformObjectToHClip
		#define _WorldSpaceLightPos0 _MainLightPosition

		#if defined(_DBUFFER)
			DecalSurfaceData GetDecals(float4 positionCS)
			{
				FETCH_DBUFFER(DBuffer, _DBufferTexture, int2(positionCS.xy));
				DecalSurfaceData decalSurfaceData = (DecalSurfaceData)0;
				DECODE_FROM_DBUFFER(DBuffer, decalSurfaceData);
				#if !defined(_DBUFFER_MRT3)
					decalSurfaceData.MAOSAlpha = 0;
				#endif
				return decalSurfaceData;
			}
		#endif

		ENDHLSL

		// ─────────────────────────────────────────────────────────
		// Outline Include
		// ─────────────────────────────────────────────────────────
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

			// ── Tumor undulation (outline pass) ──────────────────
			{
				float3 worldPos_o = mul(unity_ObjectToWorld, v.vertex).xyz;
				float3 objectScale_o;
				objectScale_o.x = length(unity_ObjectToWorld._m00_m10_m20);
				objectScale_o.y = length(unity_ObjectToWorld._m01_m11_m21);
				objectScale_o.z = length(unity_ObjectToWorld._m02_m12_m22);
				float scaleMax_o = max(max(objectScale_o.x, objectScale_o.y), objectScale_o.z);
				float disp_o = TumorUndulateDisplacement(worldPos_o, scaleMax_o);
				v.vertex.xyz += v.normal * disp_o;
			}
			// ─────────────────────────────────────────────────────

			// Shader Properties Sampling
			float __outlineWidth = ( _OutlineWidth );
			float4 __outlineColorVertex = ( _OutlineColorVertex.rgba );

			#ifdef TCP2_COLORS_AS_NORMALS
				float3 normal = (v.vertexColor.xyz*2) - 1;
			#elif TCP2_TANGENT_AS_NORMALS
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
				float3 normal = v.uvChannel.xyz;
				#else
				#if TCP2_UV_NORMALS_ZW
					#define ch1 z
					#define ch2 w
				#else
					#define ch1 x
					#define ch2 y
				#endif
				float3 n;
				v.uvChannel.ch1 = v.uvChannel.ch1 * 255.0/16.0;
				n.x = floor(v.uvChannel.ch1) / 15.0;
				n.y = frac(v.uvChannel.ch1) * 16.0 / 15.0;
				n.z = v.uvChannel.ch2;
				n = n*2 - 1;
				float3 normal = n;
				#endif
			#else
				float3 normal = v.normal;
			#endif

			#if TCP2_ZSMOOTH_ON
				normal = UnityObjectToViewPos(normal);
				normal.z = -_ZSmooth;
			#endif
			float size = 1;

			#if !defined(SHADOWCASTER_PASS)
				output.vertex = UnityObjectToClipPos(v.vertex.xyz);
				output.vertex.xy = floor(output.vertex.xy / output.vertex.w * _SnapResolution + 0.5) / _SnapResolution * output.vertex.w;
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

			float4 __outlineColor = ( float4(1,1,1,1) );

			half4 outlineColor = __outlineColor * input.vcolor.xyzw;
			outlineColor.a *= _BaseColor.a;
			outlineColor.rgb = MixFog(outlineColor.rgb, input.pack1.x);

			return outlineColor;
		}

		ENDHLSL
		// Outline Include End

		// ─────────────────────────────────────────────────────────
		// Main Pass
		// ─────────────────────────────────────────────────────────
		Pass
		{
			Name "Main"
			Tags
			{
				"LightMode"="UniversalForward"
			}
			Blend [_SrcBlend] [_DstBlend]
			Cull [_Cull]
			ZWrite [_ZWrite]

			HLSLPROGRAM
			#pragma prefer_hlslcc gles
			#pragma exclude_renderers d3d11_9x
			#pragma target 3.0

			#pragma shader_feature_local _ _RECEIVE_SHADOWS_OFF

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

			#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			#pragma multi_compile _ SHADOWS_SHADOWMASK
			#pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
			#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

			#pragma multi_compile_instancing

			#pragma vertex Vertex
			#pragma fragment Fragment

			#pragma shader_feature_local _ _ALPHAPREMULTIPLY_ON
			#pragma shader_feature_local_fragment _ALPHATEST_ON
			#pragma shader_feature_local_fragment TCP2_SKETCH
			#pragma shader_feature_local TCP2_NORMAL_MAP

			struct Attributes
			{
				float4 vertex    : POSITION;
				float3 normal    : NORMAL;
				float4 tangent   : TANGENT;
				float4 texcoord0 : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS     : SV_POSITION;
				float3 normal         : NORMAL;
				float4 worldPosAndFog : TEXCOORD0;
				#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
				float4 shadowCoord    : TEXCOORD1;
				#endif
				#ifdef _ADDITIONAL_LIGHTS_VERTEX
				half3 vertexLights    : TEXCOORD2;
				#endif
				float4 screenPosition : TEXCOORD3;
				float2 pack1          : TEXCOORD4; /* pack1.xy = texcoord0 */
				float pack2           : TEXCOORD5; /* pack2.x  = fogFactor  */
				#if defined(TCP2_NORMAL_MAP)
				float3 tangentWS      : TEXCOORD6;
				float3 bitangentWS    : TEXCOORD7;
				#endif
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			#if USE_FORWARD_PLUS || USE_CLUSTER_LIGHT_LOOP
				struct InputDataForwardPlusDummy
				{
					float3 positionWS;
					float2 normalizedScreenSpaceUV;
				};
			#endif

			Varyings Vertex(Attributes input)
			{
				Varyings output = (Varyings)0;

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				// ── Tumor undulation (main pass) ──────────────────
				{
					float3 worldPos_m = mul(unity_ObjectToWorld, input.vertex).xyz;
					float3 objectScale_m;
					objectScale_m.x = length(unity_ObjectToWorld._m00_m10_m20);
					objectScale_m.y = length(unity_ObjectToWorld._m01_m11_m21);
					objectScale_m.z = length(unity_ObjectToWorld._m02_m12_m22);
					float scaleMax_m = max(max(objectScale_m.x, objectScale_m.y), objectScale_m.z);
					float disp_m = TumorUndulateDisplacement(worldPos_m, scaleMax_m);
					input.vertex.xyz += input.normal * disp_m;
				}
				// ─────────────────────────────────────────────────

				// Texture Coordinates
				output.pack1.xy.xy = input.texcoord0.xy * _BaseMap_ST.xy + _BaseMap_ST.zw;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.vertex.xyz);
				#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
				output.shadowCoord = GetShadowCoord(vertexInput);
				#endif
				float4 clipPos = vertexInput.positionCS;
				// PS1-style vertex snapping
				clipPos.xy = floor(clipPos.xy / clipPos.w * _SnapResolution + 0.5) / _SnapResolution * clipPos.w;

				float4 screenPos = ComputeScreenPos(clipPos);
				output.screenPosition.xyzw = screenPos;

				VertexNormalInputs vertexNormalInput = GetVertexNormalInputs(input.normal, input.tangent);
				#if defined(TCP2_NORMAL_MAP)
				output.tangentWS   = vertexNormalInput.tangentWS;
				output.bitangentWS = vertexNormalInput.bitangentWS;
				#endif
				#ifdef _ADDITIONAL_LIGHTS_VERTEX
				output.vertexLights = VertexLighting(vertexInput.positionWS, vertexNormalInput.normalWS);
				#endif

				output.worldPosAndFog = float4(vertexInput.positionWS.xyz, 0);
				output.worldPosAndFog.w = ComputeFogFactor(vertexInput.positionCS.z);
				output.normal = normalize(vertexNormalInput.normalWS);
				output.positionCS = clipPos;

				return output;
			}

			half4 Fragment(Varyings input) : SV_Target
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

				// Screen Space UV
				float2 screenUV = input.screenPosition.xyzw.xy / input.screenPosition.xyzw.w;

				// Shader Properties Sampling
				float4 __albedo              = TCP2_TEX2D_SAMPLE(_BaseMap, _BaseMap, input.pack1.xy).rgba;
				float4 __mainColor           = _BaseColor.rgba;
				float  __alpha               = __albedo.a * __mainColor.a;
				float  __ambientIntensity    = 1.0;
				float  __stylizedThreshold   = TCP2_TEX2D_SAMPLE(_StylizedThreshold, _StylizedThreshold, input.pack1.xy * _StylizedThreshold_ST.xy + _StylizedThreshold_ST.zw).a;
				float  __stylizedThresholdScale = 1.0;
				float  __rampThreshold       = _RampThreshold;
				float  __rampSmoothing       = _RampSmoothing;
				float3 __sketchColor         = float3(0,0,0);
				float3 __sketchTexture       = TCP2_TEX2D_SAMPLE(_SketchTexture, _SketchTexture, screenUV * _ScreenParams.zw * _SketchTexture_ST.xy + _SketchTexture_ST.zw + hash22(floor(_Time.xx * _SketchTexture_OffsetSpeed.xx) / _SketchTexture_OffsetSpeed.xx)).aaa;
				float  __sketchThresholdScale= 1.0;
				float3 __shadowColor         = _SColor.rgb;
				float3 __highlightColor      = _HColor.rgb;

				half3 albedo = __albedo.rgb;
				half  alpha  = __alpha;

				#if defined(_ALPHATEST_ON)
				clip(alpha - _Cutoff);
				#endif

				// URP Decals
				#if defined(_DBUFFER)
					#if defined(_DBUFFER_MRT2) || defined(_DBUFFER_MRT3)
						#define HAS_DECAL_NORMALS
					#endif
					DecalSurfaceData decals = GetDecals(input.positionCS);
					albedo.rgb = albedo.rgb * decals.baseColor.a + decals.baseColor.rgb;
					#if defined(HAS_DECAL_NORMALS)
					if (decals.normalWS.w < 1.0)
						normalWS.xyz = normalize(normalWS.xyz * decals.normalWS.w + decals.normalWS.xyz);
					#endif
				#endif

				half3 emission = half3(0,0,0);

				// ── Tumor pulse: noise-masked emissive that crawls ─
				{
					// Two layers of the same noise at different scroll speeds
					// for a turbulent, non-repeating look
					float2 noiseUV1 = input.pack1.xy * _EmissionNoise_ST.xy + _EmissionNoise_ST.zw
					                + _Time.y * _EmissionNoiseScroll.xy;
					float2 noiseUV2 = input.pack1.xy * _EmissionNoise_ST.xy * 0.7 + _EmissionNoise_ST.zw
					                + _Time.y * _EmissionNoiseScroll.xy * -0.6 + float2(0.3, 0.7);

					float noise1 = TCP2_TEX2D_SAMPLE(_EmissionNoise, _EmissionNoise, noiseUV1).r;
					float noise2 = TCP2_TEX2D_SAMPLE(_EmissionNoise, _EmissionNoise, noiseUV2).r;

					// Multiply the two layers so emission only fires where both are bright
					float noiseMask = noise1 * noise2;

					// Push contrast: thin out the emissive patches so they read as veins/spots
					noiseMask = saturate(pow(noiseMask, _EmissionNoiseContrast));

					// Slow breathing pulse modulates the overall brightness
					float pulseA = sin(_Time.y * _PulseSpeed)        * 0.5 + 0.5;
					float pulseB = sin(_Time.y * _PulseSpeed * 1.37 + 1.1) * 0.5 + 0.5;
					float pulse  = pulseA * 0.7 + pulseB * 0.3;

					emission += _PulseColor.rgb * noiseMask * pulse * _PulseIntensity;
				}
				// ─────────────────────────────────────────────────

				albedo *= __mainColor.rgb;

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

				half3 bakedGI = SampleSH(normalWS);
				half  occlusion = 1;
				half3 indirectDiffuse = bakedGI * occlusion * albedo * __ambientIntensity;

				half3 lightDir   = mainLight.direction;
				half3 lightColor = mainLight.color.rgb;
				half  atten      = mainLight.shadowAttenuation * mainLight.distanceAttenuation;

				half  ndl = dot(normalWS, lightDir);
				float stylizedThreshold = __stylizedThreshold;
				stylizedThreshold -= 0.5;
				stylizedThreshold *= __stylizedThresholdScale;
				ndl += stylizedThreshold;

				half rampThreshold = __rampThreshold;
				half rampSmooth    = __rampSmoothing * 0.5;
				ndl = saturate(ndl);
				half3 ramp = smoothstep(rampThreshold - rampSmooth, rampThreshold + rampSmooth, ndl);
				ramp *= atten;

				half3 color = half3(0,0,0);
				half3 accumulatedRamp   = ramp * max(lightColor.r, max(lightColor.g, lightColor.b));
				half3 accumulatedColors = ramp * lightColor.rgb;

				// Additional lights loop
				#ifdef _ADDITIONAL_LIGHTS
				uint pixelLightCount = GetAdditionalLightsCount();

				#if USE_FORWARD_PLUS || USE_CLUSTER_LIGHT_LOOP
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
								half3 lightDir   = half3(0, 1, 0);
								half3 lightColor = half3(0, 0, 0);
								if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
								{
									lightColor = light.color.rgb;
									lightDir   = light.direction;
								}
							#else
								half3 lightColor = light.color.rgb;
								half3 lightDir   = light.direction;
							#endif
							half ndl = dot(normalWS, lightDir);
							float stylizedThreshold = __stylizedThreshold;
							stylizedThreshold -= 0.5;
							stylizedThreshold *= __stylizedThresholdScale;
							ndl += stylizedThreshold;
							ndl = saturate(ndl);
							half3 ramp = smoothstep(rampThreshold - rampSmooth, rampThreshold + rampSmooth, ndl);
							ramp *= atten;
							accumulatedRamp   += ramp * max(lightColor.r, max(lightColor.g, lightColor.b));
							accumulatedColors += ramp * lightColor.rgb;
						}
					}

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
						half3 lightDir   = half3(0, 1, 0);
						half3 lightColor = half3(0, 0, 0);
						if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
						{
							lightColor = light.color.rgb;
							lightDir   = light.direction;
						}
					#else
						half3 lightColor = light.color.rgb;
						half3 lightDir   = light.direction;
					#endif
					half ndl = dot(normalWS, lightDir);
					float stylizedThreshold = __stylizedThreshold;
					stylizedThreshold -= 0.5;
					stylizedThreshold *= __stylizedThresholdScale;
					ndl += stylizedThreshold;
					ndl = saturate(ndl);
					half3 ramp = smoothstep(rampThreshold - rampSmooth, rampThreshold + rampSmooth, ndl);
					ramp *= atten;
					accumulatedRamp   += ramp * max(lightColor.r, max(lightColor.g, lightColor.b));
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

				color += indirectDiffuse;

				#if defined(_ALPHAPREMULTIPLY_ON)
				color.rgb *= alpha;
				#endif

				color += emission;

				float fogFactor = input.worldPosAndFog.w;
				color = MixFog(color, fogFactor);

				return half4(color, alpha);
			}
			ENDHLSL
		}

		// ─────────────────────────────────────────────────────────
		// Outline Pass
		// ─────────────────────────────────────────────────────────
		Pass
		{
			Name "Outline"
			Tags { "LightMode" = "Outline" }
			Cull Front
			Blend [_SrcBlend] [_DstBlend]
			ZWrite On

			HLSLPROGRAM
			#pragma vertex vertex_outline
			#pragma fragment fragment_outline
			#pragma target 3.0
			#pragma multi_compile _ TCP2_COLORS_AS_NORMALS TCP2_TANGENT_AS_NORMALS TCP2_UV1_AS_NORMALS TCP2_UV2_AS_NORMALS TCP2_UV3_AS_NORMALS TCP2_UV4_AS_NORMALS
			#pragma multi_compile _ TCP2_UV_NORMALS_FULL TCP2_UV_NORMALS_ZW
			#pragma multi_compile_instancing
			ENDHLSL
		}

		// ─────────────────────────────────────────────────────────
		// Depth & Shadow Caster Passes
		// ─────────────────────────────────────────────────────────
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
				float4 vertex    : POSITION;
				float3 normal    : NORMAL;
				float4 texcoord0 : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				#if defined(DEPTH_NORMALS_PASS)
				float3 normalWS   : TEXCOORD0;
				#endif
				float2 pack0 : TEXCOORD1; /* pack0.xy = texcoord0 */
				#if defined(DEPTH_ONLY_PASS)
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
				#endif
			};

			float4 GetShadowPositionHClip(Attributes input)
			{
				float3 positionWS = TransformObjectToWorld(input.vertex.xyz);
				float3 normalWS   = TransformObjectToWorldNormal(input.normal);

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

				// ── Tumor undulation (depth/shadow pass) ─────────
				{
					float3 worldPos_ds = mul(unity_ObjectToWorld, input.vertex).xyz;
					float3 objectScale_ds;
					objectScale_ds.x = length(unity_ObjectToWorld._m00_m10_m20);
					objectScale_ds.y = length(unity_ObjectToWorld._m01_m11_m21);
					objectScale_ds.z = length(unity_ObjectToWorld._m02_m12_m22);
					float scaleMax_ds = max(max(objectScale_ds.x, objectScale_ds.y), objectScale_ds.z);
					float disp_ds = TumorUndulateDisplacement(worldPos_ds, scaleMax_ds);
					input.vertex.xyz += input.normal * disp_ds;
				}
				// ─────────────────────────────────────────────────

				// Texture Coordinates
				output.pack0.xy.xy = input.texcoord0.xy * _BaseMap_ST.xy + _BaseMap_ST.zw;

				#if defined(DEPTH_ONLY_PASS)
					output.positionCS = TransformObjectToHClip(input.vertex.xyz);
					#if defined(DEPTH_NORMALS_PASS)
					output.normalWS = TransformObjectToWorldNormal(input.normal);
					#endif
				#elif defined(SHADOW_CASTER_PASS)
					output.positionCS = GetShadowPositionHClip(input);
				#else
					output.positionCS = float4(0,0,0,0);
				#endif

				return output;
			}

			half4 ShadowDepthPassFragment(
				Varyings input
			#if defined(DEPTH_NORMALS_PASS) && defined(_WRITE_RENDERING_LAYERS)
				#if UNITY_VERSION >= 60020000
				, out uint outRenderingLayers : SV_Target1
				#else
				, out float4 outRenderingLayers : SV_Target1
				#endif
			#endif
			) : SV_TARGET
			{
				#if defined(DEPTH_ONLY_PASS)
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				#endif

				float4 __albedo    = TCP2_TEX2D_SAMPLE(_BaseMap, _BaseMap, input.pack0.xy).rgba;
				float4 __mainColor = _BaseColor.rgba;
				float  __alpha     = __albedo.a * __mainColor.a;

				half3 albedo   = half3(1,1,1);
				half  alpha    = __alpha;
				half3 emission = half3(0,0,0);

				#if defined(_ALPHATEST_ON)
				clip(alpha - _Cutoff);
				#endif

				#if defined(DEPTH_NORMALS_PASS)
					#if defined(_WRITE_RENDERING_LAYERS)
						#if UNITY_VERSION >= 60020000
							outRenderingLayers = EncodeMeshRenderingLayer();
						#else
							outRenderingLayers = float4(EncodeMeshRenderingLayer(GetMeshRenderingLayer()), 0, 0, 0);
						#endif
					#endif
					#if defined(URP_12_OR_NEWER)
						return float4(input.normalWS.xyz, 0.0);
					#else
						return float4(PackNormalOctRectEncode(TransformWorldToViewDir(input.normalWS, true)), 0.0, 0.0);
					#endif
				#endif

				return 0;
			}

		#endif
		ENDHLSL

		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }
			ZWrite On
			ZTest LEqual
			Cull [_faceCulling]

			HLSLPROGRAM
			#pragma prefer_hlslcc gles
			#pragma exclude_renderers d3d11_9x
			#pragma target 2.0
			#pragma multi_compile SHADOW_CASTER_PASS
			#pragma multi_compile_instancing
			#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
			#pragma shader_feature_local_fragment _ALPHATEST_ON
			#pragma vertex ShadowDepthPassVertex
			#pragma fragment ShadowDepthPassFragment
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
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
			#pragma multi_compile_instancing
			#pragma multi_compile DEPTH_ONLY_PASS
			#pragma shader_feature_local_fragment _ALPHATEST_ON
			#pragma vertex ShadowDepthPassVertex
			#pragma fragment ShadowDepthPassFragment
			ENDHLSL
		}

		Pass
		{
			Name "DepthNormals"
			Tags { "LightMode" = "DepthNormals" }
			ZWrite On
			Cull [_faceCulling]

			HLSLPROGRAM
			#pragma exclude_renderers gles gles3 glcore
			#pragma target 2.0
			#pragma multi_compile_instancing
			#pragma multi_compile DEPTH_ONLY_PASS
			#pragma multi_compile DEPTH_NORMALS_PASS
			#pragma shader_feature_local_fragment _ALPHATEST_ON
			#pragma vertex ShadowDepthPassVertex
			#pragma fragment ShadowDepthPassFragment
			ENDHLSL
		}
	}

	FallBack "Hidden/InternalErrorShader"
}
