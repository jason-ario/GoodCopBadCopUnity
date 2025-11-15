#ifndef VOLUMETRIC_REALTIME_LIGHTS_DEFINED
#define VOLUMETRIC_REALTIME_LIGHTS_DEFINED

#include "Beam_Constants.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"

float _BeamAdditionalLightsCount; //Unity's _AdditionalLightCount is limited by max light per object, this property define the number of all visible lights in the frustum
float4 _BeamMainLightAdditionalData;
float4 _BeamLightsAdditionalData[BEAM_MAX_VISIBLE_LIGHT];

//Contains a mod to get rid of division by zero warning
real Beam_SampleShadowmap(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord, ShadowSamplingData samplingData, half4 shadowParams, bool isPerspectiveProjection = true)
{
	// Compiler will optimize this branch away as long as isPerspectiveProjection is known at compile time
	if (isPerspectiveProjection)
		shadowCoord.xyz /= max(0.0001, shadowCoord.w);

	real attenuation;
	real shadowStrength = shadowParams.x;

	attenuation = real(SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, shadowCoord.xyz));
	attenuation = LerpWhiteTo(attenuation, shadowStrength);

	// Shadow coords that fall out of the light frustum volume must always return attenuation 1.0
	// TODO: We could use branch here to save some perf on some platforms.
	return BEYOND_SHADOW_FAR(shadowCoord) ?  1.0 : attenuation;
}

//Mod: removing screenspace shadowmap
half Beam_MainLightRealtimeShadow(float4 shadowCoord)
{
	#if !defined(MAIN_LIGHT_CALCULATE_SHADOWS)
		return half(1.0);
	#else
		ShadowSamplingData shadowSamplingData = GetMainLightShadowSamplingData();
		half4 shadowParams = GetMainLightShadowParams();
		return Beam_SampleShadowmap(TEXTURE2D_ARGS(_MainLightShadowmapTexture, sampler_LinearClampCompare), shadowCoord, shadowSamplingData, shadowParams, false);
	#endif
}

//Mod: removing distance attenuation
Light Beam_GetMainLight()
{
	Light light;
	light.direction = half3(_MainLightPosition.xyz);
	light.distanceAttenuation = 1.0;
	light.shadowAttenuation = 1.0;
	light.color = _MainLightColor.rgb;
	light.layerMask = _MainLightLayerMask;

	return light;
}

//Mod: also calculate realtime shadow at that position
Light Beam_GetMainLight(float3 positionWS)
{
	float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
	Light light = Beam_GetMainLight();
	light.shadowAttenuation = Beam_MainLightRealtimeShadow(shadowCoord);
	return light;
}

//Mod: also calculate realtime shadow & cookies
Light Beam_GetAdditionalLight(uint index, float3 positionWS)
{
	#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
		float4 lightPositionWS = _AdditionalLightsBuffer[index].position;
		half3 color = _AdditionalLightsBuffer[index].color.rgb;
		half4 distanceAndSpotAttenuation = _AdditionalLightsBuffer[index].attenuation;
		half4 spotDirection = _AdditionalLightsBuffer[index].spotDirection;
		uint lightLayerMask = _AdditionalLightsBuffer[index].layerMask;
	#else
		float4 lightPositionWS = _AdditionalLightsPosition[index];
		half3 color = _AdditionalLightsColor[index].rgb;
		half4 distanceAndSpotAttenuation = _AdditionalLightsAttenuation[index];
		half4 spotDirection = _AdditionalLightsSpotDir[index];
		uint lightLayerMask = asuint(_AdditionalLightsLayerMasks[index]);
	#endif

	// Directional lights store direction in lightPosition.xyz and have .w set to 0.0.
	// This way the following code will work for both directional and punctual lights.
	float3 lightVector = lightPositionWS.xyz - positionWS * lightPositionWS.w;
	float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);

	half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));
	// full-float precision required on some platforms
	float attenuation = DistanceAttenuation(distanceSqr, distanceAndSpotAttenuation.xy) * AngleAttenuation(spotDirection.xyz, lightDirection, distanceAndSpotAttenuation.zw);

	Light light;
	light.direction = lightDirection;
	light.distanceAttenuation = attenuation;
	light.color = color;
	#if defined(_ADDITIONAL_LIGHT_SHADOWS)
		light.shadowAttenuation = AdditionalLightRealtimeShadow(index, positionWS, lightDirection);
	#else
		light.shadowAttenuation = 1;
	#endif

	#if defined(BEAM_LIGHT_COOKIES) && defined(_LIGHT_COOKIES)
		real3 cookieColor = SampleAdditionalLightCookie(index, positionWS);
		light.color *= cookieColor;
	#endif

	return light;
}

int Beam_GetAdditionalLightsCount()
{
	return int(_BeamAdditionalLightsCount);
}

bool Beam_IsMainLightExcluded()
{
	float4 data = _BeamMainLightAdditionalData;
	return data.w == 0.0;
}

bool Beam_IsAdditionalLightExcluded(uint lightIndex)
{
	float4 data = _BeamLightsAdditionalData[lightIndex];
	return data.w == 0.0;
}
#endif