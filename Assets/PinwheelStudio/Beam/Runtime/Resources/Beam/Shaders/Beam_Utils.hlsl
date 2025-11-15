#ifndef BEAM_UTILS_DEFINED
#define BEAM_UTILS_DEFINED

//half4 SampleFroxel(Texture3D<half4> tex3D, float4x4 froxelMatrix_V, float3 positionWS, uint3 id)
//{
//	float2 uv = float2(id.x / (_FroxelCount.x - 1), id.y / (_FroxelCount.y - 1));
//	float sceneDepthEye = GetSceneDepthEye(uv);

//	float3 positionVS = mul(froxelMatrix_V, float4(positionWS, 1)).xyz;
//	float depthEye = -positionVS.z;
//	float depthLinear01 = (depthEye - CAMERA_NEAR_PLANE) / (CAMERA_FAR_PLANE - CAMERA_NEAR_PLANE); //samplePos.z
//	float passedDepthTest = (sceneDepthEye > depthEye) ?          1 : 0;

//	float viewPlaneHeight = 2 * depthEye * tan(radians(CAMERA_FOV * 0.5));
//	float viewPlaneWidth = viewPlaneHeight * CAMERA_ASPECT;
//	float u = positionVS.x / viewPlaneWidth + 0.5;
//	float v = positionVS.y / viewPlaneHeight + 0.5;

//	float3 sp = float3(u, v, depthLinear01);
//	float inBoundMask =
//	(sp.x >= 0) * (sp.y >= 0) * (sp.z >= 0) *
//	(sp.x <= 1) * (sp.y <= 1) * (sp.z <= 1);
//	half4 froxel = tex3D.SampleLevel(sampler_LinearClamp, sp, 0) * inBoundMask * passedDepthTest;
//	froxel.a = inBoundMask;

//	return froxel;
//}


float4 InverseLerp(float4 v, float4 a, float4 b)
{
	return (v - a) / (b - a);
}

float4 FilterLevels(float4 value, float4 inLow, float4 inMid, float4 inHigh)
{
	float4 low = inLow;
	float4 high = inHigh;
	float4 mid = lerp(low, high, inMid);

	float4 v0 = InverseLerp(value, low, mid * 2 - low);
	float4 v1 = InverseLerp(value, mid * 2 - high, high);

	return v0 * (value <= 0.5) + v1 * (value > 0.5);
}


#endif