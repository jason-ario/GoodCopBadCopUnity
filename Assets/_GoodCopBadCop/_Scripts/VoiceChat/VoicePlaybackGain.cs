using UnityEngine;

namespace GoodCopBadCop.VoiceChat
{
    /// <summary>
    /// Post-decode gain stage for Dissonance voice playback. Must sit after Dissonance's
    /// SamplePlaybackComponent on the playback prefab so it processes the decoded voice signal.
    /// Dissonance caps RemoteVoiceVolume at 1.0, so this is the only way to boost voice above unity.
    /// Boosted peaks are caught by a peak limiter (instant attack, smooth release) that scales the
    /// whole frame instead of waveshaping individual samples, so voices stay clean rather than
    /// saturated/crunchy.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoicePlaybackGain : MonoBehaviour
    {
        /// <summary>Linear gain applied when the voice volume setting is at 100%.</summary>
        public const float MaxGain = 3f;

        private const float LimiterCeiling = 0.95f;
        private const float ReleaseSeconds = 0.15f;
        private const float GainSmoothingSeconds = 0.05f;

        private static volatile float globalGain = 2.4f;

        private float releaseCoefficient;
        private float gainSmoothingCoefficient;
        private float envelope;
        private float currentGain = -1f;

        /// <summary>Sets voice gain from a 0-100 settings value.</summary>
        public static void SetVolumePercent(float percent)
        {
            globalGain = Mathf.Clamp01(percent / 100f) * MaxGain;
        }

        private void Awake()
        {
            // AudioSettings must be read on the main thread; OnAudioFilterRead runs on the audio thread.
            int sampleRate = Mathf.Max(1, AudioSettings.outputSampleRate);
            releaseCoefficient = Mathf.Exp(-1f / (ReleaseSeconds * sampleRate));
            gainSmoothingCoefficient = Mathf.Exp(-1f / (GainSmoothingSeconds * sampleRate));
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float targetGain = globalGain;
            if (currentGain < 0f)
            {
                currentGain = targetGain;
            }

            if (Mathf.Approximately(targetGain, 1f) && Mathf.Approximately(currentGain, 1f) && envelope <= 0f)
            {
                return;
            }

            if (channels < 1)
            {
                channels = 1;
            }

            float gain = currentGain;
            float env = envelope;
            for (int frame = 0; frame + channels <= data.Length; frame += channels)
            {
                // Smooth slider changes to avoid zipper noise.
                gain = targetGain + (gain - targetGain) * gainSmoothingCoefficient;

                float peak = 0f;
                for (int c = 0; c < channels; c++)
                {
                    float magnitude = data[frame + c] * gain;
                    if (magnitude < 0f)
                    {
                        magnitude = -magnitude;
                    }

                    if (magnitude > peak)
                    {
                        peak = magnitude;
                    }
                }

                env = peak > env ? peak : env * releaseCoefficient;
                float reduction = env > LimiterCeiling ? LimiterCeiling / env : 1f;
                float frameGain = gain * reduction;

                for (int c = 0; c < channels; c++)
                {
                    data[frame + c] *= frameGain;
                }
            }

            currentGain = gain;
            envelope = env < 1e-6f ? 0f : env;
        }
    }
}
