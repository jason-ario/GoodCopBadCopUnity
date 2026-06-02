using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SFXController : MonoBehaviour
{
    public static SFXController Instance;

    private const float DefaultSpatialMaxDistance = 5f;

    [Header("Audio Source")]
    [SerializeField] private AudioSource sfxSource;

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
    /// Spawns a temporary GameObject at the given world position, plays a spatialised
    /// AudioClip, and destroys the object when playback finishes.
    /// </summary>
    /// <param name="clip">The clip to play.</param>
    /// <param name="position">World-space position to spawn the sound emitter.</param>
    /// <param name="volume">Playback volume (0–1).</param>
    /// <param name="pitch">Playback pitch multiplier.</param>
    /// <param name="maxDistance">Maximum audible range in metres. Defaults to <see cref="DefaultSpatialMaxDistance"/>.</param>
    public void PlayAtPosition(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f, float maxDistance = DefaultSpatialMaxDistance)
    {
        if (!clip) return;

        GameObject emitter = new GameObject($"SFX_{clip.name}");
        emitter.transform.position = position;

        AudioSource source = emitter.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.pitch = pitch;
        source.spatialBlend = 1f;           // Full 3-D spatialisation
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1f;
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
