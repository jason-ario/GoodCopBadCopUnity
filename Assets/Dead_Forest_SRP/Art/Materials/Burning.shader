// Made with Amplify Shader Editor
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Burning"
{
	Properties
	{
		_Texture0("Texture 0", 2D) = "white" {}
		_DistortionMap("DistortionMap", 2D) = "bump" {}
		_DistortionAmount("DistortionAmount", Range( 0 , 1)) = 0.0871507
		_ScrollSpeed("ScrollSpeed", Range( 0 , 1)) = 0.3764706
		_Warm("Warm", Color) = (0.945098,0.3361675,0.01176471,0)
		_Hot("Hot", Color) = (0.9811321,0.9760582,0.0786757,0)
		_Burn("Burn", Range( 0 , 1.5)) = 0.1039447
		_Albedo("Albedo", 2D) = "white" {}
		_Normal("Normal", 2D) = "bump" {}
		_Metallic("Metallic", 2D) = "white" {}
		_AO("AO", 2D) = "white" {}
		_DissolveAmount("DissolveAmount", Range( 0 , 1)) = 0
		_HeatWave("HeatWave", Range( 0 , 1)) = 0.01
		_WiggleAmount("WiggleAmount", Range( 0 , 1)) = 0
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent"  "Queue" = "Geometry+0" "IsEmissive" = "true"  }
		Cull Back
		AlphaToMask On
		CGINCLUDE
		#include "UnityShaderVariables.cginc"
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"
		#pragma target 3.0
		struct Input
		{
			float3 worldPos;
			float2 uv_texcoord;
		};

		uniform sampler2D _DistortionMap;
		uniform float _ScrollSpeed;
		uniform sampler2D _Texture0;
		uniform float _HeatWave;
		uniform float _Burn;
		uniform float _WiggleAmount;
		uniform sampler2D _Normal;
		uniform float4 _Normal_ST;
		uniform sampler2D _Albedo;
		uniform float4 _Albedo_ST;
		uniform float4 _Warm;
		uniform float4 _Hot;
		uniform float4 _DistortionMap_ST;
		uniform float _DistortionAmount;
		uniform float _DissolveAmount;
		uniform sampler2D _Metallic;
		uniform float4 _Metallic_ST;
		uniform sampler2D _AO;
		uniform float4 _AO_ST;

		void vertexDataFunc( inout appdata_full v, out Input o )
		{
			UNITY_INITIALIZE_OUTPUT( Input, o );
			float3 ase_worldPos = mul( unity_ObjectToWorld, v.vertex );
			float3 ase_vertex3Pos = v.vertex.xyz;
			float temp_output_12_0 = ( _Time.y * _ScrollSpeed );
			float2 panner35 = ( temp_output_12_0 * float2( 0,-1 ) + v.texcoord.xy);
			float3 tex2DNode34 = UnpackNormal( tex2Dlod( _DistortionMap, float4( panner35, 0, 0.0) ) );
			float4 tex2DNode21 = tex2Dlod( _Texture0, float4( ( ( (tex2DNode34).xy * _HeatWave ) + v.texcoord.xy ), 0, 0.0) );
			float temp_output_22_0 = step( tex2DNode21.r , _Burn );
			v.vertex.xyz += ( ( ( ase_worldPos * ase_vertex3Pos ) * tex2DNode34 * temp_output_22_0 ) * _WiggleAmount );
			v.vertex.w = 1;
		}

		void surf( Input i , inout SurfaceOutputStandard o )
		{
			float2 uv_Normal = i.uv_texcoord * _Normal_ST.xy + _Normal_ST.zw;
			o.Normal = UnpackNormal( tex2D( _Normal, uv_Normal ) );
			float2 uv_Albedo = i.uv_texcoord * _Albedo_ST.xy + _Albedo_ST.zw;
			o.Albedo = tex2D( _Albedo, uv_Albedo ).rgb;
			float2 uv_DistortionMap = i.uv_texcoord * _DistortionMap_ST.xy + _DistortionMap_ST.zw;
			float temp_output_12_0 = ( _Time.y * _ScrollSpeed );
			float2 panner10 = ( temp_output_12_0 * float2( 0,-1 ) + float2( 0,0 ));
			float2 uv_TexCoord8 = i.uv_texcoord + panner10;
			float4 lerpResult16 = lerp( _Warm , _Hot , tex2D( _Texture0, ( ( (UnpackNormal( tex2D( _DistortionMap, uv_DistortionMap ) )).xyz * _DistortionAmount ) + float3( uv_TexCoord8 ,  0.0 ) ).xy ).g);
			float4 temp_cast_3 = (2.0).xxxx;
			float2 panner35 = ( temp_output_12_0 * float2( 0,-1 ) + i.uv_texcoord);
			float3 tex2DNode34 = UnpackNormal( tex2D( _DistortionMap, panner35 ) );
			float4 tex2DNode21 = tex2D( _Texture0, ( ( (tex2DNode34).xy * _HeatWave ) + i.uv_texcoord ) );
			float temp_output_22_0 = step( tex2DNode21.r , _Burn );
			float temp_output_45_0 = step( tex2DNode21.r , ( 1.0 - ( _DissolveAmount / 1.5 ) ) );
			float temp_output_47_0 = ( temp_output_45_0 - step( tex2DNode21.r , ( 1.0 - _DissolveAmount ) ) );
			float4 temp_cast_4 = (temp_output_47_0).xxxx;
			float4 temp_cast_5 = (temp_output_47_0).xxxx;
			o.Emission = ( ( ( ( pow( lerpResult16 , temp_cast_3 ) * 2.0 ) * ( temp_output_22_0 + ( temp_output_22_0 - step( tex2DNode21.r , ( _Burn / 1.5 ) ) ) ) ) - temp_cast_4 ) - temp_cast_5 ).rgb;
			float2 uv_Metallic = i.uv_texcoord * _Metallic_ST.xy + _Metallic_ST.zw;
			o.Metallic = tex2D( _Metallic, uv_Metallic ).r;
			float2 uv_AO = i.uv_texcoord * _AO_ST.xy + _AO_ST.zw;
			o.Occlusion = tex2D( _AO, uv_AO ).r;
			o.Alpha = temp_output_45_0;
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf Standard keepalpha fullforwardshadows vertex:vertexDataFunc 

		ENDCG
		Pass
		{
			Name "ShadowCaster"
			Tags{ "LightMode" = "ShadowCaster" }
			ZWrite On
			AlphaToMask Off
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile_shadowcaster
			#pragma multi_compile UNITY_PASS_SHADOWCASTER
			#pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2
			#include "HLSLSupport.cginc"
			#if ( SHADER_API_D3D11 || SHADER_API_GLCORE || SHADER_API_GLES || SHADER_API_GLES3 || SHADER_API_METAL || SHADER_API_VULKAN )
				#define CAN_SKIP_VPOS
			#endif
			#include "UnityCG.cginc"
			#include "Lighting.cginc"
			#include "UnityPBSLighting.cginc"
			sampler3D _DitherMaskLOD;
			struct v2f
			{
				V2F_SHADOW_CASTER;
				float2 customPack1 : TEXCOORD1;
				float3 worldPos : TEXCOORD2;
				float4 tSpace0 : TEXCOORD3;
				float4 tSpace1 : TEXCOORD4;
				float4 tSpace2 : TEXCOORD5;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};
			v2f vert( appdata_full v )
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID( v );
				UNITY_INITIALIZE_OUTPUT( v2f, o );
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( o );
				UNITY_TRANSFER_INSTANCE_ID( v, o );
				Input customInputData;
				vertexDataFunc( v, customInputData );
				float3 worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;
				half3 worldNormal = UnityObjectToWorldNormal( v.normal );
				half3 worldTangent = UnityObjectToWorldDir( v.tangent.xyz );
				half tangentSign = v.tangent.w * unity_WorldTransformParams.w;
				half3 worldBinormal = cross( worldNormal, worldTangent ) * tangentSign;
				o.tSpace0 = float4( worldTangent.x, worldBinormal.x, worldNormal.x, worldPos.x );
				o.tSpace1 = float4( worldTangent.y, worldBinormal.y, worldNormal.y, worldPos.y );
				o.tSpace2 = float4( worldTangent.z, worldBinormal.z, worldNormal.z, worldPos.z );
				o.customPack1.xy = customInputData.uv_texcoord;
				o.customPack1.xy = v.texcoord;
				o.worldPos = worldPos;
				TRANSFER_SHADOW_CASTER_NORMALOFFSET( o )
				return o;
			}
			half4 frag( v2f IN
			#if !defined( CAN_SKIP_VPOS )
			, UNITY_VPOS_TYPE vpos : VPOS
			#endif
			) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( IN );
				Input surfIN;
				UNITY_INITIALIZE_OUTPUT( Input, surfIN );
				surfIN.uv_texcoord = IN.customPack1.xy;
				float3 worldPos = IN.worldPos;
				half3 worldViewDir = normalize( UnityWorldSpaceViewDir( worldPos ) );
				surfIN.worldPos = worldPos;
				SurfaceOutputStandard o;
				UNITY_INITIALIZE_OUTPUT( SurfaceOutputStandard, o )
				surf( surfIN, o );
				#if defined( CAN_SKIP_VPOS )
				float2 vpos = IN.pos;
				#endif
				half alphaRef = tex3D( _DitherMaskLOD, float3( vpos.xy * 0.25, o.Alpha * 0.9375 ) ).a;
				clip( alphaRef - 0.01 );
				SHADOW_CASTER_FRAGMENT( IN )
			}
			ENDCG
		}
	}
	Fallback "Diffuse"
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=18500
268;73;1922;650;2806.281;-465.2947;3.321663;True;False
Node;AmplifyShaderEditor.RangedFloatNode;13;-3710.136,1059.809;Inherit;False;Property;_ScrollSpeed;ScrollSpeed;4;0;Create;True;0;0;False;0;False;0.3764706;0.04;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode;11;-3835.908,754.666;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;36;-3108.886,1150.384;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;12;-3405.465,755.5518;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TexturePropertyNode;3;-3737.18,-21.14476;Inherit;True;Property;_DistortionMap;DistortionMap;2;0;Create;True;0;0;False;0;False;a307a669be9dd024ebb5d97d88b3e9a7;a307a669be9dd024ebb5d97d88b3e9a7;True;bump;Auto;Texture2D;-1;0;2;SAMPLER2D;0;SAMPLERSTATE;1
Node;AmplifyShaderEditor.PannerNode;35;-2740.885,1150.384;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,-1;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SamplerNode;4;-2171.339,-7.343224;Inherit;True;Property;_TextureSample1;Texture Sample 1;3;0;Create;True;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;True;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;34;-2427.542,918.6224;Inherit;True;Property;_TextureSample3;Texture Sample 3;12;0;Create;True;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;True;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ComponentMaskNode;37;-2034.631,918.6893;Inherit;False;True;True;False;True;1;0;FLOAT3;0,0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.PannerNode;10;-2018.312,611.5991;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,-1;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;39;-1926.912,1155.382;Inherit;False;Property;_HeatWave;HeatWave;13;0;Create;True;0;0;False;0;False;0.01;0.01;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;7;-2048.735,308.403;Inherit;False;Property;_DistortionAmount;DistortionAmount;3;0;Create;True;0;0;False;0;False;0.0871507;0.068;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.ComponentMaskNode;5;-1787.452,-5.669985;Inherit;False;True;True;True;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;8;-1507.474,567.2277;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;6;-1514.54,289.4149;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;41;-1556.968,1238.305;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;38;-1617.246,918.3873;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode;42;-1296.112,913.4062;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;23;-931.0435,912.8734;Inherit;False;Property;_Burn;Burn;7;0;Create;True;0;0;False;0;False;0.1039447;0.08;0;1.5;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;9;-1150.462,411.4266;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT2;0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RangedFloatNode;31;-864.3381,1180.705;Inherit;False;Constant;_DivideAmount;DivideAmount;12;0;Create;True;0;0;False;0;False;1.5;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TexturePropertyNode;1;-1313.2,33.59996;Inherit;True;Property;_Texture0;Texture 0;1;0;Create;True;0;0;False;0;False;78962b60e022a1d4f8e724429484d3ed;78962b60e022a1d4f8e724429484d3ed;False;white;Auto;Texture2D;-1;0;2;SAMPLER2D;0;SAMPLERSTATE;1
Node;AmplifyShaderEditor.SamplerNode;21;-933.2779,677.0133;Inherit;True;Property;_TextureSample2;Texture Sample 2;7;0;Create;True;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleDivideOpNode;30;-519.3381,999.7046;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;51;-1170.639,1637.875;Inherit;False;Property;_DissolveAmount;DissolveAmount;12;0;Create;True;0;0;False;0;False;0;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;2;-928.4998,34.30001;Inherit;True;Property;_TextureSample0;Texture Sample 0;2;0;Create;True;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ColorNode;15;-894.6391,-299.6091;Inherit;False;Property;_Hot;Hot;6;0;Create;True;0;0;False;0;False;0.9811321,0.9760582,0.0786757,0;0.9811321,0.9760582,0.0786757,0;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ColorNode;14;-891.9995,-489.6742;Inherit;False;Property;_Warm;Warm;5;0;Create;True;0;0;False;0;False;0.945098,0.3361675,0.01176471,0;1,0.1553223,0,0;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleDivideOpNode;52;-634.0391,1349.975;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StepOpNode;22;-523.2544,706.0823;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;16;-559.2928,-6.098846;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.StepOpNode;29;-216.3381,903.7046;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;20;-409.9389,318.6172;Inherit;False;Constant;_Float0;Float 0;7;0;Create;True;0;0;False;0;False;2;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode;18;-192.1639,-5.513824;Inherit;False;False;2;0;COLOR;0,0,0,0;False;1;FLOAT;1;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;32;139.9966,889.3926;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;43;-131.4317,1441.111;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;44;-133.1588,1177.424;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;33;546.5967,669.693;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;19;143.7841,305.1115;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.WorldPosInputsNode;55;1376.107,418.4614;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.PosVertexDataNode;53;1379.858,647.8284;Inherit;False;0;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.StepOpNode;46;328.4805,1458.267;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StepOpNode;45;331.0807,1161.867;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;24;936.9662,695.9297;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;47;728.8809,1410.167;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;56;1757.069,494.8799;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;48;1431.95,1136.901;Inherit;False;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;54;2037.053,772.2841;Inherit;False;3;3;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RangedFloatNode;58;1925.194,1053.455;Inherit;False;Property;_WiggleAmount;WiggleAmount;14;0;Create;True;0;0;False;0;False;0;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;27;949.5208,4.94489;Inherit;True;Property;_Metallic;Metallic;10;0;Create;True;0;0;False;0;False;-1;144087ac201058e479630838c477aff8;144087ac201058e479630838c477aff8;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;26;961.4968,-200.0784;Inherit;True;Property;_Normal;Normal;9;0;Create;True;0;0;False;0;False;-1;4e46597c78cabbd449ab9181ac4db86f;4e46597c78cabbd449ab9181ac4db86f;True;0;True;bump;Auto;True;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleSubtractOpNode;50;1816.621,1392.192;Inherit;False;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;57;2441.001,1111.532;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SamplerNode;25;958.1964,-426.3783;Inherit;True;Property;_Albedo;Albedo;8;0;Create;True;0;0;False;0;False;-1;e52d892f11095544ab81f7cfcc6d063c;e52d892f11095544ab81f7cfcc6d063c;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;28;965.5208,212.9449;Inherit;True;Property;_AO;AO;11;0;Create;True;0;0;False;0;False;-1;6d99e07c6c5832f478c65b3a757fe4f8;ec2cba7121dec4646bd60ac55bb1a0dc;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;0;2692.711,651.407;Float;False;True;-1;2;ASEMaterialInspector;0;0;Standard;Burning;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;Back;0;False;-1;0;False;-1;False;0;False;-1;0;False;-1;False;0;Custom;0.5;True;True;0;True;Transparent;;Geometry;All;14;all;True;True;True;True;0;False;-1;False;0;False;-1;255;False;-1;255;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;False;2;15;10;25;False;0.5;True;0;0;False;-1;0;False;-1;0;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;Relative;0;;0;-1;-1;-1;0;True;0;0;False;-1;-1;0;False;-1;0;0;0;False;0.1;False;-1;0;False;-1;False;16;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
WireConnection;12;0;11;0
WireConnection;12;1;13;0
WireConnection;35;0;36;0
WireConnection;35;1;12;0
WireConnection;4;0;3;0
WireConnection;34;0;3;0
WireConnection;34;1;35;0
WireConnection;37;0;34;0
WireConnection;10;1;12;0
WireConnection;5;0;4;0
WireConnection;8;1;10;0
WireConnection;6;0;5;0
WireConnection;6;1;7;0
WireConnection;38;0;37;0
WireConnection;38;1;39;0
WireConnection;42;0;38;0
WireConnection;42;1;41;0
WireConnection;9;0;6;0
WireConnection;9;1;8;0
WireConnection;21;0;1;0
WireConnection;21;1;42;0
WireConnection;30;0;23;0
WireConnection;30;1;31;0
WireConnection;2;0;1;0
WireConnection;2;1;9;0
WireConnection;52;0;51;0
WireConnection;52;1;31;0
WireConnection;22;0;21;1
WireConnection;22;1;23;0
WireConnection;16;0;14;0
WireConnection;16;1;15;0
WireConnection;16;2;2;2
WireConnection;29;0;21;1
WireConnection;29;1;30;0
WireConnection;18;0;16;0
WireConnection;18;1;20;0
WireConnection;32;0;22;0
WireConnection;32;1;29;0
WireConnection;43;0;51;0
WireConnection;44;0;52;0
WireConnection;33;0;22;0
WireConnection;33;1;32;0
WireConnection;19;0;18;0
WireConnection;19;1;20;0
WireConnection;46;0;21;1
WireConnection;46;1;43;0
WireConnection;45;0;21;1
WireConnection;45;1;44;0
WireConnection;24;0;19;0
WireConnection;24;1;33;0
WireConnection;47;0;45;0
WireConnection;47;1;46;0
WireConnection;56;0;55;0
WireConnection;56;1;53;0
WireConnection;48;0;24;0
WireConnection;48;1;47;0
WireConnection;54;0;56;0
WireConnection;54;1;34;0
WireConnection;54;2;22;0
WireConnection;50;0;48;0
WireConnection;50;1;47;0
WireConnection;57;0;54;0
WireConnection;57;1;58;0
WireConnection;0;0;25;0
WireConnection;0;1;26;0
WireConnection;0;2;50;0
WireConnection;0;3;27;0
WireConnection;0;5;28;0
WireConnection;0;9;45;0
WireConnection;0;11;57;0
ASEEND*/
//CHKSM=74DE67CD5BFFF6CD56F7C6EF40125C18CDA44E34