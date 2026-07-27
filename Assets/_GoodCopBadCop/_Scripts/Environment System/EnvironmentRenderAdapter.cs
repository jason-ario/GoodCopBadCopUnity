using System;
using R3;
using UnityEngine;
using VContainer.Unity;
using VolumetricFogAndMist2;

namespace GoodCopBadCop.EnvironmentSystem
{
    /// <summary>
    /// Applies the current day preset to RenderSettings/VolumetricFog directly, with no
    /// day/night lerping or blending. Whatever preset <see cref="IEnvironmentModel.CurrentPreset"/>
    /// reports is applied as-is; night presets and day/night progress are intentionally ignored.
    /// </summary>
    public sealed class EnvironmentRenderAdapter : IInitializable, IDisposable
    {
        private readonly IEnvironmentModel model;
        private readonly VolumetricFog volumetricFog;
        private DisposableBag disposables;

        public EnvironmentRenderAdapter(IEnvironmentModel model, VolumetricFog volumetricFog, EnvironmentSchedule schedule)
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

        private void ApplyPreset(EnvironmentPreset dayPreset)
        {
            if (dayPreset == null)
            {
                return;
            }

            RenderSettings.fogColor = dayPreset.fogColor;
            RenderSettings.fogDensity = dayPreset.fogDensity;
            RenderSettings.ambientSkyColor = dayPreset.ambientLighting.skyColor;
            RenderSettings.ambientEquatorColor = dayPreset.ambientLighting.equatorColor;
            RenderSettings.ambientGroundColor = dayPreset.ambientLighting.groundColor;
            RenderSettings.skybox = dayPreset.Skybox;

            ApplyVolumetricFog(dayPreset);
        }

        private void ApplyVolumetricFog(EnvironmentPreset dayPreset)
        {
            if (volumetricFog == null)
            {
                return;
            }

            volumetricFog.profile = dayPreset.volumetricFogProfile;
            volumetricFog.UpdateMaterialPropertiesNow();
        }
    }

}
