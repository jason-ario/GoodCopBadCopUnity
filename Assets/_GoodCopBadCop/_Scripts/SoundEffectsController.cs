using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SFXController : MonoBehaviour
{
    public static SFXController Instance;

    private const float DefaultSpatialMaxDistance = 5f;
    private const string SfxVolumeSettingsKey = "settings.audio.sfxVolume";

    /// <summary>
    /// Multiplier (0-1) applied to all SFX playback, driven by Settings > Audio > SFX Volume.
    /// </summary>
    public float VolumeScale { get; set; } = 1f;

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
        VolumeScale = PlayerPrefs.GetFloat(SfxVolumeSettingsKey, 80f) / 100f;
    }

    // -----------------------------
    // PUBLIC API
    // -----------------------------

    public void Play(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (!clip) return;

        sfxSource.pitch = pitch;
        sfxSource.PlayOneShot(clip, volume * VolumeScale);
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
    public void PlayAtPosition(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f, float maxDistance = DefaultSpatialMaxDistance)
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
        source.clip = clip;
        source.volume = volume * VolumeScale;
        source.pitch = pitch;

        if (maxDistance > 0f)
            source.maxDistance = maxDistance;

        source.Play();

        StartCoroutine(DestroyAfterClip(emitter, clip.length / Mathf.Abs(pitch)));
    }
    
    public void PlayCustomSFX(GameObject customSFXPrefab, AudioClip clip)
    {
        AudioSource source = Instantiate(customSFXPrefab, transform.position, transform.rotation).GetComponent<AudioSource>();
        source.volume *= VolumeScale;
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
