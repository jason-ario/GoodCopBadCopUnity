using System;
using R3;
using UnityEngine;
using VContainer.Unity;
using VolumetricFogAndMist2;

public sealed class EnvironmentRenderAdapter : IInitializable, IDisposable
{
    private readonly IEnvironmentModel model;
    private readonly VolumetricFog volumetricFog;
    private DisposableBag disposables;

    public EnvironmentRenderAdapter(IEnvironmentModel model, VolumetricFog volumetricFog)
    {
        this.model = model;
        this.volumetricFog = volumetricFog;
    }

    public void Initialize()
    {
        model.CurrentPreset
            .Subscribe(ApplyPreset)
            .AddTo(ref disposables);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }

    private void ApplyPreset(EnvironmentPreset preset)
    {
        if (preset == null)
        {
            return;
        }

        RenderSettings.fogColor = preset.fogColor;
        RenderSettings.skybox = preset.Skybox;
        RenderSettings.fogDensity = preset.fogDensity;
        RenderSettings.ambientSkyColor = preset.ambientLighting.skyColor;
        RenderSettings.ambientEquatorColor = preset.ambientLighting.equatorColor;
        RenderSettings.ambientGroundColor = preset.ambientLighting.groundColor;

        if (volumetricFog != null)
        {
            volumetricFog.profile = preset.volumetricFogProfile;
            volumetricFog.UpdateMaterialPropertiesNow();
        }
    }
}
