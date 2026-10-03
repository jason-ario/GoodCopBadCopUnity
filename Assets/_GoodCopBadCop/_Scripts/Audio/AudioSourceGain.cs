using UnityEngine;

/// <summary>
/// Per-source gain stage that boosts an AudioSource above unity. AudioSource.volume is clamped
/// to 0-1, so quiet looping clips can't be made louder through it. Place this component BELOW
/// the AudioSource on the same GameObject so it processes that source's output.
/// A soft limiter keeps boosted peaks from hard-clipping.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class AudioSourceGain : MonoBehaviour
{
    private const float LimiterKnee = 0.6f;

    [Tooltip("Linear gain applied on top of the AudioSource volume. 2 = roughly +6 dB.")]
    [Range(1f, 8f)]
    [SerializeField] private float _gain = 2.5f;

    // Cached for the audio thread (serialized fields shouldn't be read off the main thread).
    private volatile float _threadGain = 1f;

    public float Gain
    {
        get => _gain;
        set { _gain = Mathf.Max(0f, value); _threadGain = _gain; }
    }

    private void Awake() => _threadGain = _gain;

    private void OnValidate() => _threadGain = _gain;

    private void OnAudioFilterRead(float[] data, int channels)
    {
        float gain = _threadGain;
        if (Mathf.Approximately(gain, 1f))
            return;

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
