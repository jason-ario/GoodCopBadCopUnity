using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SFXController : MonoBehaviour
{
    public static SFXController Instance;

    private const float DefaultSpatialMaxDistance = 5f;

    [Header("Audio Source")]
    [SerializeField] private AudioSource sfxSource;

    [Header("Spatial Emitter")]
    [SerializeField] private GameObject spatialAudioEmitterPrefab;

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

        sfxSource.pitch = pitch;
        sfxSource.PlayOneShot(clip, volume);
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
        source.volume = volume;
        source.pitch = pitch;

        if (maxDistance > 0f)
            source.maxDistance = maxDistance;

        source.Play();

        StartCoroutine(DestroyAfterClip(emitter, clip.length / Mathf.Abs(pitch)));
    }
    
    public void PlayCustomSFX(GameObject customSFXPrefab, AudioClip clip)
    {
        Instantiate(customSFXPrefab, transform.position, transform.rotation).GetComponent<AudioSource>().PlayOneShot(clip);
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
