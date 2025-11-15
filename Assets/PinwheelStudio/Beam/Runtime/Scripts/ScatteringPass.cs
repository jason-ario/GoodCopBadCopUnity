using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;

namespace Pinwheel.Beam
{
    public sealed partial class VolumetricLightingRendererFeature
    {
        private class ScatteringPass : ScriptableRenderPass
        {
            private ShaderData m_ShaderData;
            public QualityLevel qualityLevel { get; set; }
            public FeatureOverrideOptions useAdditionalLights { get; set; }
            public FeatureOverrideOptions useCookies { get; set; }
            public bool useLocalFogs { get; set; }
            public bool useFogsHeightAttenuation { get; set; }
            public bool useFogsNoiseAttenuation { get; set; }

            public float time { get; set; }

            private Plane[] m_FrustumPlanes = new Plane[6];
            private List<VolumetricFog> m_Fogs = new List<VolumetricFog>();
            private Matrix4x4[] m_FogsWorldToLocalMatrix = new Matrix4x4[MAX_VISIBLE_FOG_VOLUME];
            private Vector4[] m_FogsColor = new Vector4[MAX_VISIBLE_FOG_VOLUME];
            private Vector4[] m_LightsAdditionalData = new Vector4[MAX_VISIBLE_LIGHT];

            public class PassData
            {
                public ComputeShader computeShader;
                public TextureHandle scatteringTexture;
                public float cameraNearPlane;
                public float cameraFarPlane;
                public float cameraFov;
                public float cameraAspect;
                public Vector3Int froxelCount;
                public int additionalLightsCount;
                public float time;

                public bool useLocalFogs;
                public Matrix4x4[] fogsWorldToLocalMatrix;
                public Vector4[] fogsColor;
                public int fogsCount;

                public Vector4 mainLightAdditionalData;
                public Vector4[] lightsAdditionalData;

                public bool useFogsHeightAttenuation;
                public bool useFogsNoiseAttenuation;
            }

            public class PassContextData : ContextItem
            {
                public TextureHandle scatteringTexture;

                public override void Reset()
                {
                    scatteringTexture = TextureHandle.nullHandle;
                }
            }

            public ScatteringPass(ShaderData shaderData)
            {
                requiresIntermediateTexture = false;
                m_ShaderData = shaderData;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                int froxelSizePixels = VolumetricLightingRendererFeature.GetFroxelSizePixels(qualityLevel);

                RenderTextureDescriptor scattering3dRtDesc = new RenderTextureDescriptor();
                scattering3dRtDesc.width = Utilities.CeilMulOf8(cameraData.scaledWidth / froxelSizePixels);
                scattering3dRtDesc.height = Utilities.CeilMulOf8(cameraData.scaledHeight / froxelSizePixels);
                scattering3dRtDesc.volumeDepth = FROXEL_SLICE_COUNT;
                scattering3dRtDesc.dimension = TextureDimension.Tex3D;
                scattering3dRtDesc.enableRandomWrite = true;
                scattering3dRtDesc.msaaSamples = 1;
                scattering3dRtDesc.graphicsFormat = GraphicsFormat.R16G16B16_SFloat;
                scattering3dRtDesc.colorFormat = RenderTextureFormat.ARGBHalf;
                scattering3dRtDesc.depthBufferBits = 0;

                TextureDesc scatteringTexture3dDesc = new TextureDesc(scattering3dRtDesc);
                scatteringTexture3dDesc.name = "VolumetricLightScatteringTexture3D";
                scatteringTexture3dDesc.filterMode = FilterMode.Bilinear;
                scatteringTexture3dDesc.clearBuffer = false;

                TextureHandle scatteringTexture = renderGraph.CreateTexture(scatteringTexture3dDesc);

                m_ShaderData.scatteringCS.SetKeyword(ShaderData.KW_USE_ADDITIONAL_LIGHTS, useAdditionalLights == FeatureOverrideOptions.Off ? false : true);
                m_ShaderData.scatteringCS.SetKeyword(ShaderData.KW_USE_COOKIES, useCookies == FeatureOverrideOptions.Off ? false : true);
                m_ShaderData.scatteringCS.SetKeyword(ShaderData.KW_USE_LOCAL_FOGS, useLocalFogs);

                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                using (var builder = renderGraph.AddComputePass<PassData>("VL Scatter", out var passData, new ProfilingSampler(ProfilingId.SCATTERING_PASS)))
                {
                    passData.computeShader = m_ShaderData.scatteringCS;
                    passData.cameraNearPlane = cameraData.camera.nearClipPlane;
                    passData.cameraFarPlane = cameraData.camera.farClipPlane;
                    passData.cameraFov = cameraData.camera.fieldOfView;
                    passData.cameraAspect = cameraData.camera.aspect;
                    passData.additionalLightsCount = lightData.additionalLightsCount;
                    passData.time = time;

                    if (resourcesData.mainShadowsTexture.IsValid())
                    {
                        builder.UseTexture(resourcesData.mainShadowsTexture, AccessFlags.Read);
                    }

                    passData.scatteringTexture = scatteringTexture;
                    builder.UseTexture(scatteringTexture, AccessFlags.Write);

                    passData.froxelCount = new Vector3Int(scatteringTexture3dDesc.width, scatteringTexture3dDesc.height, scatteringTexture3dDesc.slices);

#if UNITY_EDITOR
                    Debug.Assert(passData.froxelCount.x >= 8);
                    Debug.Assert(passData.froxelCount.y >= 8);
                    Debug.Assert(passData.froxelCount.z >= 8);
                    Debug.Assert(passData.froxelCount.x % 8 == 0);
                    Debug.Assert(passData.froxelCount.y % 8 == 0);
                    Debug.Assert(passData.froxelCount.z % 8 == 0);
#endif

                    int punctualLightIndex = 0;
                    for (int iLight = 0; iLight < lightData.visibleLights.Length; ++iLight)
                    {
                        Vector4 lightAdditionalData = new Vector4();

                        Light lightComponent = lightData.visibleLights[iLight].light;
                        VolumetricRealtimeLight vrl = VolumetricRealtimeLight.GetForLight(lightComponent);
                        if (vrl != null)
                        {
                            lightAdditionalData.w = vrl.excluded ? 0 : 1;
                        }
                        else
                        {
                            lightAdditionalData.w = 1;
                        }

                        if (iLight == lightData.mainLightIndex)
                        {
                            passData.mainLightAdditionalData = lightAdditionalData;
                        }
                        else
                        {
                            m_LightsAdditionalData[punctualLightIndex++] = lightAdditionalData;
                        }
                    }
                    passData.lightsAdditionalData = m_LightsAdditionalData;

                    passData.useLocalFogs = useLocalFogs;
                    if (useLocalFogs)
                    {
                        GeometryUtility.CalculateFrustumPlanes(cameraData.camera, m_FrustumPlanes);
                        VolumetricFog.GetAllVisibleVolume(m_FrustumPlanes, m_Fogs);
                        for (int iFog = 0; iFog < Mathf.Min(MAX_VISIBLE_FOG_VOLUME, m_Fogs.Count); ++iFog)
                        {
                            VolumetricFog fog = m_Fogs[iFog];
                            Color fogColor = fog.color;
                            fogColor.a = fog.density;
                            m_FogsColor[iFog] = fogColor;
                            m_FogsWorldToLocalMatrix[iFog] = fog.transform.worldToLocalMatrix;
                        }

                        passData.fogsColor = m_FogsColor;
                        passData.fogsWorldToLocalMatrix = m_FogsWorldToLocalMatrix;
                        passData.fogsCount = Mathf.Min(MAX_VISIBLE_FOG_VOLUME, m_Fogs.Count);
                    }
                    passData.useFogsHeightAttenuation = useFogsHeightAttenuation;
                    passData.useFogsNoiseAttenuation = useFogsNoiseAttenuation;

                    builder.SetRenderFunc(static (PassData passData, ComputeGraphContext context) =>
                    {
                        //These values were set by Unity
                        //UNITY_MATRIX_I_V: view to world matrix
                        //_MainLightShadowmapTexture: main directional shadow map (and other shadow related vectors)
                        //_MainLightColor: main directional light color
                        //_CameraDepthTexture: camera depth texture
                        //Other _xParams (zBufferParams, orthoParams, etc.)

                        const int KERNEL_COMPUTE_LIGHT_SCATTERING = 0;

                        context.cmd.SetComputeTextureParam(passData.computeShader, KERNEL_COMPUTE_LIGHT_SCATTERING, ShaderData.SCATTERING_TEXTURE, passData.scatteringTexture);
                        context.cmd.SetComputeVectorParam(passData.computeShader, ShaderData.FROXEL_COUNT, new Vector4(passData.froxelCount.x, passData.froxelCount.y, passData.froxelCount.z));
                        context.cmd.SetComputeFloatParam(passData.computeShader, ShaderData.ADDITIONAL_LIGHT_COUNT, passData.additionalLightsCount);
                        context.cmd.SetComputeFloatParam(passData.computeShader, ShaderData.BEAM_TIME, passData.time);
                        context.cmd.SetComputeVectorParam(passData.computeShader, ShaderData.MAIN_LIGHT_ADDITIONAL_DATA, passData.mainLightAdditionalData);
                        context.cmd.SetComputeVectorArrayParam(passData.computeShader, ShaderData.LIGHTS_ADDITIONAL_DATA, passData.lightsAdditionalData);

                        Vector4 cameraParam = new Vector4(passData.cameraNearPlane, passData.cameraFarPlane, passData.cameraFov, passData.cameraAspect);
                        Vector4 heightFogParams = new Vector4();
                        Vector4 noiseFogParams = new Vector4();
                        Vector4 noiseFogWindParams = new Vector4();
                        VolumetricLightingVolumeComponent volume = VolumeManager.instance.stack?.GetComponent<VolumetricLightingVolumeComponent>();
                        if (volume != null)
                        {
                            context.cmd.SetComputeFloatParam(passData.computeShader, ShaderData.INTENSITY, volume.intensity.value);
                            context.cmd.SetComputeFloatParam(passData.computeShader, ShaderData.ANISOTROPIC, volume.anisotropic.value);
                            context.cmd.SetComputeFloatParam(passData.computeShader, ShaderData.DEPTH_BIAS_EYE, volume.depthBias.value);
                            cameraParam.y = volume.maxDistance.value;

                            heightFogParams.x = passData.useFogsHeightAttenuation ? 1.0f : 0.0f;
                            heightFogParams.y = volume.fogMinHeight.value;
                            heightFogParams.z = volume.fogMaxHeight.value;
                            heightFogParams.w = volume.fogHeightAttenuationFactor.value;

                            noiseFogParams.x = passData.useFogsNoiseAttenuation ? 1.0f : 0.0f;
                            noiseFogParams.y = volume.fogNoiseFrequency.value;

                            noiseFogWindParams = volume.fogNoiseWindDirection.value;
                            noiseFogWindParams.w = volume.fogNoiseWindStrength.value;
                        }
                        context.cmd.SetComputeVectorParam(passData.computeShader, ShaderData.CAMERA_PARAMS, cameraParam);
                        context.cmd.SetComputeVectorParam(passData.computeShader, ShaderData.HEIGHT_FOG_PARAMS, heightFogParams);
                        context.cmd.SetComputeVectorParam(passData.computeShader, ShaderData.NOISE_FOG_PARAMS, noiseFogParams);
                        context.cmd.SetComputeVectorParam(passData.computeShader, ShaderData.NOISE_FOG_WIND_PARAMS, noiseFogWindParams);

                        if (passData.useLocalFogs)
                        {
                            context.cmd.SetComputeVectorArrayParam(passData.computeShader, ShaderData.FOGS_COLOR, passData.fogsColor);
                            context.cmd.SetComputeMatrixArrayParam(passData.computeShader, ShaderData.FOGS_WORLD2LOCAL_MATRIX, passData.fogsWorldToLocalMatrix);
                            context.cmd.SetComputeFloatParam(passData.computeShader, ShaderData.FOGS_COUNT, passData.fogsCount);
                        }

                        int closeSliceCount = Utilities.CeilMulOf8(passData.froxelCount.z / 4);
                        int nearSliceCount = closeSliceCount;
                        int midSliceCount = closeSliceCount;
                        int farSliceCount = passData.froxelCount.z - closeSliceCount - midSliceCount - nearSliceCount;

                        context.cmd.SetComputeFloatParam(passData.computeShader, ShaderData.FROXEL_SAMPLE_COUNT, 4);
                        context.cmd.SetComputeFloatParam(passData.computeShader, ShaderData.SLICE_START_INDEX, 0);
                        context.cmd.DispatchCompute(passData.computeShader, KERNEL_COMPUTE_LIGHT_SCATTERING, passData.froxelCount.x / 8, passData.froxelCount.y / 8, closeSliceCount / 8);

                        context.cmd.SetComputeFloatParam(passData.computeShader, ShaderData.FROXEL_SAMPLE_COUNT, 3);
                        context.cmd.SetComputeFloatParam(passData.computeShader, ShaderData.SLICE_START_INDEX, closeSliceCount);
                        context.cmd.DispatchCompute(passData.computeShader, KERNEL_COMPUTE_LIGHT_SCATTERING, passData.froxelCount.x / 8, passData.froxelCount.y / 8, nearSliceCount / 8);

                        context.cmd.SetComputeFloatParam(passData.computeShader, ShaderData.FROXEL_SAMPLE_COUNT, 2);
                        context.cmd.SetComputeFloatParam(passData.computeShader, ShaderData.SLICE_START_INDEX, closeSliceCount + nearSliceCount);
                        context.cmd.DispatchCompute(passData.computeShader, KERNEL_COMPUTE_LIGHT_SCATTERING, passData.froxelCount.x / 8, passData.froxelCount.y / 8, midSliceCount / 8);

                        context.cmd.SetComputeFloatParam(passData.computeShader, ShaderData.FROXEL_SAMPLE_COUNT, 1);
                        context.cmd.SetComputeFloatParam(passData.computeShader, ShaderData.SLICE_START_INDEX, passData.froxelCount.z - farSliceCount);
                        context.cmd.DispatchCompute(passData.computeShader, KERNEL_COMPUTE_LIGHT_SCATTERING, passData.froxelCount.x / 8, passData.froxelCount.y / 8, farSliceCount / 8);
                    });

                    ScatteringPass.PassContextData passContextData = frameData.Create<ScatteringPass.PassContextData>();
                    passContextData.scatteringTexture = scatteringTexture;
                }
            }
        }
    }
}