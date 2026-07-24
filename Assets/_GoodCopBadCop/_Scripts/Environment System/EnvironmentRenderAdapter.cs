using System;
using R3;
using UnityEngine;
using VContainer.Unity;
using VolumetricFogAndMist2;

namespace GoodCopBadCop.EnvironmentSystem
{
    /// <summary>
    /// Applies the current day preset to RenderSettings/VolumetricFog and, when a night preset
    /// is configured for the day, progressively lerps toward it as
    /// <see cref="IEnvironmentModel.DayNightProgress"/> advances (driven by
    /// <see cref="EnvironmentSuspectProgressAdapter"/> as suspects are processed).
    /// The blend itself is smoothed every frame via <see cref="Tick"/> so each step forward
    /// reads as a progressive lerp rather than an instant cut.
    /// </summary>
    public sealed class EnvironmentRenderAdapter : IInitializable, IDisposable, ITickable
    {
        private readonly IEnvironmentModel model;
        private readonly VolumetricFog volumetricFog;
        private readonly float blendSecondsToTarget;
        private DisposableBag disposables;

        private EnvironmentPreset dayPreset;
        private EnvironmentPreset nightPreset;
        private float targetProgress;
        private float currentProgress;
        private VolumetricFogProfile blendedFogProfile;
        private Material appliedSkybox;

        public EnvironmentRenderAdapter(IEnvironmentModel model, VolumetricFog volumetricFog, EnvironmentSchedule schedule)
        {
            this.model = model;
            this.volumetricFog = volumetricFog;
            blendSecondsToTarget = schedule != null ? Mathf.Max(0.01f, schedule.DayNightBlendSeconds) : 1.5f;
        }

        public void Initialize()
        {
            // Snaps the blend back to the day preset immediately whenever a new day is applied,
            // before the day/night preset subscriptions below re-apply RenderSettings.
            model.CurrentDay
                .Subscribe(_ =>
                {
                    currentProgress = 0f;
                    ApplyBlend();
                })
                .AddTo(ref disposables);

            model.CurrentPreset
                .Subscribe(preset =>
                {
                    dayPreset = preset;
                    ApplyBlend();
                })
                .AddTo(ref disposables);

            model.CurrentNightPreset
                .Subscribe(preset =>
                {
                    nightPreset = preset;
                    ApplyBlend();
                })
                .AddTo(ref disposables);

            model.DayNightProgress
                .Subscribe(progress => targetProgress = Mathf.Clamp01(progress))
                .AddTo(ref disposables);
        }

        public void Dispose()
        {
            disposables.Dispose();

            if (blendedFogProfile != null)
            {
                UnityEngine.Object.Destroy(blendedFogProfile);
                blendedFogProfile = null;
            }
        }

        public void Tick()
        {
            if (Mathf.Approximately(currentProgress, targetProgress))
            {
                return;
            }

            float step = Time.unscaledDeltaTime / blendSecondsToTarget;
            currentProgress = Mathf.MoveTowards(currentProgress, targetProgress, step);
            ApplyBlend();
        }

        private void ApplyBlend()
        {
            if (dayPreset == null)
            {
                return;
            }

            // With no night preset configured for the day, behave exactly like before:
            // stay fixed on the day preset regardless of suspect progress.
            float t = nightPreset == null ? 0f : currentProgress;

            RenderSettings.fogColor = nightPreset == null
                ? dayPreset.fogColor
                : Color.Lerp(dayPreset.fogColor, nightPreset.fogColor, t);

            RenderSettings.fogDensity = nightPreset == null
                ? dayPreset.fogDensity
                : Mathf.Lerp(dayPreset.fogDensity, nightPreset.fogDensity, t);

            RenderSettings.ambientSkyColor = nightPreset == null
                ? dayPreset.ambientLighting.skyColor
                : Color.Lerp(dayPreset.ambientLighting.skyColor, nightPreset.ambientLighting.skyColor, t);

            RenderSettings.ambientEquatorColor = nightPreset == null
                ? dayPreset.ambientLighting.equatorColor
                : Color.Lerp(dayPreset.ambientLighting.equatorColor, nightPreset.ambientLighting.equatorColor, t);

            RenderSettings.ambientGroundColor = nightPreset == null
                ? dayPreset.ambientLighting.groundColor
                : Color.Lerp(dayPreset.ambientLighting.groundColor, nightPreset.ambientLighting.groundColor, t);

            ApplySkybox(t);
            ApplyVolumetricFog(t);
        }

        private void ApplySkybox(float t)
        {
            // Skybox materials can't be blended generically, so the swap happens once the
            // lerp crosses the midpoint rather than at the very start/end of the shift.
            Material targetSkybox = (nightPreset == null || t < 0.5f)
                ? dayPreset.Skybox
                : nightPreset.Skybox;

            if (targetSkybox == appliedSkybox)
            {
                return;
            }

            appliedSkybox = targetSkybox;
            RenderSettings.skybox = targetSkybox;
        }

        private void ApplyVolumetricFog(float t)
        {
            if (volumetricFog == null)
            {
                return;
            }

            VolumetricFogProfile dayFog = dayPreset.volumetricFogProfile;
            VolumetricFogProfile nightFog = nightPreset != null ? nightPreset.volumetricFogProfile : null;

            if (dayFog == null || nightFog == null)
            {
                volumetricFog.profile = nightFog != null && t >= 0.5f ? nightFog : dayFog;
            }
            else
            {
                if (blendedFogProfile == null)
                {
                    blendedFogProfile = ScriptableObject.CreateInstance<VolumetricFogProfile>();
                }

                blendedFogProfile.Lerp(dayFog, nightFog, t);
                volumetricFog.profile = blendedFogProfile;
            }

            volumetricFog.UpdateMaterialPropertiesNow();
        }
    }

}
