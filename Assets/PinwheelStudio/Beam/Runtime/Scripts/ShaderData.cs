using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Beam
{
    //[CreateAssetMenu(menuName = "Beam Internal/Shader Data")]
    [System.Serializable]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public class ShaderData : ScriptableObject
    {
        public ComputeShader scatteringCS;
        public ComputeShader accumulationCS;

        public Material filterCombineMaterial;
        public Material integrationMaterial;

        public const string KW_USE_COOKIES = "BEAM_LIGHT_COOKIES";
        public const string KW_USE_ADDITIONAL_LIGHTS = "BEAM_ADDITIONAL_LIGHTS";
        public const string KW_USE_ADDITIONAL_LIGHT_SHADOWS = "BEAM_ADDITIONAL_LIGHT_SHADOWS";
        public const string KW_USE_LOCAL_FOGS = "BEAM_FOGS_LOCAL";
        public const string KW_USE_FOGS_HEIGHT_ATTEN = "BEAM_FOGS_HEIGHT_ATTEN";
        public const string KW_USE_FOGS_NOISE_ATTEN = "BEAM_FOGS_NOISE_ATTEN";

        public static readonly int BEAM_TIME = Shader.PropertyToID("_BeamTime");
        public static readonly int SCATTERING_TEXTURE = Shader.PropertyToID("_ScatteringTexture");
        public static readonly int FROXEL_COUNT = Shader.PropertyToID("_FroxelCount");
        public static readonly int INTENSITY = Shader.PropertyToID("_Intensity");
        public static readonly int ANISOTROPIC = Shader.PropertyToID("_Anisotropic");
        public static readonly int DEPTH_BIAS_EYE = Shader.PropertyToID("_DepthBiasEye");
        public static readonly int CAMERA_PARAMS = Shader.PropertyToID("_CameraParams");
        public static readonly int ADDITIONAL_LIGHT_COUNT = Shader.PropertyToID("_BeamAdditionalLightsCount");
        public static readonly int MAIN_LIGHT_ADDITIONAL_DATA = Shader.PropertyToID("_BeamMainLightAdditionalData");
        public static readonly int LIGHTS_ADDITIONAL_DATA = Shader.PropertyToID("_BeamLightsAdditionalData");
        public static readonly int FROXEL_SAMPLE_COUNT = Shader.PropertyToID("_FroxelSampleCount");
        public static readonly int SLICE_START_INDEX = Shader.PropertyToID("_SliceStartIndex");
        public static readonly int FOGS_WORLD2LOCAL_MATRIX = Shader.PropertyToID("_BeamFogsWorldToLocalMatrix");
        public static readonly int FOGS_COLOR = Shader.PropertyToID("_BeamFogsColor");
        public static readonly int FOGS_COUNT = Shader.PropertyToID("_BeamFogsCount");
        public static readonly int HEIGHT_FOG_PARAMS = Shader.PropertyToID("_HeightFogParams");
        public static readonly int NOISE_FOG_PARAMS = Shader.PropertyToID("_NoiseFogParams");
        public static readonly int NOISE_FOG_WIND_PARAMS = Shader.PropertyToID("_NoiseFogWindParams");
        public static readonly int ACCUMULATION_TEXTURE = Shader.PropertyToID("_AccumulationTexture");
        public static readonly int BLUR_RADIUS = Shader.PropertyToID("_BlurRadius");
        public static readonly int ACCUM_TEX_FULL = Shader.PropertyToID("_AccumTexFull");
        public static readonly int ACCUM_TEX_HALF = Shader.PropertyToID("_AccumTexHalf");
        public static readonly int ACCUM_TEX_FOURTH = Shader.PropertyToID("_AccumTexFourth");
        public static readonly int ACCUM_TEX_EIGHTH = Shader.PropertyToID("_AccumTexEighth");
        public static readonly int ACCUM_TEX_SIXTEENTH = Shader.PropertyToID("_AccumTexSixteenth");
        public static readonly int BLIT_TEXTURE = Shader.PropertyToID("_BlitTexture");
        public static readonly int BLIT_SCALE_BIAS = Shader.PropertyToID("_BlitScaleBias");
        public static readonly int DARK = Shader.PropertyToID("_Dark");
        public static readonly int BRIGHT = Shader.PropertyToID("_Bright");
    }
}
