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
    /// than named tiers). Presets intentionally never touch LOD settings (lodBias /
    /// maximumLODLevel): LODGroups are authored against the project tier's lodBias, and
    /// overriding it at runtime made objects cull much closer than in the editor.
    /// Shadow distance/cascades are set on the active URP asset (URP ignores the QualitySettings
    /// equivalents) and restored on dispose so Editor play sessions don't dirty the asset.
    /// Brightness/Film Grain drive overrides on the always-on global Volume
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

        private UniversalRenderPipelineAsset urpAsset;
        private float originalShadowDistance;
        private int originalShadowCascadeCount;

        public GraphicsPreferencesApplier(ISettingsModel model, GraphicsPreferencesVolumeAnchor volumeAnchor)
        {
            this.model = model;
            this.volumeAnchor = volumeAnchor;
        }

        public void Initialize()
        {
            CacheRenderPipelineDefaults();
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
            RestoreRenderPipelineDefaults();
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

        private void CacheRenderPipelineDefaults()
        {
            urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset == null)
            {
                return;
            }

            originalShadowDistance = urpAsset.shadowDistance;
            originalShadowCascadeCount = urpAsset.shadowCascadeCount;
        }

        // The URP asset is a project asset: runtime edits would persist in the Editor, so restore them.
        private void RestoreRenderPipelineDefaults()
        {
            if (urpAsset == null)
            {
                return;
            }

            urpAsset.shadowDistance = originalShadowDistance;
            urpAsset.shadowCascadeCount = originalShadowCascadeCount;
        }

        private void ApplyQualityPreset(EQualityPreset preset)
        {
            float shadowDistance;
            int shadowCascades;

            switch (preset)
            {
                case EQualityPreset.Low:
                    shadowDistance = 20f;
                    shadowCascades = 1;
                    QualitySettings.globalTextureMipmapLimit = 2;
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                    QualitySettings.particleRaycastBudget = 64;
                    QualitySettings.realtimeReflectionProbes = false;
                    break;
                case EQualityPreset.Medium:
                    shadowDistance = 35f;
                    shadowCascades = 1;
                    QualitySettings.globalTextureMipmapLimit = 1;
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                    QualitySettings.particleRaycastBudget = 128;
                    QualitySettings.realtimeReflectionProbes = false;
                    break;
                case EQualityPreset.High:
                    shadowDistance = 50f;
                    shadowCascades = 1;
                    QualitySettings.globalTextureMipmapLimit = 0;
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                    QualitySettings.particleRaycastBudget = 256;
                    QualitySettings.realtimeReflectionProbes = true;
                    break;
                case EQualityPreset.Ultra:
                default:
                    shadowDistance = 70f;
                    shadowCascades = 2;
                    QualitySettings.globalTextureMipmapLimit = 0;
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                    QualitySettings.particleRaycastBudget = 512;
                    QualitySettings.realtimeReflectionProbes = true;
                    break;
            }

            // URP ignores QualitySettings.shadowDistance/shadowCascades; they live on the pipeline asset.
            if (urpAsset != null)
            {
                urpAsset.shadowDistance = shadowDistance;
                urpAsset.shadowCascadeCount = shadowCascades;
            }
        }
    }
}
