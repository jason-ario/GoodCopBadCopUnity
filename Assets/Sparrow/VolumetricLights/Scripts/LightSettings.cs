// 
// Copyright (c) 2023 Off The Beaten Track UG
// All rights reserved.
// 
// Maintainer: Jens Bahr
// 

using UnityEngine;

namespace Sparrow.VolumetricLight
{
    [System.Serializable]
    public class LightSettings 
    {
        public bool init = false;
        public int textureResolution => 256;

        //BLENDING OPTIONS
        [Tooltip("Choose the blending mode for the volumetric light. 'Alpha' for standard blending, 'Additive' for brighter, cumulative effects.")]
        public VolumetricLight.BlendingMode blendingMode = VolumetricLight.BlendingMode.Alpha;
        [Tooltip("Adjust the transparency of the light. Ranges from 0 (fully transparent) to 1 (fully opaque).")]
        [Range(0f, 1f)] public float alpha = 1f;

        // COLOR OVERRIDES
        [Tooltip("Enable this to override the light's color with a custom color defined below.")]
        public bool overrideLightColor;
        [Tooltip("Set the new color of the light when override is enabled.")]
        [ColorUsage(false, false)] public Color newColor = Color.white;
        [Tooltip("Define the new intensity of the light when override is enabled.")]
        [Min(0f)] public float newIntensity = 1f;

        [Tooltip("Multiplier for the light's intensity. Ranges from 0 to 2.")]
        [Range(0f, 2f)] public float intensityMultiplier = 1f;

        // FADING OPTIONS
        [Tooltip("Enable this to use a custom gradient for light fading.")]
        public bool useCustomFade;
        [Tooltip("Define the gradient used for light fading when custom fade is enabled.")]
        [GradientUsage(false)] public Gradient fadeGradient = new Gradient();
        [SerializeField] private Texture2D fadeTexture;

        // GEOMETRY AND DEPTH FADING OVERRIDES
        [Tooltip("Enable this to override the default fading settings with custom values.")]
        public bool overrideFading;
        [Tooltip("Set the distance over which the light fades based on depth.")]
        [Min(0f)] public float depthFadeDistance = 5f;
        [Tooltip("Set the distance over which the light fades relative to the camera position.")]
        [Min(0f)] public float cameraFadeDistance = 5f;
        [Tooltip("Control the strength of Fresnel-based fading effect.")]
        [Min(0f)] public float fresnelFadeStrength = 1f;

        // FOG SETTINGS
        [Tooltip("Enable this to apply a texture to the light for fog-like effects.")]
        public bool useFogTexture;

        [Tooltip("Specify the texture used to mask the light for fog effects. The red channel of this texture is used to mask the light.")]
        public Texture2D fogTexture;
        [Tooltip("Set the tiling factor for the fog texture.")]
        public float fogTextureTiling = 1;
        [Tooltip("Adjust the opacity of the fog effect.")]
        [Range(0f, 1f)] public float fogOpacity = 1f;
        [Tooltip("Set the vertical scrolling speed for the fog texture.")]
        public float verticalScrollingSpeed;
        [Tooltip("Set the horizontal scrolling speed for the fog texture.")]
        public float horizontalScrollingSpeed;

        // PROPERTIES
        public Vector2 FogScrollingSpeed => new Vector2(horizontalScrollingSpeed, verticalScrollingSpeed);

        public LightSettings()
        {

        }

        public LightSettings(LightSettings profile)
        {
            this.init = true;
            this.blendingMode = profile.blendingMode;
            this.alpha = profile.alpha;
            this.overrideLightColor = profile.overrideLightColor;
            this.newColor = profile.newColor;
            this.newIntensity = profile.newIntensity;
            this.intensityMultiplier = profile.intensityMultiplier;
            this.useCustomFade = profile.useCustomFade;
            this.fadeGradient = profile.fadeGradient;
            this.fadeTexture = profile.fadeTexture;
            this.overrideFading = profile.overrideFading;
            this.depthFadeDistance = profile.depthFadeDistance;
            this.cameraFadeDistance = profile.cameraFadeDistance;
            this.fresnelFadeStrength = profile.fresnelFadeStrength;
            this.useFogTexture = profile.useFogTexture;
            this.fogTexture = profile.fogTexture;
            this.fogTextureTiling = profile.fogTextureTiling;
            this.fogOpacity = profile.fogOpacity;
            this.verticalScrollingSpeed = profile.verticalScrollingSpeed;
            this.horizontalScrollingSpeed = profile.horizontalScrollingSpeed;
        }

        public LightSettings(VolumetricLightProfile profile)
        {
            this.init = true;
            this.blendingMode = profile.blendingMode;
            this.alpha = profile.alpha;
            this.overrideLightColor= profile.overrideLightColor;
            this.newColor = profile.newColor;
            this.newIntensity = profile.newIntensity;
            this.intensityMultiplier = profile.intensityMultiplier;
            this.useCustomFade= profile.useCustomFade;
            this.fadeGradient = profile.fadeGradient;
            this.fadeTexture= profile.fadeTexture;
            this.overrideFading= profile.overrideFading;
            this.depthFadeDistance = profile.depthFadeDistance;
            this.cameraFadeDistance = profile.cameraFadeDistance;
            this.fresnelFadeStrength = profile.fresnelFadeStrength;
            this.useFogTexture= profile.useFogTexture;
            this.fogTexture= profile.fogTexture;
            this.fogTextureTiling = profile.fogTextureTiling;
            this.fogOpacity= profile.fogOpacity;
            this.verticalScrollingSpeed= profile.verticalScrollingSpeed;
            this.horizontalScrollingSpeed = profile.horizontalScrollingSpeed;
        }

        public Texture2D FadeTexture
        {
            get
            {
                if (fadeTexture == null)
                {
                    GenerateTexture(textureResolution);
                }
                return fadeTexture;
            }
        }

        public void ResetFadeTexture()
        {
            fadeTexture = null;
        }


        public void GenerateTexture(int textureResolution)
        {
            Color[] pixels = new Color[textureResolution];
            float step = 1f / (textureResolution - 1);
            for (int x = 0; x < pixels.Length; x++)
            {
                pixels[x] = fadeGradient.Evaluate(step * x);
            }

            // create a new texture, if we have to.
            if (fadeTexture == null)
                fadeTexture = new Texture2D(textureResolution, 1);
            if (fadeTexture.width != textureResolution)
                fadeTexture = new Texture2D(textureResolution, 1);

            fadeTexture.SetPixels(pixels);
            fadeTexture.Apply();
        }
    }
}