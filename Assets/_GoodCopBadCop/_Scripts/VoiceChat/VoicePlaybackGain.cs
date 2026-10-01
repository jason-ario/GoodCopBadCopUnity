using UnityEngine;

namespace GoodCopBadCop.VoiceChat
{
    /// <summary>
    /// Post-decode gain stage for Dissonance voice playback. Must sit after Dissonance's
    /// SamplePlaybackComponent on the playback prefab so it processes the decoded voice signal.
    /// Dissonance caps RemoteVoiceVolume at 1.0, so this is the only way to boost voice above unity.
    /// A soft limiter prevents boosted peaks from hard-clipping.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoicePlaybackGain : MonoBehaviour
    {
        /// <summary>Linear gain applied when the voice volume setting is at 100%.</summary>
        public const float MaxGain = 3f;

        private const float LimiterKnee = 0.6f;

        private static volatile float globalGain = 2.4f;

        /// <summary>Sets voice gain from a 0-100 settings value.</summary>
        public static void SetVolumePercent(float percent)
        {
            globalGain = Mathf.Clamp01(percent / 100f) * MaxGain;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float gain = globalGain;
            if (Mathf.Approximately(gain, 1f))
            {
                return;
            }

            const float range = 1f - LimiterKnee;
            for (int i = 0; i < data.Length; i++)
            {
                float sample = data[i] * gain;
                float magnitude = sample < 0f ? -sample : sample;
                if (magnitude > LimiterKnee)
                {
                    float limited = LimiterKnee + range * Tanh((magnitude - LimiterKnee) / range);
                    sample = sample < 0f ? -limited : limited;
                }

                data[i] = sample;
            }
        }

        private static float Tanh(float x)
        {
            float e = Mathf.Exp(2f * x);
            return (e - 1f) / (e + 1f);
        }
    }
}
