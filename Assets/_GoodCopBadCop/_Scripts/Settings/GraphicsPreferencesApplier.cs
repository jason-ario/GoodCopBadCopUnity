using System;
using R3;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VContainer.Unity;

namespace GoodCopBadCop.Settings
{
    /// <summary>
    /// Applies the Graphics-tab preferences (Quality Preset, Brightness, Film Grain) that have
    /// persisted backing (<see cref="ISettingsModel"/>) but previously had no runtime effect.
    /// Quality Preset adjusts global <see cref="QualitySettings"/> knobs directly (the project
    /// only defines a single "PC" quality tier, so presets are expressed as knob values rather
    /// than named tiers). Brightness/Film Grain drive overrides on the always-on global Volume
    /// identified by <see cref="GraphicsPreferencesVolumeAnchor"/>.
    /// </summary>
    public sealed class GraphicsPreferencesApplier : IInitializable, IDisposable
    {
        private const float MinExposure = -2f;
        private const float MaxExposure = 2f;
        private const float DefaultFilmGrainIntensity = 0.2f;

        private readonly ISettingsModel model;
        private readonly GraphicsPreferencesVolumeAnchor volumeAnchor;
        private DisposableBag disposables;

        private ColorAdjustments colorAdjustments;
        private FilmGrain filmGrain;
        private float filmGrainMaxIntensity = DefaultFilmGrainIntensity;

        public GraphicsPreferencesApplier(ISettingsModel model, GraphicsPreferencesVolumeAnchor volumeAnchor)
        {
            this.model = model;
            this.volumeAnchor = volumeAnchor;
        }

        public void Initialize()
        {
            model.QualityPreset.Subscribe(ApplyQualityPreset).AddTo(ref disposables);

            Volume volume = volumeAnchor != null ? volumeAnchor.Volume : null;
            VolumeProfile profile = volume != null ? volume.profile : null;
            if (profile == null)
            {
                Debug.LogWarning(
                    "[GraphicsPreferencesApplier] No Volume assigned via GraphicsPreferencesVolumeAnchor; " +
                    "Brightness/Film Grain will not be applied.");
                return;
            }

            if (!profile.TryGet(out colorAdjustments))
            {
                colorAdjustments = profile.Add<ColorAdjustments>(true);
            }
            colorAdjustments.active = true;
            colorAdjustments.postExposure.overrideState = true;

            if (!profile.TryGet(out filmGrain))
            {
                filmGrain = profile.Add<FilmGrain>(true);
            }
            filmGrain.active = true;
            filmGrain.intensity.overrideState = true;
            filmGrainMaxIntensity = filmGrain.intensity.value > 0f
                ? filmGrain.intensity.value
                : DefaultFilmGrainIntensity;

            model.Brightness.Subscribe(ApplyBrightness).AddTo(ref disposables);
            model.FilmGrainEnabled.Subscribe(ApplyFilmGrain).AddTo(ref disposables);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }

        private void ApplyBrightness(float value)
        {
            if (colorAdjustments == null)
            {
                return;
            }

            colorAdjustments.postExposure.value = Mathf.Lerp(MinExposure, MaxExposure, Mathf.Clamp01(value / 100f));
        }

        private void ApplyFilmGrain(bool isEnabled)
        {
            if (filmGrain == null)
            {
                return;
            }

            filmGrain.intensity.value = isEnabled ? filmGrainMaxIntensity : 0f;
        }

        private static void ApplyQualityPreset(EQualityPreset preset)
        {
            switch (preset)
            {
                case EQualityPreset.Low:
                    QualitySettings.shadowDistance = 15f;
                    QualitySettings.globalTextureMipmapLimit = 2;
                    QualitySettings.lodBias = 0.5f;
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                    QualitySettings.particleRaycastBudget = 64;
                    QualitySettings.softParticles = false;
                    QualitySettings.realtimeReflectionProbes = false;
                    break;
                case EQualityPreset.Medium:
                    QualitySettings.shadowDistance = 25f;
                    QualitySettings.globalTextureMipmapLimit = 1;
                    QualitySettings.lodBias = 0.8f;
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                    QualitySettings.particleRaycastBudget = 128;
                    QualitySettings.softParticles = false;
                    QualitySettings.realtimeReflectionProbes = false;
                    break;
                case EQualityPreset.High:
                    QualitySettings.shadowDistance = 40f;
                    QualitySettings.globalTextureMipmapLimit = 0;
                    QualitySettings.lodBias = 1f;
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                    QualitySettings.particleRaycastBudget = 256;
                    QualitySettings.softParticles = true;
                    QualitySettings.realtimeReflectionProbes = true;
                    break;
                case EQualityPreset.Ultra:
                default:
                    QualitySettings.shadowDistance = 60f;
                    QualitySettings.globalTextureMipmapLimit = 0;
                    QualitySettings.lodBias = 1.5f;
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                    QualitySettings.particleRaycastBudget = 512;
                    QualitySettings.softParticles = true;
                    QualitySettings.realtimeReflectionProbes = true;
                    break;
            }
        }
    }
}
