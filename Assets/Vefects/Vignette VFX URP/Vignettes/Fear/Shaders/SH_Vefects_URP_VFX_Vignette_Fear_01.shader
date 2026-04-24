// Made with Amplify Shader Editor v1.9.7.1
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Vefects/SH_Vefects_URP_VFX_Vignette_Fear_01"
{
	Properties
	{
		[HideInInspector] _EmissionColor("Emission Color", Color) = (1,1,1,1)
		[HideInInspector] _AlphaCutoff("Alpha Cutoff ", Range(0, 1)) = 0.5
		_EmissionMultiply("Emission Multiply", Float) = 3
		[Space(33)][Header(LUT)][Space(13)]_LUT("LUT", 2D) = "white" {}
		_LUTSSSmoothness("LUT SS Smoothness", Float) = 1
		_LUTRange("LUT Range", Float) = 1
		_LUTPan("LUT Pan", Float) = 0
		_LUTOffset("LUT Offset", Float) = 0
		[Space(33)][Header(Noise)][Space(13)]_NoiseTexture("Noise Texture", 2D) = "white" {}
		_EdgeMaskPower("Edge Mask Power", Float) = 1
		_EdgeMaskMultiply("Edge Mask Multiply", Float) = 1
		_OpacityMultiply("Opacity Multiply", Float) = 1
		_OpacitySSSmoothness("Opacity SS Smoothness", Float) = 0.25
		[Space(33)][Header(Distortion)][Space(13)]_DistortionTexture("Distortion Texture", 2D) = "white" {}
		_DistortionUVScale("Distortion UV Scale", Vector) = (2,0.1,0,0)
		_DistortionUVPanSpeed("Distortion UV Pan Speed", Vector) = (0.1,1,0,0)
		_DistortionUVSwirl("Distortion UV Swirl", Float) = 0.3
		_NoiseUVScale("Noise UV Scale", Vector) = (1,0.1,0,0)
		_NoiseUVPanSpeed("Noise UV Pan Speed", Vector) = (0.1,1.5,0,0)
		_NoiseUVSwirl("Noise UV Swirl", Float) = 0.3
		_DistortionMultiply("Distortion Multiply", Float) = 0.1
		[Space(13)][Header(AR)][Space(13)]_Cull1("Cull", Float) = 2
		_Src1("Src", Float) = 5
		_Dst1("Dst", Float) = 10
		_ZWrite1("ZWrite", Float) = 0
		_ZTest1("ZTest", Float) = 2


		//_TessPhongStrength( "Tess Phong Strength", Range( 0, 1 ) ) = 0.5
		//_TessValue( "Tess Max Tessellation", Range( 1, 32 ) ) = 16
		//_TessMin( "Tess Min Distance", Float ) = 10
		//_TessMax( "Tess Max Distance", Float ) = 25
		//_TessEdgeLength ( "Tess Edge length", Range( 2, 50 ) ) = 16
		//_TessMaxDisp( "Tess Max Displacement", Float ) = 25

		[HideInInspector] _QueueOffset("_QueueOffset", Float) = 0
        [HideInInspector] _QueueControl("_QueueControl", Float) = -1

        [HideInInspector][NoScaleOffset] unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}

		//[HideInInspector][ToggleUI] _AddPrecomputedVelocity("Add Precomputed Velocity", Float) = 1
		[HideInInspector][ToggleOff] _ReceiveShadows("Receive Shadows", Float) = 1.0
	}

	SubShader
	{
		LOD 0

		

		Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "UniversalMaterialType"="Unlit" }

		Cull [_Cull1]
		AlphaToMask Off

		

		HLSLINCLUDE
		#pragma target 3.5
		#pragma prefer_hlslcc gles
		// ensure rendering platforms toggle list is visible

		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"

		#ifndef ASE_TESS_FUNCS
		#define ASE_TESS_FUNCS
		float4 FixedTess( float tessValue )
		{
			return tessValue;
		}

		float CalcDistanceTessFactor (float4 vertex, float minDist, float maxDist, float tess, float4x4 o2w, float3 cameraPos )
		{
			float3 wpos = mul(o2w,vertex).xyz;
			float dist = distance (wpos, cameraPos);
			float f = clamp(1.0 - (dist - minDist) / (maxDist - minDist), 0.01, 1.0) * tess;
			return f;
		}

		float4 CalcTriEdgeTessFactors (float3 triVertexFactors)
		{
			float4 tess;
			tess.x = 0.5 * (triVertexFactors.y + triVertexFactors.z);
			tess.y = 0.5 * (triVertexFactors.x + triVertexFactors.z);
			tess.z = 0.5 * (triVertexFactors.x + triVertexFactors.y);
			tess.w = (triVertexFactors.x + triVertexFactors.y + triVertexFactors.z) / 3.0f;
			return tess;
		}

		float CalcEdgeTessFactor (float3 wpos0, float3 wpos1, float edgeLen, float3 cameraPos, float4 scParams )
		{
			float dist = distance (0.5 * (wpos0+wpos1), cameraPos);
			float len = distance(wpos0, wpos1);
			float f = max(len * scParams.y / (edgeLen * dist), 1.0);
			return f;
		}

		float DistanceFromPlane (float3 pos, float4 plane)
		{
			float d = dot (float4(pos,1.0f), plane);
			return d;
		}

		bool WorldViewFrustumCull (float3 wpos0, float3 wpos1, float3 wpos2, float cullEps, float4 planes[6] )
		{
			float4 planeTest;
			planeTest.x = (( DistanceFromPlane(wpos0, planes[0]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[0]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[0]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.y = (( DistanceFromPlane(wpos0, planes[1]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[1]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[1]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.z = (( DistanceFromPlane(wpos0, planes[2]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[2]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[2]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.w = (( DistanceFromPlane(wpos0, planes[3]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[3]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[3]) > -cullEps) ? 1.0f : 0.0f );
			return !all (planeTest);
		}

		float4 DistanceBasedTess( float4 v0, float4 v1, float4 v2, float tess, float minDist, float maxDist, float4x4 o2w, float3 cameraPos )
		{
			float3 f;
			f.x = CalcDistanceTessFactor (v0,minDist,maxDist,tess,o2w,cameraPos);
			f.y = CalcDistanceTessFactor (v1,minDist,maxDist,tess,o2w,cameraPos);
			f.z = CalcDistanceTessFactor (v2,minDist,maxDist,tess,o2w,cameraPos);

			return CalcTriEdgeTessFactors (f);
		}

		float4 EdgeLengthBasedTess( float4 v0, float4 v1, float4 v2, float edgeLength, float4x4 o2w, float3 cameraPos, float4 scParams )
		{
			float3 pos0 = mul(o2w,v0).xyz;
			float3 pos1 = mul(o2w,v1).xyz;
			float3 pos2 = mul(o2w,v2).xyz;
			float4 tess;
			tess.x = CalcEdgeTessFactor (pos1, pos2, edgeLength, cameraPos, scParams);
			tess.y = CalcEdgeTessFactor (pos2, pos0, edgeLength, cameraPos, scParams);
			tess.z = CalcEdgeTessFactor (pos0, pos1, edgeLength, cameraPos, scParams);
			tess.w = (tess.x + tess.y + tess.z) / 3.0f;
			return tess;
		}

		float4 EdgeLengthBasedTessCull( float4 v0, float4 v1, float4 v2, float edgeLength, float maxDisplacement, float4x4 o2w, float3 cameraPos, float4 scParams, float4 planes[6] )
		{
			float3 pos0 = mul(o2w,v0).xyz;
			float3 pos1 = mul(o2w,v1).xyz;
			float3 pos2 = mul(o2w,v2).xyz;
			float4 tess;

			if (WorldViewFrustumCull(pos0, pos1, pos2, maxDisplacement, planes))
			{
				tess = 0.0f;
			}
			else
			{
				tess.x = CalcEdgeTessFactor (pos1, pos2, edgeLength, cameraPos, scParams);
				tess.y = CalcEdgeTessFactor (pos2, pos0, edgeLength, cameraPos, scParams);
				tess.z = CalcEdgeTessFactor (pos0, pos1, edgeLength, cameraPos, scParams);
				tess.w = (tess.x + tess.y + tess.z) / 3.0f;
			}
			return tess;
		}
		#endif //ASE_TESS_FUNCS
		ENDHLSL

		
		Pass
		{
			
			Name "Forward"
			Tags { "LightMode"="UniversalForwardOnly" }

			Blend [_Src1] [_Dst1], One OneMinusSrcAlpha
			ZWrite Off
			ZTest [_ZTest1]
			Offset 0 , 0
			ColorMask RGBA

			

			HLSLPROGRAM

			#pragma shader_feature_local _RECEIVE_SHADOWS_OFF
			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define ASE_VERSION 19701
			#define ASE_SRP_VERSION 170003


			#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
			#pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
			#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
			#pragma multi_compile_fragment _ DEBUG_DISPLAY

			#pragma vertex vert
			#pragma fragment frag

			#define SHADERPASS SHADERPASS_UNLIT

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging3D.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float4 clipPosV : TEXCOORD0;
				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
					float3 positionWS : TEXCOORD1;
				#endif
				#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					float4 shadowCoord : TEXCOORD2;
				#endif
				#ifdef ASE_FOG
					float fogFactor : TEXCOORD3;
				#endif
				float4 ase_texcoord4 : TEXCOORD4;
				float4 ase_texcoord5 : TEXCOORD5;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float2 _DistortionUVScale;
			float2 _NoiseUVScale;
			float2 _NoiseUVPanSpeed;
			float2 _DistortionUVPanSpeed;
			float _EmissionMultiply;
			float _LUTOffset;
			float _LUTRange;
			float _EdgeMaskMultiply;
			float _EdgeMaskPower;
			float _DistortionMultiply;
			float _DistortionUVSwirl;
			float _Cull1;
			float _NoiseUVSwirl;
			float _LUTSSSmoothness;
			float _LUTPan;
			float _ZWrite1;
			float _ZTest1;
			float _Dst1;
			float _Src1;
			float _OpacitySSSmoothness;
			float _OpacityMultiply;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			sampler2D _LUT;
			sampler2D _NoiseTexture;
			sampler2D _DistortionTexture;


			
			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.ase_texcoord4 = input.ase_texcoord;
				output.ase_texcoord5 = input.ase_texcoord1;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
					output.positionWS = vertexInput.positionWS;
				#endif

				#ifdef ASE_FOG
					output.fogFactor = ComputeFogFactor( vertexInput.positionCS.z );
				#endif

				#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					output.shadowCoord = GetShadowCoord( vertexInput );
				#endif

				output.positionCS = vertexInput.positionCS;
				output.clipPosV = vertexInput.positionCS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 vertex : INTERNALTESSPOS;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.vertex = input.positionOS;
				output.normalOS = input.normalOS;
				output.ase_texcoord = input.ase_texcoord;
				output.ase_texcoord1 = input.ase_texcoord1;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].vertex, input[1].vertex, input[2].vertex, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].vertex, input[1].vertex, input[2].vertex, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].vertex, input[1].vertex, input[2].vertex, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].vertex * bary.x + patch[1].vertex * bary.y + patch[2].vertex * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				output.ase_texcoord1 = patch[0].ase_texcoord1 * bary.x + patch[1].ase_texcoord1 * bary.y + patch[2].ase_texcoord1 * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].vertex.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag ( PackedVaryings input
				#ifdef _WRITE_RENDERING_LAYERS
				, out float4 outRenderingLayers : SV_Target1
				#endif
				 ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( input );
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
					float3 WorldPosition = input.positionWS;
				#endif

				float4 ShadowCoords = float4( 0, 0, 0, 0 );

				float4 ClipPos = input.clipPosV;
				float4 ScreenPos = ComputeScreenPos( input.clipPosV );

				#if defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
						ShadowCoords = input.shadowCoord;
					#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
						ShadowCoords = TransformWorldToShadowCoord( WorldPosition );
					#endif
				#endif

				float2 temp_cast_0 = (_LUTPan).xx;
				float temp_output_113_0 = ( 1.0 - input.ase_texcoord4.z );
				float2 temp_output_73_0_g51 = _NoiseUVPanSpeed;
				float2 texCoord68_g51 = input.ase_texcoord4.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_86_0_g51 = ( texCoord68_g51 - float2( 1,1 ) );
				float2 appendResult93_g51 = (float2(frac( ( atan2( (temp_output_86_0_g51).x , (temp_output_86_0_g51).y ) / TWO_PI ) ) , length( temp_output_86_0_g51 )));
				float2 panner59_g51 = ( ( (temp_output_73_0_g51).x * _TimeParameters.x ) * float2( 1,0 ) + appendResult93_g51);
				float2 texCoord103_g51 = input.ase_texcoord4.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_104_0_g51 = ( texCoord103_g51 - float2( 1,1 ) );
				float2 appendResult107_g51 = (float2(frac( ( atan2( (temp_output_104_0_g51).x , (temp_output_104_0_g51).y ) / TWO_PI ) ) , length( temp_output_104_0_g51 )));
				float2 panner60_g51 = ( ( _TimeParameters.x * (temp_output_73_0_g51).y ) * float2( 0,1 ) + appendResult93_g51);
				float2 appendResult90_g51 = (float2(( (panner59_g51).x + ( (appendResult107_g51).y * _NoiseUVSwirl ) ) , (panner60_g51).y));
				float randomUV233 = input.ase_texcoord5.z;
				float2 temp_output_73_0_g47 = _DistortionUVPanSpeed;
				float2 texCoord68_g47 = input.ase_texcoord4.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_86_0_g47 = ( texCoord68_g47 - float2( 1,1 ) );
				float2 appendResult93_g47 = (float2(frac( ( atan2( (temp_output_86_0_g47).x , (temp_output_86_0_g47).y ) / TWO_PI ) ) , length( temp_output_86_0_g47 )));
				float2 panner59_g47 = ( ( (temp_output_73_0_g47).x * _TimeParameters.x ) * float2( 1,0 ) + appendResult93_g47);
				float2 texCoord103_g47 = input.ase_texcoord4.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_104_0_g47 = ( texCoord103_g47 - float2( 1,1 ) );
				float2 appendResult107_g47 = (float2(frac( ( atan2( (temp_output_104_0_g47).x , (temp_output_104_0_g47).y ) / TWO_PI ) ) , length( temp_output_104_0_g47 )));
				float2 panner60_g47 = ( ( _TimeParameters.x * (temp_output_73_0_g47).y ) * float2( 0,1 ) + appendResult93_g47);
				float2 appendResult90_g47 = (float2(( (panner59_g47).x + ( (appendResult107_g47).y * _DistortionUVSwirl ) ) , (panner60_g47).y));
				float2 texCoord27_g50 = input.ase_texcoord4.xy * float2( 1,1 ) + float2( 0,0 );
				float2 temp_cast_3 = (1.0).xx;
				float2 temp_output_26_0_g50 = ( texCoord27_g50 - temp_cast_3 );
				float temp_output_13_0_g50 = frac( ( atan2( (temp_output_26_0_g50).x , (temp_output_26_0_g50).y ) / 6.283 ) );
				float2 temp_output_17_0_g50 = ( temp_output_26_0_g50 * temp_output_26_0_g50 );
				float temp_output_12_0_g50 = sqrt( ( (temp_output_17_0_g50).x + (temp_output_17_0_g50).y ) );
				float2 appendResult5_g50 = (float2(temp_output_13_0_g50 , temp_output_12_0_g50));
				float2 texCoord11_g48 = input.ase_texcoord4.xy * float2( 1,1 ) + float2( 0,0 );
				float2 temp_cast_4 = (0.0).xx;
				float2 appendResult3_g48 = (float2((appendResult5_g50).x , ( ( distance( max( ( abs( ( texCoord11_g48 + -0.5 ) ) - ( float2( 0.15,0.15 ) * 0.5 ) ) , float2( 0,0 ) ) , temp_cast_4 ) + 0.0 ) * 1.5 )));
				float temp_output_114_0 = saturate( ( tex2D( _NoiseTexture, ( float4( ( ( _NoiseUVScale * appendResult90_g51 ) + randomUV233 ), 0.0 , 0.0 ) + ( tex2D( _DistortionTexture, ( ( _DistortionUVScale * appendResult90_g47 ) + randomUV233 ) ) * _DistortionMultiply ) ).rg ).g * ( pow( (appendResult3_g48).y , _EdgeMaskPower ) * _EdgeMaskMultiply ) ) );
				float smoothstepResult111 = smoothstep( temp_output_113_0 , ( temp_output_113_0 + _LUTSSSmoothness ) , temp_output_114_0);
				float2 temp_cast_5 = (( ( saturate( smoothstepResult111 ) * _LUTRange ) + _LUTOffset )).xx;
				float2 panner224 = ( 1.0 * _Time.y * temp_cast_0 + temp_cast_5);
				
				float smoothstepResult115 = smoothstep( temp_output_113_0 , ( temp_output_113_0 + _OpacitySSSmoothness ) , temp_output_114_0);
				
				float3 BakedAlbedo = 0;
				float3 BakedEmission = 0;
				float3 Color = ( tex2D( _LUT, panner224 ) * _EmissionMultiply ).rgb;
				float Alpha = saturate( ( saturate( smoothstepResult115 ) * saturate( ( input.ase_texcoord4.z * _OpacityMultiply ) ) ) );
				float AlphaClipThreshold = 0.5;
				float AlphaClipThresholdShadow = 0.5;

				#ifdef _ALPHATEST_ON
					clip( Alpha - AlphaClipThreshold );
				#endif

				#if defined(_DBUFFER)
					ApplyDecalToBaseColor(input.positionCS, Color);
				#endif

				#ifdef LOD_FADE_CROSSFADE
					LODFadeCrossFade( input.positionCS );
				#endif

				#ifdef ASE_FOG
					Color = MixFog( Color, input.fogFactor );
				#endif

				#ifdef _WRITE_RENDERING_LAYERS
					uint renderingLayers = GetMeshRenderingLayer();
					outRenderingLayers = float4( EncodeMeshRenderingLayer( renderingLayers ), 0, 0, 0 );
				#endif

				return half4( Color, Alpha );
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "DepthOnly"
			Tags { "LightMode"="DepthOnly" }

			ZWrite On
			ColorMask 0
			AlphaToMask Off

			HLSLPROGRAM

			#pragma multi_compile_instancing
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define ASE_VERSION 19701
			#define ASE_SRP_VERSION 170003


			#pragma vertex vert
			#pragma fragment frag

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float4 clipPosV : TEXCOORD0;
				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
				float3 positionWS : TEXCOORD1;
				#endif
				#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
				float4 shadowCoord : TEXCOORD2;
				#endif
				float4 ase_texcoord3 : TEXCOORD3;
				float4 ase_texcoord4 : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float2 _DistortionUVScale;
			float2 _NoiseUVScale;
			float2 _NoiseUVPanSpeed;
			float2 _DistortionUVPanSpeed;
			float _EmissionMultiply;
			float _LUTOffset;
			float _LUTRange;
			float _EdgeMaskMultiply;
			float _EdgeMaskPower;
			float _DistortionMultiply;
			float _DistortionUVSwirl;
			float _Cull1;
			float _NoiseUVSwirl;
			float _LUTSSSmoothness;
			float _LUTPan;
			float _ZWrite1;
			float _ZTest1;
			float _Dst1;
			float _Src1;
			float _OpacitySSSmoothness;
			float _OpacityMultiply;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			sampler2D _NoiseTexture;
			sampler2D _DistortionTexture;


			
			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.ase_texcoord3 = input.ase_texcoord;
				output.ase_texcoord4 = input.ase_texcoord1;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
					output.positionWS = vertexInput.positionWS;
				#endif

				#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					output.shadowCoord = GetShadowCoord( vertexInput );
				#endif

				output.positionCS = vertexInput.positionCS;
				output.clipPosV = vertexInput.positionCS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 vertex : INTERNALTESSPOS;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.vertex = input.positionOS;
				output.normalOS = input.normalOS;
				output.ase_texcoord = input.ase_texcoord;
				output.ase_texcoord1 = input.ase_texcoord1;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].vertex, input[1].vertex, input[2].vertex, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].vertex, input[1].vertex, input[2].vertex, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].vertex, input[1].vertex, input[2].vertex, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].vertex * bary.x + patch[1].vertex * bary.y + patch[2].vertex * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				output.ase_texcoord1 = patch[0].ase_texcoord1 * bary.x + patch[1].ase_texcoord1 * bary.y + patch[2].ase_texcoord1 * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].vertex.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(PackedVaryings input  ) : SV_TARGET
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
				float3 WorldPosition = input.positionWS;
				#endif

				float4 ShadowCoords = float4( 0, 0, 0, 0 );

				float4 ClipPos = input.clipPosV;
				float4 ScreenPos = ComputeScreenPos( input.clipPosV );

				#if defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
						ShadowCoords = input.shadowCoord;
					#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
						ShadowCoords = TransformWorldToShadowCoord( WorldPosition );
					#endif
				#endif

				float temp_output_113_0 = ( 1.0 - input.ase_texcoord3.z );
				float2 temp_output_73_0_g51 = _NoiseUVPanSpeed;
				float2 texCoord68_g51 = input.ase_texcoord3.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_86_0_g51 = ( texCoord68_g51 - float2( 1,1 ) );
				float2 appendResult93_g51 = (float2(frac( ( atan2( (temp_output_86_0_g51).x , (temp_output_86_0_g51).y ) / TWO_PI ) ) , length( temp_output_86_0_g51 )));
				float2 panner59_g51 = ( ( (temp_output_73_0_g51).x * _TimeParameters.x ) * float2( 1,0 ) + appendResult93_g51);
				float2 texCoord103_g51 = input.ase_texcoord3.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_104_0_g51 = ( texCoord103_g51 - float2( 1,1 ) );
				float2 appendResult107_g51 = (float2(frac( ( atan2( (temp_output_104_0_g51).x , (temp_output_104_0_g51).y ) / TWO_PI ) ) , length( temp_output_104_0_g51 )));
				float2 panner60_g51 = ( ( _TimeParameters.x * (temp_output_73_0_g51).y ) * float2( 0,1 ) + appendResult93_g51);
				float2 appendResult90_g51 = (float2(( (panner59_g51).x + ( (appendResult107_g51).y * _NoiseUVSwirl ) ) , (panner60_g51).y));
				float randomUV233 = input.ase_texcoord4.z;
				float2 temp_output_73_0_g47 = _DistortionUVPanSpeed;
				float2 texCoord68_g47 = input.ase_texcoord3.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_86_0_g47 = ( texCoord68_g47 - float2( 1,1 ) );
				float2 appendResult93_g47 = (float2(frac( ( atan2( (temp_output_86_0_g47).x , (temp_output_86_0_g47).y ) / TWO_PI ) ) , length( temp_output_86_0_g47 )));
				float2 panner59_g47 = ( ( (temp_output_73_0_g47).x * _TimeParameters.x ) * float2( 1,0 ) + appendResult93_g47);
				float2 texCoord103_g47 = input.ase_texcoord3.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_104_0_g47 = ( texCoord103_g47 - float2( 1,1 ) );
				float2 appendResult107_g47 = (float2(frac( ( atan2( (temp_output_104_0_g47).x , (temp_output_104_0_g47).y ) / TWO_PI ) ) , length( temp_output_104_0_g47 )));
				float2 panner60_g47 = ( ( _TimeParameters.x * (temp_output_73_0_g47).y ) * float2( 0,1 ) + appendResult93_g47);
				float2 appendResult90_g47 = (float2(( (panner59_g47).x + ( (appendResult107_g47).y * _DistortionUVSwirl ) ) , (panner60_g47).y));
				float2 texCoord27_g50 = input.ase_texcoord3.xy * float2( 1,1 ) + float2( 0,0 );
				float2 temp_cast_2 = (1.0).xx;
				float2 temp_output_26_0_g50 = ( texCoord27_g50 - temp_cast_2 );
				float temp_output_13_0_g50 = frac( ( atan2( (temp_output_26_0_g50).x , (temp_output_26_0_g50).y ) / 6.283 ) );
				float2 temp_output_17_0_g50 = ( temp_output_26_0_g50 * temp_output_26_0_g50 );
				float temp_output_12_0_g50 = sqrt( ( (temp_output_17_0_g50).x + (temp_output_17_0_g50).y ) );
				float2 appendResult5_g50 = (float2(temp_output_13_0_g50 , temp_output_12_0_g50));
				float2 texCoord11_g48 = input.ase_texcoord3.xy * float2( 1,1 ) + float2( 0,0 );
				float2 temp_cast_3 = (0.0).xx;
				float2 appendResult3_g48 = (float2((appendResult5_g50).x , ( ( distance( max( ( abs( ( texCoord11_g48 + -0.5 ) ) - ( float2( 0.15,0.15 ) * 0.5 ) ) , float2( 0,0 ) ) , temp_cast_3 ) + 0.0 ) * 1.5 )));
				float temp_output_114_0 = saturate( ( tex2D( _NoiseTexture, ( float4( ( ( _NoiseUVScale * appendResult90_g51 ) + randomUV233 ), 0.0 , 0.0 ) + ( tex2D( _DistortionTexture, ( ( _DistortionUVScale * appendResult90_g47 ) + randomUV233 ) ) * _DistortionMultiply ) ).rg ).g * ( pow( (appendResult3_g48).y , _EdgeMaskPower ) * _EdgeMaskMultiply ) ) );
				float smoothstepResult115 = smoothstep( temp_output_113_0 , ( temp_output_113_0 + _OpacitySSSmoothness ) , temp_output_114_0);
				

				float Alpha = saturate( ( saturate( smoothstepResult115 ) * saturate( ( input.ase_texcoord3.z * _OpacityMultiply ) ) ) );
				float AlphaClipThreshold = 0.5;

				#ifdef _ALPHATEST_ON
					clip(Alpha - AlphaClipThreshold);
				#endif

				#ifdef LOD_FADE_CROSSFADE
					LODFadeCrossFade( input.positionCS );
				#endif
				return 0;
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "SceneSelectionPass"
			Tags { "LightMode"="SceneSelectionPass" }

			Cull Off
			AlphaToMask Off

			HLSLPROGRAM

			#define _SURFACE_TYPE_TRANSPARENT 1
			#define ASE_VERSION 19701
			#define ASE_SRP_VERSION 170003


			#pragma vertex vert
			#pragma fragment frag

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT
			#define SHADERPASS SHADERPASS_DEPTHONLY

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float2 _DistortionUVScale;
			float2 _NoiseUVScale;
			float2 _NoiseUVPanSpeed;
			float2 _DistortionUVPanSpeed;
			float _EmissionMultiply;
			float _LUTOffset;
			float _LUTRange;
			float _EdgeMaskMultiply;
			float _EdgeMaskPower;
			float _DistortionMultiply;
			float _DistortionUVSwirl;
			float _Cull1;
			float _NoiseUVSwirl;
			float _LUTSSSmoothness;
			float _LUTPan;
			float _ZWrite1;
			float _ZTest1;
			float _Dst1;
			float _Src1;
			float _OpacitySSSmoothness;
			float _OpacityMultiply;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			sampler2D _NoiseTexture;
			sampler2D _DistortionTexture;


			
			int _ObjectId;
			int _PassValue;

			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			PackedVaryings VertexFunction(Attributes input  )
			{
				PackedVaryings output;
				ZERO_INITIALIZE(PackedVaryings, output);

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.ase_texcoord = input.ase_texcoord;
				output.ase_texcoord1 = input.ase_texcoord1;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;

				float3 positionWS = TransformObjectToWorld( input.positionOS.xyz );

				output.positionCS = TransformWorldToHClip(positionWS);

				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 vertex : INTERNALTESSPOS;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.vertex = input.positionOS;
				output.normalOS = input.normalOS;
				output.ase_texcoord = input.ase_texcoord;
				output.ase_texcoord1 = input.ase_texcoord1;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].vertex, input[1].vertex, input[2].vertex, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].vertex, input[1].vertex, input[2].vertex, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].vertex, input[1].vertex, input[2].vertex, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].vertex * bary.x + patch[1].vertex * bary.y + patch[2].vertex * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				output.ase_texcoord1 = patch[0].ase_texcoord1 * bary.x + patch[1].ase_texcoord1 * bary.y + patch[2].ase_texcoord1 * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].vertex.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(PackedVaryings input ) : SV_TARGET
			{
				SurfaceDescription surfaceDescription = (SurfaceDescription)0;

				float temp_output_113_0 = ( 1.0 - input.ase_texcoord.z );
				float2 temp_output_73_0_g51 = _NoiseUVPanSpeed;
				float2 texCoord68_g51 = input.ase_texcoord.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_86_0_g51 = ( texCoord68_g51 - float2( 1,1 ) );
				float2 appendResult93_g51 = (float2(frac( ( atan2( (temp_output_86_0_g51).x , (temp_output_86_0_g51).y ) / TWO_PI ) ) , length( temp_output_86_0_g51 )));
				float2 panner59_g51 = ( ( (temp_output_73_0_g51).x * _TimeParameters.x ) * float2( 1,0 ) + appendResult93_g51);
				float2 texCoord103_g51 = input.ase_texcoord.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_104_0_g51 = ( texCoord103_g51 - float2( 1,1 ) );
				float2 appendResult107_g51 = (float2(frac( ( atan2( (temp_output_104_0_g51).x , (temp_output_104_0_g51).y ) / TWO_PI ) ) , length( temp_output_104_0_g51 )));
				float2 panner60_g51 = ( ( _TimeParameters.x * (temp_output_73_0_g51).y ) * float2( 0,1 ) + appendResult93_g51);
				float2 appendResult90_g51 = (float2(( (panner59_g51).x + ( (appendResult107_g51).y * _NoiseUVSwirl ) ) , (panner60_g51).y));
				float randomUV233 = input.ase_texcoord1.z;
				float2 temp_output_73_0_g47 = _DistortionUVPanSpeed;
				float2 texCoord68_g47 = input.ase_texcoord.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_86_0_g47 = ( texCoord68_g47 - float2( 1,1 ) );
				float2 appendResult93_g47 = (float2(frac( ( atan2( (temp_output_86_0_g47).x , (temp_output_86_0_g47).y ) / TWO_PI ) ) , length( temp_output_86_0_g47 )));
				float2 panner59_g47 = ( ( (temp_output_73_0_g47).x * _TimeParameters.x ) * float2( 1,0 ) + appendResult93_g47);
				float2 texCoord103_g47 = input.ase_texcoord.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_104_0_g47 = ( texCoord103_g47 - float2( 1,1 ) );
				float2 appendResult107_g47 = (float2(frac( ( atan2( (temp_output_104_0_g47).x , (temp_output_104_0_g47).y ) / TWO_PI ) ) , length( temp_output_104_0_g47 )));
				float2 panner60_g47 = ( ( _TimeParameters.x * (temp_output_73_0_g47).y ) * float2( 0,1 ) + appendResult93_g47);
				float2 appendResult90_g47 = (float2(( (panner59_g47).x + ( (appendResult107_g47).y * _DistortionUVSwirl ) ) , (panner60_g47).y));
				float2 texCoord27_g50 = input.ase_texcoord.xy * float2( 1,1 ) + float2( 0,0 );
				float2 temp_cast_2 = (1.0).xx;
				float2 temp_output_26_0_g50 = ( texCoord27_g50 - temp_cast_2 );
				float temp_output_13_0_g50 = frac( ( atan2( (temp_output_26_0_g50).x , (temp_output_26_0_g50).y ) / 6.283 ) );
				float2 temp_output_17_0_g50 = ( temp_output_26_0_g50 * temp_output_26_0_g50 );
				float temp_output_12_0_g50 = sqrt( ( (temp_output_17_0_g50).x + (temp_output_17_0_g50).y ) );
				float2 appendResult5_g50 = (float2(temp_output_13_0_g50 , temp_output_12_0_g50));
				float2 texCoord11_g48 = input.ase_texcoord.xy * float2( 1,1 ) + float2( 0,0 );
				float2 temp_cast_3 = (0.0).xx;
				float2 appendResult3_g48 = (float2((appendResult5_g50).x , ( ( distance( max( ( abs( ( texCoord11_g48 + -0.5 ) ) - ( float2( 0.15,0.15 ) * 0.5 ) ) , float2( 0,0 ) ) , temp_cast_3 ) + 0.0 ) * 1.5 )));
				float temp_output_114_0 = saturate( ( tex2D( _NoiseTexture, ( float4( ( ( _NoiseUVScale * appendResult90_g51 ) + randomUV233 ), 0.0 , 0.0 ) + ( tex2D( _DistortionTexture, ( ( _DistortionUVScale * appendResult90_g47 ) + randomUV233 ) ) * _DistortionMultiply ) ).rg ).g * ( pow( (appendResult3_g48).y , _EdgeMaskPower ) * _EdgeMaskMultiply ) ) );
				float smoothstepResult115 = smoothstep( temp_output_113_0 , ( temp_output_113_0 + _OpacitySSSmoothness ) , temp_output_114_0);
				

				surfaceDescription.Alpha = saturate( ( saturate( smoothstepResult115 ) * saturate( ( input.ase_texcoord.z * _OpacityMultiply ) ) ) );
				surfaceDescription.AlphaClipThreshold = 0.5;

				#if _ALPHATEST_ON
					float alphaClipThreshold = 0.01f;
					#if ALPHA_CLIP_THRESHOLD
						alphaClipThreshold = surfaceDescription.AlphaClipThreshold;
					#endif
					clip(surfaceDescription.Alpha - alphaClipThreshold);
				#endif

				half4 outColor = half4(_ObjectId, _PassValue, 1.0, 1.0);
				return outColor;
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "ScenePickingPass"
			Tags { "LightMode"="Picking" }

			AlphaToMask Off

			HLSLPROGRAM

			#define _SURFACE_TYPE_TRANSPARENT 1
			#define ASE_VERSION 19701
			#define ASE_SRP_VERSION 170003


			#pragma vertex vert
			#pragma fragment frag

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT

			#define SHADERPASS SHADERPASS_DEPTHONLY

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float2 _DistortionUVScale;
			float2 _NoiseUVScale;
			float2 _NoiseUVPanSpeed;
			float2 _DistortionUVPanSpeed;
			float _EmissionMultiply;
			float _LUTOffset;
			float _LUTRange;
			float _EdgeMaskMultiply;
			float _EdgeMaskPower;
			float _DistortionMultiply;
			float _DistortionUVSwirl;
			float _Cull1;
			float _NoiseUVSwirl;
			float _LUTSSSmoothness;
			float _LUTPan;
			float _ZWrite1;
			float _ZTest1;
			float _Dst1;
			float _Src1;
			float _OpacitySSSmoothness;
			float _OpacityMultiply;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			sampler2D _NoiseTexture;
			sampler2D _DistortionTexture;


			
			float4 _SelectionID;

			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			PackedVaryings VertexFunction(Attributes input  )
			{
				PackedVaryings output;
				ZERO_INITIALIZE(PackedVaryings, output);

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.ase_texcoord = input.ase_texcoord;
				output.ase_texcoord1 = input.ase_texcoord1;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;

				float3 positionWS = TransformObjectToWorld( input.positionOS.xyz );
				output.positionCS = TransformWorldToHClip(positionWS);
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 vertex : INTERNALTESSPOS;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.vertex = input.positionOS;
				output.normalOS = input.normalOS;
				output.ase_texcoord = input.ase_texcoord;
				output.ase_texcoord1 = input.ase_texcoord1;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].vertex, input[1].vertex, input[2].vertex, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].vertex, input[1].vertex, input[2].vertex, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].vertex, input[1].vertex, input[2].vertex, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].vertex * bary.x + patch[1].vertex * bary.y + patch[2].vertex * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				output.ase_texcoord1 = patch[0].ase_texcoord1 * bary.x + patch[1].ase_texcoord1 * bary.y + patch[2].ase_texcoord1 * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].vertex.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(PackedVaryings input ) : SV_TARGET
			{
				SurfaceDescription surfaceDescription = (SurfaceDescription)0;

				float temp_output_113_0 = ( 1.0 - input.ase_texcoord.z );
				float2 temp_output_73_0_g51 = _NoiseUVPanSpeed;
				float2 texCoord68_g51 = input.ase_texcoord.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_86_0_g51 = ( texCoord68_g51 - float2( 1,1 ) );
				float2 appendResult93_g51 = (float2(frac( ( atan2( (temp_output_86_0_g51).x , (temp_output_86_0_g51).y ) / TWO_PI ) ) , length( temp_output_86_0_g51 )));
				float2 panner59_g51 = ( ( (temp_output_73_0_g51).x * _TimeParameters.x ) * float2( 1,0 ) + appendResult93_g51);
				float2 texCoord103_g51 = input.ase_texcoord.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_104_0_g51 = ( texCoord103_g51 - float2( 1,1 ) );
				float2 appendResult107_g51 = (float2(frac( ( atan2( (temp_output_104_0_g51).x , (temp_output_104_0_g51).y ) / TWO_PI ) ) , length( temp_output_104_0_g51 )));
				float2 panner60_g51 = ( ( _TimeParameters.x * (temp_output_73_0_g51).y ) * float2( 0,1 ) + appendResult93_g51);
				float2 appendResult90_g51 = (float2(( (panner59_g51).x + ( (appendResult107_g51).y * _NoiseUVSwirl ) ) , (panner60_g51).y));
				float randomUV233 = input.ase_texcoord1.z;
				float2 temp_output_73_0_g47 = _DistortionUVPanSpeed;
				float2 texCoord68_g47 = input.ase_texcoord.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_86_0_g47 = ( texCoord68_g47 - float2( 1,1 ) );
				float2 appendResult93_g47 = (float2(frac( ( atan2( (temp_output_86_0_g47).x , (temp_output_86_0_g47).y ) / TWO_PI ) ) , length( temp_output_86_0_g47 )));
				float2 panner59_g47 = ( ( (temp_output_73_0_g47).x * _TimeParameters.x ) * float2( 1,0 ) + appendResult93_g47);
				float2 texCoord103_g47 = input.ase_texcoord.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_104_0_g47 = ( texCoord103_g47 - float2( 1,1 ) );
				float2 appendResult107_g47 = (float2(frac( ( atan2( (temp_output_104_0_g47).x , (temp_output_104_0_g47).y ) / TWO_PI ) ) , length( temp_output_104_0_g47 )));
				float2 panner60_g47 = ( ( _TimeParameters.x * (temp_output_73_0_g47).y ) * float2( 0,1 ) + appendResult93_g47);
				float2 appendResult90_g47 = (float2(( (panner59_g47).x + ( (appendResult107_g47).y * _DistortionUVSwirl ) ) , (panner60_g47).y));
				float2 texCoord27_g50 = input.ase_texcoord.xy * float2( 1,1 ) + float2( 0,0 );
				float2 temp_cast_2 = (1.0).xx;
				float2 temp_output_26_0_g50 = ( texCoord27_g50 - temp_cast_2 );
				float temp_output_13_0_g50 = frac( ( atan2( (temp_output_26_0_g50).x , (temp_output_26_0_g50).y ) / 6.283 ) );
				float2 temp_output_17_0_g50 = ( temp_output_26_0_g50 * temp_output_26_0_g50 );
				float temp_output_12_0_g50 = sqrt( ( (temp_output_17_0_g50).x + (temp_output_17_0_g50).y ) );
				float2 appendResult5_g50 = (float2(temp_output_13_0_g50 , temp_output_12_0_g50));
				float2 texCoord11_g48 = input.ase_texcoord.xy * float2( 1,1 ) + float2( 0,0 );
				float2 temp_cast_3 = (0.0).xx;
				float2 appendResult3_g48 = (float2((appendResult5_g50).x , ( ( distance( max( ( abs( ( texCoord11_g48 + -0.5 ) ) - ( float2( 0.15,0.15 ) * 0.5 ) ) , float2( 0,0 ) ) , temp_cast_3 ) + 0.0 ) * 1.5 )));
				float temp_output_114_0 = saturate( ( tex2D( _NoiseTexture, ( float4( ( ( _NoiseUVScale * appendResult90_g51 ) + randomUV233 ), 0.0 , 0.0 ) + ( tex2D( _DistortionTexture, ( ( _DistortionUVScale * appendResult90_g47 ) + randomUV233 ) ) * _DistortionMultiply ) ).rg ).g * ( pow( (appendResult3_g48).y , _EdgeMaskPower ) * _EdgeMaskMultiply ) ) );
				float smoothstepResult115 = smoothstep( temp_output_113_0 , ( temp_output_113_0 + _OpacitySSSmoothness ) , temp_output_114_0);
				

				surfaceDescription.Alpha = saturate( ( saturate( smoothstepResult115 ) * saturate( ( input.ase_texcoord.z * _OpacityMultiply ) ) ) );
				surfaceDescription.AlphaClipThreshold = 0.5;

				#if _ALPHATEST_ON
					float alphaClipThreshold = 0.01f;
					#if ALPHA_CLIP_THRESHOLD
						alphaClipThreshold = surfaceDescription.AlphaClipThreshold;
					#endif
					clip(surfaceDescription.Alpha - alphaClipThreshold);
				#endif

				half4 outColor = 0;
				outColor = _SelectionID;

				return outColor;
			}

			ENDHLSL
		}

		
		Pass
		{
			
			Name "DepthNormals"
			Tags { "LightMode"="DepthNormalsOnly" }

			ZTest LEqual
			ZWrite On

			HLSLPROGRAM

        	#pragma multi_compile_instancing
        	#define _SURFACE_TYPE_TRANSPARENT 1
        	#define ASE_VERSION 19701
        	#define ASE_SRP_VERSION 170003


        	#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

			#pragma vertex vert
			#pragma fragment frag

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT
			#define VARYINGS_NEED_NORMAL_WS

			#define SHADERPASS SHADERPASS_DEPTHNORMALSONLY

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float4 clipPosV : TEXCOORD0;
				float3 normalWS : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord3 : TEXCOORD3;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float2 _DistortionUVScale;
			float2 _NoiseUVScale;
			float2 _NoiseUVPanSpeed;
			float2 _DistortionUVPanSpeed;
			float _EmissionMultiply;
			float _LUTOffset;
			float _LUTRange;
			float _EdgeMaskMultiply;
			float _EdgeMaskPower;
			float _DistortionMultiply;
			float _DistortionUVSwirl;
			float _Cull1;
			float _NoiseUVSwirl;
			float _LUTSSSmoothness;
			float _LUTPan;
			float _ZWrite1;
			float _ZTest1;
			float _Dst1;
			float _Src1;
			float _OpacitySSSmoothness;
			float _OpacityMultiply;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			sampler2D _NoiseTexture;
			sampler2D _DistortionTexture;


			
			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			PackedVaryings VertexFunction(Attributes input  )
			{
				PackedVaryings output;
				ZERO_INITIALIZE(PackedVaryings, output);

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.ase_texcoord2 = input.ase_texcoord;
				output.ase_texcoord3 = input.ase_texcoord1;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				output.positionCS = vertexInput.positionCS;
				output.clipPosV = vertexInput.positionCS;
				output.normalWS = TransformObjectToWorldNormal( input.normalOS );
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 vertex : INTERNALTESSPOS;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.vertex = input.positionOS;
				output.normalOS = input.normalOS;
				output.ase_texcoord = input.ase_texcoord;
				output.ase_texcoord1 = input.ase_texcoord1;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].vertex, input[1].vertex, input[2].vertex, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].vertex, input[1].vertex, input[2].vertex, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].vertex, input[1].vertex, input[2].vertex, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].vertex * bary.x + patch[1].vertex * bary.y + patch[2].vertex * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				output.ase_texcoord1 = patch[0].ase_texcoord1 * bary.x + patch[1].ase_texcoord1 * bary.y + patch[2].ase_texcoord1 * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].vertex.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			void frag( PackedVaryings input
				, out half4 outNormalWS : SV_Target0
			#ifdef _WRITE_RENDERING_LAYERS
				, out float4 outRenderingLayers : SV_Target1
			#endif
				 )
			{
				float4 ClipPos = input.clipPosV;
				float4 ScreenPos = ComputeScreenPos( input.clipPosV );

				float temp_output_113_0 = ( 1.0 - input.ase_texcoord2.z );
				float2 temp_output_73_0_g51 = _NoiseUVPanSpeed;
				float2 texCoord68_g51 = input.ase_texcoord2.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_86_0_g51 = ( texCoord68_g51 - float2( 1,1 ) );
				float2 appendResult93_g51 = (float2(frac( ( atan2( (temp_output_86_0_g51).x , (temp_output_86_0_g51).y ) / TWO_PI ) ) , length( temp_output_86_0_g51 )));
				float2 panner59_g51 = ( ( (temp_output_73_0_g51).x * _TimeParameters.x ) * float2( 1,0 ) + appendResult93_g51);
				float2 texCoord103_g51 = input.ase_texcoord2.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_104_0_g51 = ( texCoord103_g51 - float2( 1,1 ) );
				float2 appendResult107_g51 = (float2(frac( ( atan2( (temp_output_104_0_g51).x , (temp_output_104_0_g51).y ) / TWO_PI ) ) , length( temp_output_104_0_g51 )));
				float2 panner60_g51 = ( ( _TimeParameters.x * (temp_output_73_0_g51).y ) * float2( 0,1 ) + appendResult93_g51);
				float2 appendResult90_g51 = (float2(( (panner59_g51).x + ( (appendResult107_g51).y * _NoiseUVSwirl ) ) , (panner60_g51).y));
				float randomUV233 = input.ase_texcoord3.z;
				float2 temp_output_73_0_g47 = _DistortionUVPanSpeed;
				float2 texCoord68_g47 = input.ase_texcoord2.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_86_0_g47 = ( texCoord68_g47 - float2( 1,1 ) );
				float2 appendResult93_g47 = (float2(frac( ( atan2( (temp_output_86_0_g47).x , (temp_output_86_0_g47).y ) / TWO_PI ) ) , length( temp_output_86_0_g47 )));
				float2 panner59_g47 = ( ( (temp_output_73_0_g47).x * _TimeParameters.x ) * float2( 1,0 ) + appendResult93_g47);
				float2 texCoord103_g47 = input.ase_texcoord2.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_104_0_g47 = ( texCoord103_g47 - float2( 1,1 ) );
				float2 appendResult107_g47 = (float2(frac( ( atan2( (temp_output_104_0_g47).x , (temp_output_104_0_g47).y ) / TWO_PI ) ) , length( temp_output_104_0_g47 )));
				float2 panner60_g47 = ( ( _TimeParameters.x * (temp_output_73_0_g47).y ) * float2( 0,1 ) + appendResult93_g47);
				float2 appendResult90_g47 = (float2(( (panner59_g47).x + ( (appendResult107_g47).y * _DistortionUVSwirl ) ) , (panner60_g47).y));
				float2 texCoord27_g50 = input.ase_texcoord2.xy * float2( 1,1 ) + float2( 0,0 );
				float2 temp_cast_2 = (1.0).xx;
				float2 temp_output_26_0_g50 = ( texCoord27_g50 - temp_cast_2 );
				float temp_output_13_0_g50 = frac( ( atan2( (temp_output_26_0_g50).x , (temp_output_26_0_g50).y ) / 6.283 ) );
				float2 temp_output_17_0_g50 = ( temp_output_26_0_g50 * temp_output_26_0_g50 );
				float temp_output_12_0_g50 = sqrt( ( (temp_output_17_0_g50).x + (temp_output_17_0_g50).y ) );
				float2 appendResult5_g50 = (float2(temp_output_13_0_g50 , temp_output_12_0_g50));
				float2 texCoord11_g48 = input.ase_texcoord2.xy * float2( 1,1 ) + float2( 0,0 );
				float2 temp_cast_3 = (0.0).xx;
				float2 appendResult3_g48 = (float2((appendResult5_g50).x , ( ( distance( max( ( abs( ( texCoord11_g48 + -0.5 ) ) - ( float2( 0.15,0.15 ) * 0.5 ) ) , float2( 0,0 ) ) , temp_cast_3 ) + 0.0 ) * 1.5 )));
				float temp_output_114_0 = saturate( ( tex2D( _NoiseTexture, ( float4( ( ( _NoiseUVScale * appendResult90_g51 ) + randomUV233 ), 0.0 , 0.0 ) + ( tex2D( _DistortionTexture, ( ( _DistortionUVScale * appendResult90_g47 ) + randomUV233 ) ) * _DistortionMultiply ) ).rg ).g * ( pow( (appendResult3_g48).y , _EdgeMaskPower ) * _EdgeMaskMultiply ) ) );
				float smoothstepResult115 = smoothstep( temp_output_113_0 , ( temp_output_113_0 + _OpacitySSSmoothness ) , temp_output_114_0);
				

				float Alpha = saturate( ( saturate( smoothstepResult115 ) * saturate( ( input.ase_texcoord2.z * _OpacityMultiply ) ) ) );
				float AlphaClipThreshold = 0.5;

				#if _ALPHATEST_ON
					clip( Alpha - AlphaClipThreshold );
				#endif

				#ifdef LOD_FADE_CROSSFADE
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined(_GBUFFER_NORMALS_OCT)
					float3 normalWS = normalize(input.normalWS);
					float2 octNormalWS = PackNormalOctQuadEncode(normalWS);           // values between [-1, +1], must use fp32 on some platforms
					float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);   // values between [ 0,  1]
					half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);      // values between [ 0,  1]
					outNormalWS = half4(packedNormalWS, 0.0);
				#else
					float3 normalWS = input.normalWS;
					outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
				#endif

				#ifdef _WRITE_RENDERING_LAYERS
					uint renderingLayers = GetMeshRenderingLayer();
					outRenderingLayers = float4(EncodeMeshRenderingLayer(renderingLayers), 0, 0, 0);
				#endif
			}

			ENDHLSL
		}

		
		Pass
		{
			
			Name "MotionVectors"
			Tags { "LightMode"="MotionVectors" }

			ColorMask RG

			HLSLPROGRAM

			#pragma multi_compile_instancing
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define ASE_VERSION 19701
			#define ASE_SRP_VERSION 170003


			#pragma vertex vert
			#pragma fragment frag

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
		    #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
		    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
		    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
		    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
				#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
			#endif

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"

			

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 positionOld : TEXCOORD4;
				#if _ADD_PRECOMPUTED_VELOCITY
					float3 alembicMotionVector : TEXCOORD5;
				#endif
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float4 positionCSNoJitter : TEXCOORD0;
				float4 previousPositionCSNoJitter : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord3 : TEXCOORD3;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float2 _DistortionUVScale;
			float2 _NoiseUVScale;
			float2 _NoiseUVPanSpeed;
			float2 _DistortionUVPanSpeed;
			float _EmissionMultiply;
			float _LUTOffset;
			float _LUTRange;
			float _EdgeMaskMultiply;
			float _EdgeMaskPower;
			float _DistortionMultiply;
			float _DistortionUVSwirl;
			float _Cull1;
			float _NoiseUVSwirl;
			float _LUTSSSmoothness;
			float _LUTPan;
			float _ZWrite1;
			float _ZTest1;
			float _Dst1;
			float _Src1;
			float _OpacitySSSmoothness;
			float _OpacityMultiply;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			sampler2D _NoiseTexture;
			sampler2D _DistortionTexture;


			
			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.ase_texcoord2 = input.ase_texcoord;
				output.ase_texcoord3 = input.ase_texcoord1;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				// Jittered. Match the frame.
				output.positionCS = vertexInput.positionCS;
				output.positionCSNoJitter = mul( _NonJitteredViewProjMatrix, mul( UNITY_MATRIX_M, input.positionOS ) );

				float4 prevPos = ( unity_MotionVectorsParams.x == 1 ) ? float4( input.positionOld, 1 ) : input.positionOS;

				#if _ADD_PRECOMPUTED_VELOCITY
					prevPos = prevPos - float4(input.alembicMotionVector, 0);
				#endif

				output.previousPositionCSNoJitter = mul( _PrevViewProjMatrix, mul( UNITY_PREV_MATRIX_M, prevPos ) );

				return output;
			}

			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}

			half4 frag(	PackedVaryings input  ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				float temp_output_113_0 = ( 1.0 - input.ase_texcoord2.z );
				float2 temp_output_73_0_g51 = _NoiseUVPanSpeed;
				float2 texCoord68_g51 = input.ase_texcoord2.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_86_0_g51 = ( texCoord68_g51 - float2( 1,1 ) );
				float2 appendResult93_g51 = (float2(frac( ( atan2( (temp_output_86_0_g51).x , (temp_output_86_0_g51).y ) / TWO_PI ) ) , length( temp_output_86_0_g51 )));
				float2 panner59_g51 = ( ( (temp_output_73_0_g51).x * _TimeParameters.x ) * float2( 1,0 ) + appendResult93_g51);
				float2 texCoord103_g51 = input.ase_texcoord2.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_104_0_g51 = ( texCoord103_g51 - float2( 1,1 ) );
				float2 appendResult107_g51 = (float2(frac( ( atan2( (temp_output_104_0_g51).x , (temp_output_104_0_g51).y ) / TWO_PI ) ) , length( temp_output_104_0_g51 )));
				float2 panner60_g51 = ( ( _TimeParameters.x * (temp_output_73_0_g51).y ) * float2( 0,1 ) + appendResult93_g51);
				float2 appendResult90_g51 = (float2(( (panner59_g51).x + ( (appendResult107_g51).y * _NoiseUVSwirl ) ) , (panner60_g51).y));
				float randomUV233 = input.ase_texcoord3.z;
				float2 temp_output_73_0_g47 = _DistortionUVPanSpeed;
				float2 texCoord68_g47 = input.ase_texcoord2.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_86_0_g47 = ( texCoord68_g47 - float2( 1,1 ) );
				float2 appendResult93_g47 = (float2(frac( ( atan2( (temp_output_86_0_g47).x , (temp_output_86_0_g47).y ) / TWO_PI ) ) , length( temp_output_86_0_g47 )));
				float2 panner59_g47 = ( ( (temp_output_73_0_g47).x * _TimeParameters.x ) * float2( 1,0 ) + appendResult93_g47);
				float2 texCoord103_g47 = input.ase_texcoord2.xy * float2( 2,2 ) + float2( 0,0 );
				float2 temp_output_104_0_g47 = ( texCoord103_g47 - float2( 1,1 ) );
				float2 appendResult107_g47 = (float2(frac( ( atan2( (temp_output_104_0_g47).x , (temp_output_104_0_g47).y ) / TWO_PI ) ) , length( temp_output_104_0_g47 )));
				float2 panner60_g47 = ( ( _TimeParameters.x * (temp_output_73_0_g47).y ) * float2( 0,1 ) + appendResult93_g47);
				float2 appendResult90_g47 = (float2(( (panner59_g47).x + ( (appendResult107_g47).y * _DistortionUVSwirl ) ) , (panner60_g47).y));
				float2 texCoord27_g50 = input.ase_texcoord2.xy * float2( 1,1 ) + float2( 0,0 );
				float2 temp_cast_2 = (1.0).xx;
				float2 temp_output_26_0_g50 = ( texCoord27_g50 - temp_cast_2 );
				float temp_output_13_0_g50 = frac( ( atan2( (temp_output_26_0_g50).x , (temp_output_26_0_g50).y ) / 6.283 ) );
				float2 temp_output_17_0_g50 = ( temp_output_26_0_g50 * temp_output_26_0_g50 );
				float temp_output_12_0_g50 = sqrt( ( (temp_output_17_0_g50).x + (temp_output_17_0_g50).y ) );
				float2 appendResult5_g50 = (float2(temp_output_13_0_g50 , temp_output_12_0_g50));
				float2 texCoord11_g48 = input.ase_texcoord2.xy * float2( 1,1 ) + float2( 0,0 );
				float2 temp_cast_3 = (0.0).xx;
				float2 appendResult3_g48 = (float2((appendResult5_g50).x , ( ( distance( max( ( abs( ( texCoord11_g48 + -0.5 ) ) - ( float2( 0.15,0.15 ) * 0.5 ) ) , float2( 0,0 ) ) , temp_cast_3 ) + 0.0 ) * 1.5 )));
				float temp_output_114_0 = saturate( ( tex2D( _NoiseTexture, ( float4( ( ( _NoiseUVScale * appendResult90_g51 ) + randomUV233 ), 0.0 , 0.0 ) + ( tex2D( _DistortionTexture, ( ( _DistortionUVScale * appendResult90_g47 ) + randomUV233 ) ) * _DistortionMultiply ) ).rg ).g * ( pow( (appendResult3_g48).y , _EdgeMaskPower ) * _EdgeMaskMultiply ) ) );
				float smoothstepResult115 = smoothstep( temp_output_113_0 , ( temp_output_113_0 + _OpacitySSSmoothness ) , temp_output_114_0);
				

				float Alpha = saturate( ( saturate( smoothstepResult115 ) * saturate( ( input.ase_texcoord2.z * _OpacityMultiply ) ) ) );
				float AlphaClipThreshold = 0.5;

				#ifdef _ALPHATEST_ON
					clip(Alpha - AlphaClipThreshold);
				#endif

				#ifdef LOD_FADE_CROSSFADE
					LODFadeCrossFade( input.positionCS );
				#endif

				return float4( CalcNdcMotionVectorFromCsPositions( input.positionCSNoJitter, input.previousPositionCSNoJitter ), 0, 0 );
			}
			ENDHLSL
		}
		
	}
	
	CustomEditor "UnityEditor.ShaderGraphUnlitGUI"
	FallBack "Hidden/Shader Graph/FallbackError"
	
	Fallback Off
}
/*ASEBEGIN
Version=19701
Node;AmplifyShaderEditor.CommentaryNode;232;-6112,64;Inherit;False;548;257.3334;Random UV;2;234;233;Random UV;0,0,0,1;0;0
Node;AmplifyShaderEditor.TexCoordVertexDataNode;234;-6064,112;Inherit;False;1;4;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.Vector2Node;196;-5120,-256;Inherit;False;Property;_DistortionUVScale;Distortion UV Scale;15;0;Create;True;0;0;0;False;0;False;2,0.1;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.Vector2Node;203;-5120,-128;Inherit;False;Property;_DistortionUVPanSpeed;Distortion UV Pan Speed;16;0;Create;True;0;0;0;False;0;False;0.1,1;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.RangedFloatNode;222;-5120,-384;Inherit;False;Property;_DistortionUVSwirl;Distortion UV Swirl;17;0;Create;True;0;0;0;False;0;False;0.3;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;233;-5808,112;Inherit;False;randomUV;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode;219;-4480,-384;Inherit;False;SH_F_Vefects_UV_Radial_Distortion;-1;;47;a5b4adfbe5eabfa4f9c38d170d3dddde;0;4;120;FLOAT;0;False;98;FLOAT2;1,1;False;73;FLOAT2;1,1;False;66;FLOAT2;0,0;False;1;FLOAT2;100
Node;AmplifyShaderEditor.GetLocalVarNode;236;-4165.348,-242.7658;Inherit;False;233;randomUV;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;130;-3072,640;Inherit;False;Constant;_Float6;Float 6;11;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TexturePropertyNode;178;-5120,-640;Inherit;True;Property;_DistortionTexture;Distortion Texture;14;0;Create;True;0;0;0;False;3;Space(33);Header(Distortion);Space(13);False;96dcce1503826c54480ca5fa7f3190a2;96dcce1503826c54480ca5fa7f3190a2;False;white;Auto;Texture2D;-1;0;2;SAMPLER2D;0;SAMPLERSTATE;1
Node;AmplifyShaderEditor.Vector2Node;201;-5120,384;Inherit;False;Property;_NoiseUVScale;Noise UV Scale;18;0;Create;True;0;0;0;False;0;False;1,0.1;1,1;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.Vector2Node;200;-5120,512;Inherit;False;Property;_NoiseUVPanSpeed;Noise UV Pan Speed;19;0;Create;True;0;0;0;False;0;False;0.1,1.5;0.2,0.5;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.RangedFloatNode;221;-5120,256;Inherit;False;Property;_NoiseUVSwirl;Noise UV Swirl;20;0;Create;True;0;0;0;False;0;False;0.3;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;235;-4160,-368;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.FunctionNode;126;-2816,640;Inherit;False;SH_F_Vefects_VFX_SDF_Box_UV;1;;48;7b24bf4a1857f98439a6a786a080fdc7;0;1;7;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SamplerNode;205;-4480,-640;Inherit;True;Property;_TextureSample1;Texture Sample 1;26;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Instance;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RangedFloatNode;202;-3968,-768;Inherit;False;Property;_DistortionMultiply;Distortion Multiply;21;0;Create;True;0;0;0;False;0;False;0.1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode;220;-4480,256;Inherit;False;SH_F_Vefects_UV_Radial_Distortion;-1;;51;a5b4adfbe5eabfa4f9c38d170d3dddde;0;4;120;FLOAT;0;False;98;FLOAT2;1,1;False;73;FLOAT2;1,1;False;66;FLOAT2;0,0;False;1;FLOAT2;100
Node;AmplifyShaderEditor.GetLocalVarNode;238;-4132.788,389.2029;Inherit;False;233;randomUV;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;135;-2176,768;Inherit;False;Property;_EdgeMaskPower;Edge Mask Power;10;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ComponentMaskNode;133;-2432,640;Inherit;False;False;True;False;False;1;0;FLOAT2;0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;208;-3968,-640;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleAddOpNode;237;-4160,256;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.PowerNode;131;-2176,640;Inherit;False;False;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;136;-1920,768;Inherit;False;Property;_EdgeMaskMultiply;Edge Mask Multiply;11;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;207;-3968,256;Inherit;False;2;2;0;FLOAT2;0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.TexCoordVertexDataNode;110;-2048,-128;Inherit;False;0;4;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;132;-1920,640;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;124;-3712,256;Inherit;True;Property;_NoiseTexture;Noise Texture;9;0;Create;True;0;0;0;False;3;Space(33);Header(Noise);Space(13);False;-1;f189a42c3a428034691438da050dceed;f189a42c3a428034691438da050dceed;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;125;-1792,256;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;113;-1536,-128;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;140;-1280,-256;Inherit;False;Property;_OpacitySSSmoothness;Opacity SS Smoothness;13;0;Create;True;0;0;0;False;0;False;0.25;0.25;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;119;-1152,384;Inherit;False;Property;_OpacityMultiply;Opacity Multiply;12;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;114;-1536,256;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;122;-1280,-384;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.25;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;118;-1152,256;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SmoothstepOpNode;115;-1280,-512;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;120;-896,256;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;121;-896,-512;Inherit;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;223;-1842,-306;Inherit;False;228;162.6667;Debug;1;137;;0,0,0,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;49;459.26,31.68577;Inherit;False;1238;166;Auto Register Variables;5;54;53;52;51;50;Lush was here! <3;0.4872068,0.2971698,1,1;0;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;116;-640,256;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;50;507.2599,79.68589;Inherit;False;Property;_Cull1;Cull;22;0;Create;True;0;0;0;True;3;Space(13);Header(AR);Space(13);False;2;2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;51;763.2599,79.68589;Inherit;False;Property;_Src1;Src;23;0;Create;True;0;0;0;True;0;False;5;5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;52;1019.26,79.68589;Inherit;False;Property;_Dst1;Dst;24;0;Create;True;0;0;0;True;0;False;10;10;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;53;1531.26,79.68589;Inherit;False;Property;_ZTest1;ZTest;26;0;Create;True;0;0;0;True;0;False;2;2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;54;1275.26,79.68589;Inherit;False;Property;_ZWrite1;ZWrite;25;0;Create;True;0;0;0;True;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SmoothstepOpNode;111;-1280,-128;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;117;-384,256;Inherit;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;112;-1280,0;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;123;-896,-128;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;139;-1280,128;Inherit;False;Property;_LUTSSSmoothness;LUT SS Smoothness;5;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;137;-1792,-256;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PannerNode;224;-1040,-1152;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;225;-1040,-1024;Inherit;False;Property;_LUTPan;LUT Pan;7;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;226;-1296,-1152;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;227;-1296,-1024;Inherit;False;Property;_LUTOffset;LUT Offset;8;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;228;-1552,-1024;Inherit;False;Property;_LUTRange;LUT Range;6;0;Create;True;0;0;0;False;0;False;1;3;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;229;-1552,-1152;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;109;-768,-1152;Inherit;True;Property;_LUT;LUT;4;0;Create;True;0;0;0;False;3;Space(33);Header(LUT);Space(13);False;-1;0d040e115ef994843b838eeb0f1094c3;0d040e115ef994843b838eeb0f1094c3;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;230;-384,-896;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;231;-512,-768;Inherit;False;Property;_EmissionMultiply;Emission Multiply;0;0;Create;True;0;0;0;False;0;False;3;3;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;0;0,0;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ExtraPrePass;0;0;ExtraPrePass;5;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;True;1;1;False;;0;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;True;0;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1;0,0;Float;False;True;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;13;Vefects/SH_Vefects_URP_VFX_Vignette_Fear_01;2992e84f91cbeb14eab234972e07ea9d;True;Forward;0;1;Forward;8;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;True;True;0;True;_Cull1;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;True;True;2;5;True;_Src1;10;True;_Dst1;1;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;2;False;_ZWrite1;True;3;True;_ZTest1;True;True;0;False;;0;False;;True;1;LightMode=UniversalForwardOnly;False;False;0;;0;0;Standard;25;Surface;1;638255503152939265;  Blend;0;0;Two Sided;1;0;Forward Only;0;0;Alpha Clipping;0;638861335214108721;  Use Shadow Threshold;0;0;Cast Shadows;0;638500290705344932;Receive Shadows;1;0;Motion Vectors;1;0;  Add Precomputed Velocity;0;0;GPU Instancing;1;0;LOD CrossFade;0;0;Built-in Fog;0;0;Meta Pass;0;0;Extra Pre Pass;0;0;Tessellation;0;0;  Phong;0;0;  Strength;0.5,False,;0;  Type;0;0;  Tess;16,False,;0;  Min;10,False,;0;  Max;25,False,;0;  Edge Length;16,False,;0;  Max Displacement;25,False,;0;Vertex Position,InvertActionOnDeselection;1;0;0;11;False;True;False;True;False;False;True;True;True;False;True;False;;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;2;0,0;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ShadowCaster;0;2;ShadowCaster;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;True;False;False;False;False;0;False;;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;True;1;LightMode=ShadowCaster;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;3;0,0;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthOnly;0;3;DepthOnly;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;True;False;False;False;False;0;False;;False;False;False;False;False;False;False;False;False;True;1;False;;False;False;True;1;LightMode=DepthOnly;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;4;0,0;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;Meta;0;4;Meta;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Meta;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;5;0,0;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;Universal2D;0;5;Universal2D;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;True;1;1;False;;0;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;True;1;LightMode=Universal2D;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;6;0,0;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;SceneSelectionPass;0;6;SceneSelectionPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=SceneSelectionPass;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;7;0,0;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ScenePickingPass;0;7;ScenePickingPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Picking;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;8;0,0;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthNormals;0;8;DepthNormals;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;True;1;LightMode=DepthNormalsOnly;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;9;0,0;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthNormalsOnly;0;9;DepthNormalsOnly;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;True;1;LightMode=DepthNormalsOnly;False;True;9;d3d11;metal;vulkan;xboxone;xboxseries;playstation;ps4;ps5;switch;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;239;0,100;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;MotionVectors;0;10;MotionVectors;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;False;False;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=MotionVectors;False;False;0;;0;0;Standard;0;False;0
WireConnection;233;0;234;3
WireConnection;219;120;222;0
WireConnection;219;98;196;0
WireConnection;219;73;203;0
WireConnection;235;0;219;100
WireConnection;235;1;236;0
WireConnection;126;7;130;0
WireConnection;205;0;178;0
WireConnection;205;1;235;0
WireConnection;220;120;221;0
WireConnection;220;98;201;0
WireConnection;220;73;200;0
WireConnection;133;0;126;0
WireConnection;208;0;205;0
WireConnection;208;1;202;0
WireConnection;237;0;220;100
WireConnection;237;1;238;0
WireConnection;131;0;133;0
WireConnection;131;1;135;0
WireConnection;207;0;237;0
WireConnection;207;1;208;0
WireConnection;132;0;131;0
WireConnection;132;1;136;0
WireConnection;124;1;207;0
WireConnection;125;0;124;2
WireConnection;125;1;132;0
WireConnection;113;0;110;3
WireConnection;114;0;125;0
WireConnection;122;0;113;0
WireConnection;122;1;140;0
WireConnection;118;0;110;3
WireConnection;118;1;119;0
WireConnection;115;0;114;0
WireConnection;115;1;113;0
WireConnection;115;2;122;0
WireConnection;120;0;118;0
WireConnection;121;0;115;0
WireConnection;116;0;121;0
WireConnection;116;1;120;0
WireConnection;111;0;114;0
WireConnection;111;1;113;0
WireConnection;111;2;112;0
WireConnection;117;0;116;0
WireConnection;112;0;113;0
WireConnection;112;1;139;0
WireConnection;123;0;111;0
WireConnection;137;0;110;3
WireConnection;224;0;226;0
WireConnection;224;2;225;0
WireConnection;226;0;229;0
WireConnection;226;1;227;0
WireConnection;229;0;123;0
WireConnection;229;1;228;0
WireConnection;109;1;224;0
WireConnection;230;0;109;0
WireConnection;230;1;231;0
WireConnection;1;2;230;0
WireConnection;1;3;117;0
ASEEND*/
//CHKSM=FE1D777411620010BD9423BE3E9E0B4EC10E809D