using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

namespace Pinwheel.Beam
{
    /// <summary>
    /// Contain additional data and settings for rendering volumetric effect on lights.
    /// Must be added to a Light object.
    /// </summary>
    [ExecuteInEditMode]
    [RequireComponent(typeof(Light))]
    public class VolumetricRealtimeLight : MonoBehaviour
    {
        protected static Dictionary<Light, VolumetricRealtimeLight> s_ActiveLights = new Dictionary<Light, VolumetricRealtimeLight>();

        [SerializeField]
        protected bool m_Excluded;
        /// <summary>
        /// Exclude this light from volumetric effect. E.g: it still cast light to nearby surface but won't have halo or beam.
        /// </summary>
        public bool excluded
        {
            get
            {
                return m_Excluded;
            }
            set
            {
                m_Excluded = value;
            }
        }

        [SerializeField]
        [HideInInspector]
        protected Light m_Light;

        protected void OnEnable()
        {
            m_Light = GetComponent<Light>();
            AddToActiveLights();
        }

        protected void OnDisable()
        {
            RemoveFromActiveLights();
        }

        protected void AddToActiveLights()
        {
            s_ActiveLights.Add(m_Light, this);
        }

        protected void RemoveFromActiveLights()
        {
            s_ActiveLights.Remove(m_Light);
        }

        /// <summary>
        /// Retrieve a <c>Volumetric Realtime Light</c> component attached to a Light object. 
        /// Generally faster than regular GetComponent()
        /// </summary>
        /// <param name="light">The Light object.</param>
        /// <returns>The Volumetric Realtime Light component, null if not attached.</returns>
        public static VolumetricRealtimeLight GetForLight(Light light)
        {
            if (light == null)
                throw new ArgumentNullException(nameof(light));

            if (s_ActiveLights == null)
                return null;
            VolumetricRealtimeLight vrl;
            s_ActiveLights.TryGetValue(light, out vrl);
            return vrl;
        }
    }
}
