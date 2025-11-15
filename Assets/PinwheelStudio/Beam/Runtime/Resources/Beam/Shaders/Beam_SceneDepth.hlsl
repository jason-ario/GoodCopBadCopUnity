#ifndef BEAM_SCENE_DEPTH_DEFINED
#define BEAM_SCENE_DEPTH_DEFINED

float3 EyeDepthToWorldPosition(float depthEye, float2 uv, float cameraFov, float cameraAspect)
{
	float viewPlaneHeight = 2 * depthEye * tan(radians(cameraFov * 0.5));
	float viewPlaneWidth = viewPlaneHeight * cameraAspect;
	float x = viewPlaneWidth * (uv.x - 0.5);
	float y = viewPlaneHeight * (uv.y - 0.5);
	float4 positionVS = float4(x, y, -depthEye, 1);
	float4 positionWS = mul(UNITY_MATRIX_I_V, positionVS);
	return positionWS.xyz;
}

float _SampleSceneDepth(float2 uv, SAMPLER(samplerParam))
{
	uv = ClampAndScaleUVForBilinear(UnityStereoTransformScreenSpaceTex(uv), _CameraDepthTexture_TexelSize.xy);
	return _CameraDepthTexture.SampleLevel(samplerParam, uv, 0).r; //The builtin function in DeclareDepthTexture.hlsl cannot be used in compute shader, mod the macro to use SampleLevel() instead

}

float _SampleSceneDepth(float2 uv)
{
	return _SampleSceneDepth(uv, sampler_PointClamp);
}

float GetSceneDepthEye(float2 uv)
{
	if (unity_OrthoParams.w == 1.0)
	{
		return LinearEyeDepth(ComputeWorldSpacePosition(uv.xy, _SampleSceneDepth(uv.xy), UNITY_MATRIX_I_VP), UNITY_MATRIX_V);
	}
	else
	{
		return LinearEyeDepth(_SampleSceneDepth(uv.xy), _ZBufferParams);
	}
}

#endif