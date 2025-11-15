using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

namespace Pinwheel.Beam
{
    /// <summary>
    /// Cubic volumetric fog volume that can be placed anywhere in the scene.
    /// </summary>
    [AddComponentMenu("")]
    [ExecuteInEditMode]    
    public class VolumetricFog : MonoBehaviour
    {
        protected static List<VolumetricFog> s_AllInstances = new List<VolumetricFog>();

        [SerializeField]
        protected Color m_Color;
        /// <summary>
        /// Tint color of the fog
        /// </summary>
        public Color color
        {
            get
            {
                return m_Color;
            }
            set
            {
                m_Color = value;
            }
        }

        [SerializeField]
        protected float m_Density;
        /// <summary>
        /// Fog density
        /// </summary>
        public float density
        {
            get
            {
                return m_Density;
            }
            set
            {
                m_Density = value;
            }
        }

        private void OnEnable()
        {
            s_AllInstances.Add(this);
        }

        private void OnDisable()
        {
            s_AllInstances.Remove(this);
        }

        private void Reset()
        {
            m_Color = Color.white;
            m_Density = 1;
        }

        /// <summary>
        /// Get all active volumes in the scene
        /// </summary>
        /// <param name="volumes">A pre-allocated list to contain the volumes</param>
        public static void GetAllVolumes(List<VolumetricFog> volumes)
        {
            volumes.Clear();
            volumes.AddRange(s_AllInstances);
        }

        /// <summary>
        /// Get all active volumes that is visible by a camera.
        /// </summary>
        /// <param name="frustumPlanes">Camera's frustum plane</param>
        /// <param name="volumes">A pre-allocated list to contain the volumes</param>
        public static void GetAllVisibleVolume(Plane[] frustumPlanes, List<VolumetricFog> volumes)
        {
            volumes.Clear();
            foreach (VolumetricFog vf in s_AllInstances)
            {
                if (VolumeUtilities.IsVisible(frustumPlanes, vf.transform.localToWorldMatrix))
                {
                    volumes.Add(vf);
                }
            }
        }
    }
}
