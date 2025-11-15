using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Pinwheel.Beam
{
    /// <summary>
    /// Add this to your volume profile to override default volumetric lighting and fog settings
    /// </summary>
    [VolumeComponentMenu("Post-processing Custom/Volumetric Lighting")]
    [VolumeRequiresRendererFeatures(typeof(VolumetricLightingRendererFeature))]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class VolumetricLightingVolumeComponent : VolumeComponent, IPostProcessComponent
    {
        public VolumetricLightingVolumeComponent()
        {
            displayName = "Volumetric Lighting";
        }

        /// <summary>
        /// Overall strength of the effect. Too high might cause visual artifacts.
        /// </summary>
        [Header("Light Scattering")]        
        public MinFloatParameter intensity = new MinFloatParameter(0f, 0f);
        /// <summary>
        /// Define scatter angle of incoming light. Low value makes photons scatter more forward, while high value makes them scatter in all direction.
        /// </summary>
        public ClampedFloatParameter anisotropic = new ClampedFloatParameter(0.85f, 0.05f, 1f);
        /// <summary>
        /// Maximum distance from the camera to simulate light scattering effect. Low value will miss possibly visible light, while too high value might produce flickering artifact.
        /// </summary>
        public ClampedFloatParameter maxDistance = new ClampedFloatParameter(64f, 1f, 100f);

        /// <summary>
        /// Minimum world space height where fog is visible.
        /// </summary>
        [Header("Fog Height Attenuation")]
        public FloatParameter fogMinHeight = new FloatParameter(0);
        /// <summary>
        /// Maximum world space height where fog is visible.
        /// </summary>
        public FloatParameter fogMaxHeight = new FloatParameter(100);
        /// <summary>
        /// Contributes in fog attenuation based on world height.
        /// </summary>
        public MinFloatParameter fogHeightAttenuationFactor = new MinFloatParameter(1f, 0f);

        /// <summary>
        /// Frequency of the 3D noise used in fog attenuation.
        /// </summary>
        [Header("Fog Noise Attenuation")]
        public MinFloatParameter fogNoiseFrequency = new MinFloatParameter(5, 1);
        /// <summary>
        /// Direction of the wind, contributes in fog animation.
        /// </summary>
        public Vector3Parameter fogNoiseWindDirection = new Vector3Parameter(Vector3.up);
        /// <summary>
        /// Strength of fog wind, contributes in fog animation.
        /// </summary>
        public MinFloatParameter fogNoiseWindStrength = new MinFloatParameter(1, 0);

        /// <summary>
        /// Make the volumetric light looks softer, reducing aliasing due to low resolution simulation.
        /// </summary>
        [Header("Filter")]
        public ClampedFloatParameter softness = new ClampedFloatParameter(0.35f, 0f, 1f);
        /// <summary>
        /// Help remove banding/acne artifact on the floor.
        /// </summary>
        public MinFloatParameter depthBias = new MinFloatParameter(0.4f, 0f);
        /// <summary>
        /// Emphasize the dark area of light scattering.
        /// </summary>
        public ClampedFloatParameter dark = new ClampedFloatParameter(0f, 0f, 1f);
        /// <summary>
        /// Emphasize the bright area of light scattering.
        /// </summary>
        public ClampedFloatParameter bright = new ClampedFloatParameter(0f, 0f, 1f);

        /// <summary>
        /// Is the effect active and being rendered in the scene
        /// </summary>
        /// <returns>True if the effect is active.</returns>
        public bool IsActive()
        {
            return intensity.GetValue<float>() > 0.0f;
        }
    }
}