using GoodCopBadCop.EnvironmentSystem;
using UnityEngine;
using VolumetricFogAndMist2;

public class BackgroundManager : MonoBehaviour
{
    [SerializeField] private EnvironmentPreset[] _environments;
    [SerializeField] private VolumetricFog _volumetricFog;

    public void SetEnvironment(EnvironmentPreset environment)
    {
        if (environment == null)
        {
            return;
        }

        RenderSettings.fogColor = environment.fogColor;
        if (_volumetricFog != null)
        {
            _volumetricFog.profile = environment.volumetricFogProfile;
        }

        RenderSettings.skybox = environment.Skybox;
        RenderSettings.fogDensity = environment.fogDensity;
        RenderSettings.ambientSkyColor = environment.ambientLighting.skyColor;
        RenderSettings.ambientEquatorColor = environment.ambientLighting.equatorColor;
        RenderSettings.ambientGroundColor = environment.ambientLighting.groundColor;
    }
}
