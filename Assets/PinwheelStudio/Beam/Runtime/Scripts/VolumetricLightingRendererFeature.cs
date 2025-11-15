using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using StopWatch = System.Diagnostics.Stopwatch;

namespace Pinwheel.Beam
{
    /// <summary>
    /// Add this renderer feature to your URP renderer data asset to render volumetric lighting and fog effect.
    /// </summary>
    public sealed partial class VolumetricLightingRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private ShaderData m_ShaderData;
        [SerializeField]
        private QualityLevel m_Quality = QualityLevel.InGame;

        [Header("Lights")]
        [SerializeField]
        private FeatureOverrideOptions m_UseAdditionalLights = FeatureOverrideOptions.UseRenderPipelineSettings;
        [SerializeField]
        private FeatureOverrideOptions m_UseCookies = FeatureOverrideOptions.UseRenderPipelineSettings;

        [Header("Fogs")]
        [SerializeField]
        private bool m_UseLocalFogs = false;
        [SerializeField]
        private bool m_UseFogsHeightAttenuation = true;
        [SerializeField]
        private bool m_UseFogsNoiseAttenuation = true;

        /// <summary>
        /// Size of the "froxels texture" 3D, in Z dimension.
        /// </summary>
        public const int FROXEL_SLICE_COUNT = 64;
        /// <summary>
        /// Maximum number of realtime light to render in scattering pass, this value is aligned with URP light limit.
        /// </summary>
        public const int MAX_VISIBLE_LIGHT = 256; //on PC
        /// <summary>
        /// Maximum number of volumetric fog volume that is visible.
        /// </summary>
        public const int MAX_VISIBLE_FOG_VOLUME = 256;

        private ScatteringPass m_ScatteringPass;
        private AccumulationPass m_AccumulationPass;
        private FilterPass m_FilterPass;
        private IntegrationPass m_IntegrationPass;

        private StopWatch m_StopWatch;

        /// <exclude />
        public override void Create()
        {
            Debug.Assert(FROXEL_SLICE_COUNT % 8 == 0, $"{nameof(FROXEL_SLICE_COUNT)} should be multiple of 8");

            if (m_ShaderData == null)
            {
                m_ShaderData = Resources.Load<ShaderData>("Beam/VolumetricLightingShaderData");
            }

            m_StopWatch = new StopWatch();
            m_StopWatch.Start();
        }

        /// <exclude />
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_ShaderData == null)
            {
                Debug.LogWarning("Missing VolumetricLightingShaderData asset");
                return;
            }

            if (renderingData.cameraData.cameraType != CameraType.Game &&
                renderingData.cameraData.cameraType != CameraType.SceneView)
                return;

            if (m_ScatteringPass == null)
            {
                m_ScatteringPass = new ScatteringPass(m_ShaderData);
            }
            m_ScatteringPass.qualityLevel = m_Quality;
            m_ScatteringPass.useAdditionalLights = m_UseAdditionalLights;
            m_ScatteringPass.useCookies = m_UseCookies;
            m_ScatteringPass.useLocalFogs = m_UseLocalFogs;
            m_ScatteringPass.useFogsHeightAttenuation = m_UseFogsHeightAttenuation;
            m_ScatteringPass.useFogsNoiseAttenuation = m_UseFogsNoiseAttenuation;
            m_ScatteringPass.time = m_StopWatch.ElapsedMilliseconds * 0.001f;
            m_ScatteringPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            m_ScatteringPass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(m_ScatteringPass);

            if (m_AccumulationPass == null)
            {
                m_AccumulationPass = new AccumulationPass(m_ShaderData);
            }
            m_AccumulationPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            m_AccumulationPass.ConfigureInput(ScriptableRenderPassInput.None);
            renderer.EnqueuePass(m_AccumulationPass);

            if (m_FilterPass == null)
            {
                m_FilterPass = new FilterPass(m_ShaderData);
            }
            m_FilterPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            m_FilterPass.ConfigureInput(ScriptableRenderPassInput.None);
            renderer.EnqueuePass(m_FilterPass);

            if (m_IntegrationPass == null)
            {
                m_IntegrationPass = new IntegrationPass(m_ShaderData);
            }
            m_IntegrationPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            m_IntegrationPass.ConfigureInput(ScriptableRenderPassInput.Color);
            renderer.EnqueuePass(m_IntegrationPass);
        }

        /// <exclude />
        protected override void Dispose(bool disposing)
        {
            if (m_ShaderData != null)
            {
                Resources.UnloadAsset(m_ShaderData);
                m_ShaderData = null;
            }

            m_ScatteringPass = null;
            m_AccumulationPass = null;
            m_FilterPass = null;
            m_IntegrationPass = null;
            m_StopWatch = null;
        }

        /// <summary>
        /// Get the number of pixels covered by 1 froxel on the screen. Larger = lower quality.
        /// </summary>
        /// <param name="quality">Quality level</param>
        /// <returns>Size of 1 froxel in pixels</returns>
        public static int GetFroxelSizePixels(QualityLevel quality)
        {
            switch (quality)
            {
                case QualityLevel.InGameLow: return 16;
                case QualityLevel.InGame: return 8;
                case QualityLevel.Cinematic: return 4;
                case QualityLevel.CinematicHigh: return 2;
                default: return 8;
            }
        }
    }
}