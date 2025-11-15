#ifndef VOLUMETRIC_FOG_DEFINED
#define VOLUMETRIC_FOG_DEFINED

#include "Beam_Constants.hlsl"

struct BeamFog
{
	float3 color;
	float density;
};

#ifndef SHADER_API_GLES3
	CBUFFER_START(VolumetricFogs)
#endif
float4x4 _BeamFogsWorldToLocalMatrix[BEAM_MAX_VISIBLE_FOG_VOLUME];
float4 _BeamFogsColor[BEAM_MAX_VISIBLE_FOG_VOLUME];
#ifndef SHADER_API_GLES3
	CBUFFER_END
#endif

float _BeamFogsCount;

uint Beam_GetFogsCount()
{
	return uint(_BeamFogsCount);
}

BeamFog Beam_GetFog(uint index, float3 positionWS)
{
	float4x4 worldToLocal = _BeamFogsWorldToLocalMatrix[index];
	float4 color = _BeamFogsColor[index];

	float3 positionOS = mul(worldToLocal, float4(positionWS, 1)).xyz;
	float boundAttenuation =
	(positionOS.x >= -0.5) * (positionOS.x <= 0.5) *
	(positionOS.y >= -0.5) * (positionOS.y <= 0.5) *
	(positionOS.z >= -0.5) * (positionOS.z <= 0.5);

	BeamFog fog;
	fog.color = color.rgb;
	fog.density = color.a * boundAttenuation;
	return fog;
}

#endif