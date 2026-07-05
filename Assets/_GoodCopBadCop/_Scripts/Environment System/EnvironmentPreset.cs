using UnityEngine;
using VolumetricFogAndMist2;

namespace GoodCopBadCop.EnvironmentSystem
{
    [CreateAssetMenu(fileName = "EnvironmentPreset", menuName = "GoodCopBadCop/Environment Preset")]
    public class EnvironmentPreset : ScriptableObject
    {
        public Color fogColor;
        public VolumetricFogProfile volumetricFogProfile;
        public Material Skybox;
        public float fogDensity;

        [System.Serializable]
        public struct AmbientLighting
        {
            public Color skyColor;
            public Color equatorColor;
            public Color groundColor;
        }

        public AmbientLighting ambientLighting;
    }

}
