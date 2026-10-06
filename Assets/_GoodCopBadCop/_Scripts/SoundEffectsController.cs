using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SFXController : MonoBehaviour
{
    public static SFXController Instance;

    // SFX volume is applied by the MainMix "SFX" group fader (GoodCopBadCop.Audio.GameAudioMixer),
    // not per source, so world sources without SFXController are covered too.
    private const float DefaultSpatialMaxDistance = 5f;

    /// <summary>Looping 2D music source; routed to the MainMix Music group.</summary>
    public AudioSource MusicSource => _musicSource;

    [Header("Audio Source")]
    [SerializeField] private AudioSource sfxSource;

    [Header("Spatial Emitter")]
    [SerializeField] private GameObject spatialAudioEmitterPrefab;

    [Header("Music")]
    [Tooltip("Dedicated AudioSource used for looping music tracks (e.g. chase music). " +
             "Should be a separate AudioSource with loop enabled and spatial blend = 0 (2D).")]
    [SerializeField] private AudioSource _musicSource;

    private Coroutine _musicFadeCoroutine;

    void Awake()
    {
        Instance = this;
    }

    // -----------------------------
    // PUBLIC API
    // -----------------------------

    public void Play(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (!clip) return;

        // AudioSource.pitch applies to every one-shot still playing on that source, so changing
        // the shared source's pitch would warp unrelated sounds mid-playback (e.g. the bunk-bed
        // end-of-day SFX getting re-pitched by the shift report's randomised row sounds).
        // The shared source stays at 1; pitched one-shots get their own pooled source.
        if (Mathf.Approximately(pitch, 1f))
        {
            sfxSource.pitch = 1f;
            sfxSource.PlayOneShot(clip, volume);
            return;
        }

        AudioSource source = GetPitchedSource();
        source.pitch = pitch;
        source.PlayOneShot(clip, volume);
    }

    private readonly List<AudioSource> _pitchedSources = new();

    private AudioSource GetPitchedSource()
    {
        foreach (AudioSource pooled in _pitchedSources)
        {
            if (pooled != null && !pooled.isPlaying)
                return pooled;
        }

        var go = new GameObject($"PitchedSFX_{_pitchedSources.Count}");
        go.transform.SetParent(transform, false);
        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.outputAudioMixerGroup = sfxSource.outputAudioMixerGroup;
        source.spatialBlend = sfxSource.spatialBlend;
        source.priority = sfxSource.priority;
        source.ignoreListenerPause = sfxSource.ignoreListenerPause;
        source.ignoreListenerVolume = sfxSource.ignoreListenerVolume;
        source.bypassEffects = sfxSource.bypassEffects;
        source.bypassListenerEffects = sfxSource.bypassListenerEffects;
        source.bypassReverbZones = sfxSource.bypassReverbZones;
        source.reverbZoneMix = sfxSource.reverbZoneMix;

        _pitchedSources.Add(source);
        return source;
    }

    /// <summary>
    /// Starts playing <paramref name="clip"/> as a looping 2D music track, fading in over
    /// <paramref name="fadeDuration"/> seconds. Any previously playing music is interrupted immediately.
    /// </summary>
    public void PlayMusicLooping(AudioClip clip, float fadeDuration = 1f)
    {
        if (!clip || _musicSource == null) return;

        if (_musicFadeCoroutine != null)
            StopCoroutine(_musicFadeCoroutine);

        _musicSource.clip = clip;
        _musicSource.loop = true;
        _musicSource.volume = 0f;
        _musicSource.Play();
        _musicFadeCoroutine = StartCoroutine(FadeMusic(1f, fadeDuration, stopOnComplete: false));
    }

    /// <summary>
    /// Fades out and stops the currently looping music over <paramref name="fadeDuration"/> seconds.
    /// Safe to call when nothing is playing.
    /// </summary>
    public void StopMusic(float fadeDuration = 1f)
    {
        if (_musicSource == null || !_musicSource.isPlaying) return;

        if (_musicFadeCoroutine != null)
            StopCoroutine(_musicFadeCoroutine);

        _musicFadeCoroutine = StartCoroutine(FadeMusic(0f, fadeDuration, stopOnComplete: true));
    }

    private IEnumerator FadeMusic(float targetVolume, float duration, bool stopOnComplete)
    {
        float startVolume = _musicSource.volume;
        float elapsed = 0f;

        if (duration > 0f)
        {
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                _musicSource.volume = Mathf.Lerp(startVolume, targetVolume, elapsed / duration);
                yield return null;
            }
        }

        _musicSource.volume = targetVolume;

        if (stopOnComplete)
            _musicSource.Stop();

        _musicFadeCoroutine = null;
    }

    /// <summary>
    /// Instantiates the <see cref="spatialAudioEmitterPrefab"/> at the given world position,
    /// plays a spatialised AudioClip through its pre-configured AudioSource, then destroys
    /// the instance when playback finishes. The prefab must have an AudioSource component.
    /// </summary>
    /// <param name="clip">The clip to play.</param>
    /// <param name="position">World-space position to spawn the sound emitter.</param>
    /// <param name="volume">Playback volume (0–1).</param>
    /// <param name="pitch">Playback pitch multiplier.</param>
    /// <param name="maxDistance">Maximum audible range in metres. Overrides the prefab's maxDistance when greater than zero.</param>
    /// <param name="minDistance">Distance within which the clip plays at full volume. Overrides the prefab's minDistance when greater than zero.</param>
    /// <param name="priority">AudioSource priority (0 = highest, 256 = lowest). Overrides the prefab's priority when zero or greater, so important one-shots aren't voice-stolen.</param>
    public void PlayAtPosition(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f, float maxDistance = DefaultSpatialMaxDistance, float minDistance = -1f, int priority = -1)
    {
        if (!clip) return;

        if (!spatialAudioEmitterPrefab)
        {
            Debug.LogWarning("[SFXController] spatialAudioEmitterPrefab is not assigned. Assign it in the Inspector.");
            return;
        }

        GameObject emitter = Instantiate(spatialAudioEmitterPrefab, position, Quaternion.identity);
        emitter.name = $"SFX_{clip.name}";

        AudioSource source = emitter.GetComponent<AudioSource>();
        GoodCopBadCop.Audio.GameAudioMixer.Route(source);
        source.clip = clip;
        source.volume = volume;
        source.pitch = pitch;

        if (maxDistance > 0f)
            source.maxDistance = maxDistance;

        if (minDistance > 0f)
            source.minDistance = Mathf.Min(minDistance, source.maxDistance - 0.01f);

        if (priority >= 0)
            source.priority = Mathf.Clamp(priority, 0, 256);

        source.Play();

        StartCoroutine(DestroyAfterClip(emitter, clip.length / Mathf.Abs(pitch)));
    }
    
    public void PlayCustomSFX(GameObject customSFXPrefab, AudioClip clip)
    {
        AudioSource source = Instantiate(customSFXPrefab, transform.position, transform.rotation).GetComponent<AudioSource>();
        GoodCopBadCop.Audio.GameAudioMixer.Route(source);
        source.PlayOneShot(clip);
    }

    // -----------------------------
    // PRIVATE HELPERS
    // -----------------------------

    private IEnumerator DestroyAfterClip(GameObject target, float duration)
    {
        yield return new WaitForSeconds(duration);
        if (target != null)
            Destroy(target);
    }
}
