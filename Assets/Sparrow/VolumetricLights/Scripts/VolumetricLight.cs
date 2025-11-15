// 
// Copyright (c) 2023 Off The Beaten Track UG
// All rights reserved.
// 
// Maintainer: Jens Bahr
// 

using UnityEngine;
using UnityEngine.Serialization;

namespace Sparrow.VolumetricLight
{
    /*
     * Stores light source and profile, handles creation of supporting objects.
     */
    [RequireComponent(typeof(Light)), ExecuteAlways, SelectionBase, AddComponentMenu("Rendering/Volumetric Light")]
    public class VolumetricLight : MonoBehaviour
    {
        public static string version => "1.5.0";
        public static string hexColor => "#FA8231";
        public static string assetID => "185266";
        public static Color color
        {
            get
            {
                if (m_Color.Equals(Color.white))
                    ColorUtility.TryParseHtmlString(hexColor, out m_Color);
                return m_Color;
            }
        }
        private static Color m_Color = Color.white;

        public enum BlendingMode
        {
            Alpha,
            Additive
        }

        // SPOTLIGHT SETTINGS
        [Tooltip("Adjust the influence of the spotlight's angle on the light's appearance. Ranges from 0 to 1.")]
        [Range(0f, 1f)] public float spotAngleWeight = 1f;
        [Tooltip("Enable this to override the light's spot angle with a custom value.")]
        public bool overrideSpotAngle;
        [Tooltip("Set a new spot angle for the light when override is enabled. Ranges from 1 to 179 degrees.")]
        [Range(1f, 179f)] public float newSpotAngle = 90;

        [SerializeField, FormerlySerializedAs("profile")] VolumetricLightProfile m_LightProfile;
        [SerializeField] LightSettings m_Settings = new LightSettings();
        [SerializeField] LightVolume m_LightVolume;
        [SerializeField] Light m_LightSource;

        [SerializeField] bool m_ScaleWithObjectScale = false;

        public bool scaleWithObjectScale => m_ScaleWithObjectScale;

        public LightSettings settings => m_LightProfile == null ? localSettings : m_LightProfile.settings;

        public LightVolume Volume
        {
            get
            {
                // reset light volume if its a child of another, e.g. if light was copied
                if (m_LightVolume != null && !m_LightVolume.transform.IsChildOf(transform))
                    m_LightVolume = null;
                if (m_LightVolume != null) return m_LightVolume;
                m_LightVolume = GetComponentInChildren<LightVolume>();
                if (m_LightVolume != null) return m_LightVolume;
                AddLightVolume();
                return m_LightVolume;
            }
        }

        public Light lightSource
        {
            get
            {
                if (m_LightSource == null)
                {
                    m_LightSource = GetComponent<Light>();
                }

                return m_LightSource;
            }
        }

        public LightSettings localSettings
        {
            get
            {
                if (m_Settings == null)
                {
                    m_Settings = new LightSettings();
                    m_Settings.init = true;
                }
                return m_Settings;
            }
            set => m_Settings = value;
        }
        
        public VolumetricLightProfile lightProfile
        {
            get => m_LightProfile;
            set => m_LightProfile = value;
        }

        void Update()
        {
#if UNITY_EDITOR
            SetUpVolume(); // calling this to react to changes in the light settings
#endif
            UpdateVolumetricLight();
        }

        public void SetUpVolume()
        {
            Volume.RecalculateMesh();
        }
        public Color GetColor()
        {
            if (settings is null) return Color.white;
            Color col = settings.overrideLightColor
                ? settings.newColor * settings.newIntensity
                : lightSource.color * settings.intensityMultiplier * lightSource.intensity;
            col.a = settings.blendingMode == BlendingMode.Alpha ? settings.alpha : 1;
            return col;
        }
#if UNITY_EDITOR
        void OnEnable()
        {
            SetUpVolume();
        }
#endif
        void OnDestroy()
        {
#if UNITY_EDITOR
            DestroyImmediate(Volume.gameObject);
#else
            Destroy(Volume.gameObject);
#endif
        }

        public void UpdateVolumetricLight()
        {
            Volume.SendValuesToMaterial();
        }

        public void AddLightVolume()
        {
            // remove all child volumes
            var objs = transform.GetComponentsInChildren<LightVolume>();
            for (int i = objs.Length - 1; i >= 0; i--)
            {
#if UNITY_EDITOR
                DestroyImmediate(objs[i].gameObject);
#else
                Destroy(objs[i].gameObject);
#endif
            }

            // add new one
            GameObject obj = new GameObject("LightVolume");
            obj.transform.parent = transform;
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localScale = Vector3.one;
            obj.transform.localRotation = Quaternion.identity;

            obj.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;

            LightVolume vol = obj.AddComponent<LightVolume>();

            vol.Setup(this);

            m_LightVolume = vol;
        }
    }
}
