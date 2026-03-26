using System;
using UnityEngine;
using VolumetricFogAndMist2;

public class BackgroundManager : MonoBehaviour
{
    [SerializeField] private Environment[] _environments;
    [SerializeField] private VolumetricFog _volumetricFog;

    private void Start()
    {
        SetEnvironment(_environments[0]);
    }

    public void SetEnvironment(Environment environment)
    {
        RenderSettings.fogColor = environment.fogColor;
        _volumetricFog.profile = environment.volumetricFogProfile;
        RenderSettings.skybox = environment.Skybox;
        RenderSettings.fogDensity = environment.fogDensity;
        RenderSettings.ambientSkyColor = environment.ambientLighting.skyColor;
        RenderSettings.ambientEquatorColor = environment.ambientLighting.equatorColor;
        RenderSettings.ambientGroundColor = environment.ambientLighting.groundColor;

    }
}
