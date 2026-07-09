using System.Collections.Generic;
using GoodCopBadCop.Audio;
using GoodCopBadCop.CameraSystem;
using UnityEngine;

namespace GoodCopBadCop.Effects
{
    public interface IEffectService
    {
        void Play(EffectPreset preset);
        void Play(EffectPreset preset, EffectContext context);
        bool PlayByKey(string key);
        bool PlayByKey(string key, EffectContext context);
    }

    public sealed class EffectService : IEffectService
    {
        private readonly IEffectCatalog catalog;
        private readonly IFullscreenEffectService fullscreenEffectService;
        private readonly ICameraService cameraService;
        private readonly IAudioService audioService;
        private readonly Dictionary<string, float> lastPlayedByKey = new Dictionary<string, float>();

        public EffectService(
            IEffectCatalog catalog,
            IFullscreenEffectService fullscreenEffectService,
            ICameraService cameraService,
            IAudioService audioService)
        {
            this.catalog = catalog;
            this.fullscreenEffectService = fullscreenEffectService;
            this.cameraService = cameraService;
            this.audioService = audioService;
        }

        public void Play(EffectPreset preset)
        {
            Play(preset, EffectContext.Default);
        }

        public void Play(EffectPreset preset, EffectContext context)
        {
            if (preset == null)
                return;

            if (!CanPlay(preset))
                return;

            fullscreenEffectService.Play(preset.Fullscreen, context);
            PlayCamera(preset.Camera);
            PlayAudio(preset.Audio, context);
        }

        public bool PlayByKey(string key)
        {
            return PlayByKey(key, EffectContext.Default);
        }

        public bool PlayByKey(string key, EffectContext context)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (!catalog.TryGet(key, out EffectPreset preset))
            {
                Debug.LogWarning($"[EffectService] Effect key '{key}' was not found.");
                return false;
            }

            Play(preset, context);
            return true;
        }

        private bool CanPlay(EffectPreset preset)
        {
            string key = preset.Key;
            if (string.IsNullOrWhiteSpace(key) || preset.MinInterval <= 0f)
                return true;

            float now = Time.unscaledTime;
            if (lastPlayedByKey.TryGetValue(key, out float lastPlayed) &&
                now - lastPlayed < preset.MinInterval)
            {
                return false;
            }

            lastPlayedByKey[key] = now;
            return true;
        }

        private void PlayCamera(CameraEffectSettings settings)
        {
            if (settings == null || !settings.Enabled)
                return;

            cameraService.PlayLocalImpulse(settings.LocalImpulse);
            cameraService.PlayLocalSway(settings.LocalSway);
            cameraService.PlayLocalDamageFeedback(settings.LocalDamage);
        }

        private void PlayAudio(AudioEffectSettings settings, EffectContext context)
        {
            if (settings == null || !settings.Enabled || settings.Clips == null || settings.Clips.Count == 0)
                return;

            AudioClip clip = settings.Clips[Random.Range(0, settings.Clips.Count)];
            if (clip == null)
                return;

            float minPitch = Mathf.Min(settings.PitchRange.x, settings.PitchRange.y);
            float maxPitch = Mathf.Max(settings.PitchRange.x, settings.PitchRange.y);
            float pitch = Mathf.Approximately(minPitch, maxPitch)
                ? minPitch
                : Random.Range(minPitch, maxPitch);

            if (settings.PlayAtWorldPosition && context.WorldPosition.HasValue)
            {
                audioService.PlaySfxAtPosition(
                    clip,
                    context.WorldPosition.Value,
                    settings.Volume,
                    pitch,
                    settings.MaxDistance);
                return;
            }

            audioService.PlaySfx(clip, settings.Volume, pitch);
        }
    }
}
