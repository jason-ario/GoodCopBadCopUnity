using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Military teleprinter / teletype sound for a <see cref="TMPTextReveal"/>: one tight, uniform
/// mechanical strike per printed character, silence on whitespace, and a carriage-return
/// clatter on each line break.
///
/// The raw typewriter clips have 50-180 ms of lead-in silence and a second key-release click,
/// so played as-is they never line up with the characters and their tails smear together.
/// At runtime each key clip is trimmed to just its strike transient (found from the peak),
/// peak-normalised, and given a short fade-out, so every character gets an identical, crisp
/// tick. All sound plays through a dedicated 2D source that is cut instantly when the reveal
/// is skipped/interrupted. Leave the reveal's own <c>revealSounds</c> empty when using this.
/// </summary>
[RequireComponent(typeof(TMPTextReveal))]
public class TeleprinterRevealAudio : MonoBehaviour
{
    [Header("Clips")]
    [Tooltip("Key-strike clips. Each is trimmed at runtime to its loudest transient. Clips must use Decompress On Load.")]
    [SerializeField] private AudioClip[] keyClips;

    [Tooltip("Played (untrimmed) when the printer crosses a line break.")]
    [SerializeField] private AudioClip carriageReturnClip;

    [Header("Strike Shaping")]
    [Tooltip("Length of each trimmed strike in seconds. Shorter = tighter, more machine-like.")]
    [SerializeField] private float strikeLength = 0.07f;

    [Tooltip("Seconds kept before the detected onset so the attack isn't clipped.")]
    [SerializeField] private float preRoll = 0.004f;

    [Tooltip("Fraction of the peak that counts as the strike onset.")]
    [Range(0.05f, 0.9f)]
    [SerializeField] private float onsetThreshold = 0.25f;

    [Header("Playback")]
    [Range(0f, 1f)] [SerializeField] private float keyVolume = 0.55f;
    [Range(0f, 1f)] [SerializeField] private float carriageReturnVolume = 0.45f;

    [Tooltip("Random ± pitch variation per strike. Keep small — machines are uniform.")]
    [Range(0f, 0.2f)] [SerializeField] private float pitchJitter = 0.03f;

    [Tooltip("Random volume reduction per strike (0-1).")]
    [Range(0f, 0.5f)] [SerializeField] private float volumeJitter = 0.12f;

    [Tooltip("Silence spaces and other whitespace, like a printer head advancing without striking.")]
    [SerializeField] private bool silentWhitespace = true;

    [Header("Old Recording Filter")]
    [Tooltip("Band-limit, distort and add grit so the printer sounds like an old recording / transmission.")]
    [SerializeField] private bool oldRecordingFilter = true;

    [Tooltip("Cuts lows below this frequency (Hz) — thin, small-speaker sound.")]
    [SerializeField] private float highPassCutoff = 300f;

    [Tooltip("Cuts highs above this frequency (Hz) — band-limited radio sound (AM radio ≈ 4-5 kHz).")]
    [SerializeField] private float lowPassCutoff = 4500f;

    [Tooltip("Resonance at the band edges. Slightly above 1 gives a boxy, 'telephone' honk.")]
    [Range(1f, 4f)] [SerializeField] private float bandResonance = 1.1f;

    [Tooltip("Real-time saturation amount.")]
    [Range(0f, 0.9f)] [SerializeField] private float distortion = 0.1f;

    [Tooltip("Bit depth baked into the clips. Lower = crunchier. 16 = off.")]
    [Range(4, 16)] [SerializeField] private int bitDepth = 12;

    [Tooltip("Hiss baked into each sound (linear amplitude). 0 = off.")]
    [Range(0f, 0.1f)] [SerializeField] private float hissLevel = 0.006f;

    private TMPTextReveal _reveal;
    private AudioSource _source;
    private readonly List<AudioClip> _strikes = new();
    private AudioClip _carriageReturn;
    private bool _built;
    private int _lastStrikeIndex = -1;

    private void Awake()
    {
        _reveal = GetComponent<TMPTextReveal>();

        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;
        _source.spatialBlend = 0f;
        _source.priority = 64;
        GoodCopBadCop.Audio.GameAudioMixer.Route(_source);

        // Filters on this GameObject only process this object's source, so the rest of the
        // game's SFX are untouched (unlike a mixer-group effect).
        if (oldRecordingFilter)
        {
            var highPass = gameObject.AddComponent<AudioHighPassFilter>();
            highPass.cutoffFrequency = highPassCutoff;
            highPass.highpassResonanceQ = bandResonance;

            var lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = lowPassCutoff;
            lowPass.lowpassResonanceQ = bandResonance;

            if (distortion > 0f)
            {
                var dist = gameObject.AddComponent<AudioDistortionFilter>();
                dist.distortionLevel = distortion;
            }
        }
    }

    private void OnEnable()
    {
        _reveal.CharacterRevealed += OnCharacterRevealed;
        _reveal.RevealInterrupted += CutSound;

        if (!_built)
            StartCoroutine(BuildStrikes());
    }

    private void OnDisable()
    {
        _reveal.CharacterRevealed -= OnCharacterRevealed;
        _reveal.RevealInterrupted -= CutSound;
        CutSound();
    }

    private void OnDestroy()
    {
        foreach (AudioClip strike in _strikes)
        {
            if (strike != null)
                Destroy(strike);
        }
        _strikes.Clear();

        if (_carriageReturn != null && _carriageReturn != carriageReturnClip)
            Destroy(_carriageReturn);
        _carriageReturn = null;
    }

    private void OnCharacterRevealed(char c, bool crossedLineBreak)
    {
        AudioClip carriage = _carriageReturn != null ? _carriageReturn : carriageReturnClip;
        if (crossedLineBreak && carriage != null)
        {
            _source.pitch = 1f;
            _source.Stop();
            _source.PlayOneShot(carriage, carriageReturnVolume);
        }

        if (c == '\n' || (silentWhitespace && char.IsWhiteSpace(c)) || _strikes.Count == 0)
            return;

        // Avoid the same strike twice in a row so the rhythm doesn't sound looped.
        int index = Random.Range(0, _strikes.Count);
        if (_strikes.Count > 1 && index == _lastStrikeIndex)
            index = (index + 1) % _strikes.Count;
        _lastStrikeIndex = index;

        _source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        _source.PlayOneShot(_strikes[index], keyVolume * (1f - Random.Range(0f, volumeJitter)));
    }

    private void CutSound()
    {
        if (_source != null)
            _source.Stop();
    }

    private IEnumerator BuildStrikes()
    {
        if (keyClips == null) yield break;

        foreach (AudioClip clip in keyClips)
        {
            if (clip != null && clip.loadState != AudioDataLoadState.Loaded)
                clip.LoadAudioData();
        }

        if (carriageReturnClip != null && carriageReturnClip.loadState != AudioDataLoadState.Loaded)
            carriageReturnClip.LoadAudioData();

        foreach (AudioClip clip in keyClips)
        {
            if (clip == null) continue;

            while (clip.loadState == AudioDataLoadState.Loading)
                yield return null;

            if (clip.loadState != AudioDataLoadState.Loaded) continue;

            AudioClip strike = TrimToStrike(clip);
            if (strike != null)
                _strikes.Add(strike);
        }

        if (carriageReturnClip != null && oldRecordingFilter)
        {
            while (carriageReturnClip.loadState == AudioDataLoadState.Loading)
                yield return null;

            if (carriageReturnClip.loadState == AudioDataLoadState.Loaded)
                _carriageReturn = CreateGrittyCopy(carriageReturnClip);
        }

        _built = true;
    }

    /// <summary>Full-length copy of <paramref name="clip"/> with the baked bit-crush + hiss applied.</summary>
    private AudioClip CreateGrittyCopy(AudioClip clip)
    {
        var data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0)) return null;

        ApplyGrit(data);

        AudioClip copy = AudioClip.Create($"{clip.name}_Old", clip.samples, clip.channels, clip.frequency, false);
        copy.SetData(data, 0);
        return copy;
    }

    /// <summary>Quantises to <see cref="bitDepth"/> and adds hiss in place. No-op when the filter is off.</summary>
    private void ApplyGrit(float[] samples)
    {
        if (!oldRecordingFilter) return;

        float steps = bitDepth >= 16 ? 0f : Mathf.Pow(2f, bitDepth - 1);
        for (int i = 0; i < samples.Length; i++)
        {
            float s = samples[i];
            if (steps > 0f)
                s = Mathf.Round(s * steps) / steps;
            if (hissLevel > 0f)
                s += Random.Range(-hissLevel, hissLevel);
            samples[i] = Mathf.Clamp(s, -1f, 1f);
        }
    }

    private AudioClip TrimToStrike(AudioClip clip)
    {
        int channels = clip.channels;
        int frequency = clip.frequency;
        int frames = clip.samples;
        if (channels <= 0 || frames <= 0) return null;

        var data = new float[frames * channels];
        if (!clip.GetData(data, 0))
        {
            Debug.LogWarning($"[TeleprinterRevealAudio] Could not read '{clip.name}'. Set its Load Type to Decompress On Load.");
            return null;
        }

        // Peak frame (max across channels).
        int peakFrame = 0;
        float peak = 0f;
        for (int f = 0; f < frames; f++)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                float v = Mathf.Abs(data[f * channels + ch]);
                if (v > peak) { peak = v; peakFrame = f; }
            }
        }
        if (peak <= 0.0001f) return null;

        // Walk back from the peak to the strike onset.
        float threshold = peak * onsetThreshold;
        int onsetFrame = peakFrame;
        int searchLimit = Mathf.Max(0, peakFrame - Mathf.RoundToInt(0.03f * frequency));
        for (int f = peakFrame; f >= searchLimit; f--)
        {
            bool loud = false;
            for (int ch = 0; ch < channels; ch++)
            {
                if (Mathf.Abs(data[f * channels + ch]) >= threshold) { loud = true; break; }
            }
            if (loud) onsetFrame = f;
        }

        int startFrame = Mathf.Max(0, onsetFrame - Mathf.RoundToInt(preRoll * frequency));
        int lengthFrames = Mathf.Min(frames - startFrame, Mathf.Max(1, Mathf.RoundToInt(strikeLength * frequency)));

        int fadeInFrames = Mathf.Max(1, Mathf.RoundToInt(0.001f * frequency));
        int fadeOutFrames = Mathf.Max(1, Mathf.RoundToInt(lengthFrames * 0.6f));
        float gain = 0.9f / peak;

        var trimmed = new float[lengthFrames * channels];
        for (int f = 0; f < lengthFrames; f++)
        {
            for (int ch = 0; ch < channels; ch++)
                trimmed[f * channels + ch] = data[(startFrame + f) * channels + ch] * gain;
        }

        // Grit before the envelope so the hiss fades with the strike instead of clicking at the edges.
        ApplyGrit(trimmed);

        for (int f = 0; f < lengthFrames; f++)
        {
            float env = 1f;
            if (f < fadeInFrames)
                env = f / (float)fadeInFrames;
            int fromEnd = lengthFrames - 1 - f;
            if (fromEnd < fadeOutFrames)
                env *= fromEnd / (float)fadeOutFrames;

            for (int ch = 0; ch < channels; ch++)
                trimmed[f * channels + ch] *= env;
        }

        AudioClip strike = AudioClip.Create($"{clip.name}_Strike", lengthFrames, channels, frequency, false);
        strike.SetData(trimmed, 0);
        return strike;
    }
}
