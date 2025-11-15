// 
// Copyright (c) 2023 Off The Beaten Track UG
// All rights reserved.
// 
// Maintainer: Jens Bahr
// 

using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sparrow.VolumetricLight
{
    /*
     * data storage of light properties, overrides by category
     */
    [CreateAssetMenu(menuName = "Sparrow/Volumetric Lights/Volumetric Light Profile", fileName = "Volumetric Light Profile")]
    public class VolumetricLightProfile : ScriptableObject
    {
        [SerializeField] LightSettings m_LightSettings = null;
        
        public LightSettings settings
        {
            get
            {
                if(m_LightSettings == null || m_LightSettings.init == false)
                {
                    m_LightSettings = new LightSettings(this);
                }
                return m_LightSettings;
            }
            set => m_LightSettings = value;
        }

        private void OnValidate()
        {
            CheckProfileSettings();
        } 

        public void CheckProfileSettings() { 
            if (m_LightSettings == null || m_LightSettings.init == false)
            {
                m_LightSettings = new LightSettings(this);
            }
        }

#if UNITY_EDITOR
        //adapted from VolumeProfileFactory.cs (renderpipeline-core)
        public static VolumetricLightProfile CreateProfile(Scene scene, string targetName)
        {
            string path;

            if (string.IsNullOrEmpty(scene.path))
            {
                path = "Assets/";
            }
            else
            {
                string scenePath = Path.GetDirectoryName(scene.path);
                string extPath = scene.name;
                string profilePath = scenePath + "/" + extPath;

                if (!AssetDatabase.IsValidFolder(profilePath))
                    AssetDatabase.CreateFolder(scenePath, extPath);

                path = profilePath + "/";
            }

            path += targetName + " Profile.asset";
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            VolumetricLightProfile profile = CreateInstance<VolumetricLightProfile>();
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return profile;
        }
#endif

        // BACKWARDS COMPATIBILITY
        public VolumetricLight.BlendingMode blendingMode = VolumetricLight.BlendingMode.Alpha;
        public float alpha = 1f;
        public bool overrideLightColor;
        public Color newColor = Color.white;
        public float newIntensity = 1f;
        public float intensityMultiplier = 1f;
        public bool useCustomFade;
        public Gradient fadeGradient = new Gradient();
        public Texture2D fadeTexture;
        public bool overrideFading;
        public float depthFadeDistance = 5f;
        public float cameraFadeDistance = 5f;
        public float fresnelFadeStrength = 1f;
        public bool useFogTexture;
        public Texture2D fogTexture;
        public float fogTextureTiling = 1;
        public float fogOpacity = 1f;
        public float verticalScrollingSpeed;
        public float horizontalScrollingSpeed;
    }
}