using System.Collections.Generic;
using UnityEngine;

namespace GoodCopBadCop.Audio
{
    public interface IAudioService
    {
        bool IsDeadSilenceActive { get; }
        void SetDeadSilence(object source, bool active);
        void PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f);
        void PlaySfxAtPosition(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f, float maxDistance = 5f);
    }

    public sealed class AudioService : IAudioService
    {
        private readonly HashSet<object> deadSilenceSources = new HashSet<object>();

        public bool IsDeadSilenceActive { get; private set; }

        public void SetDeadSilence(object source, bool active)
        {
            if (source == null)
            {
                Debug.LogWarning("[AudioService] Dead silence source cannot be null.");
                return;
            }

            if (active)
            {
                deadSilenceSources.Add(source);
            }
            else
            {
                deadSilenceSources.Remove(source);
            }

            ApplyDeadSilence(deadSilenceSources.Count > 0);
        }

        public void PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (global::SFXController.Instance == null)
            {
                Debug.LogWarning("[AudioService] SFXController.Instance is not available.");
                return;
            }

            global::SFXController.Instance.Play(clip, volume, pitch);
        }

        public void PlaySfxAtPosition(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f, float maxDistance = 5f)
        {
            if (global::SFXController.Instance == null)
            {
                Debug.LogWarning("[AudioService] SFXController.Instance is not available.");
                return;
            }

            global::SFXController.Instance.PlayAtPosition(clip, position, volume, pitch, maxDistance);
        }

        private void ApplyDeadSilence(bool active)
        {
            if (IsDeadSilenceActive == active)
            {
                return;
            }

            IsDeadSilenceActive = active;

            if (global::AudioManager.Instance == null)
            {
                Debug.LogWarning("[AudioService] AudioManager.Instance is not available.");
                return;
            }

            if (active)
            {
                global::AudioManager.Instance.FadeOutAmbientAudio();
            }
            else
            {
                global::AudioManager.Instance.StartAmbientAudio();
            }
        }
    }
}