using UnityEngine;

public class FootstepsAudio : MonoBehaviour
{
    [Header("Audio")]
    public AudioSource audioSource;

    [Tooltip("Clips played when the player is outside the booth.")]
    public AudioClip[] outsideFootstepClips;

    [Tooltip("Clips played when the player is inside the booth.")]
    public AudioClip[] insideFootstepClips;

    [Header("Pitch Variation")]
    [Range(0f, 0.5f)]
    public float pitchRandomness = 0.1f;

    public bool disable;

    /// <summary>
    /// Plays a random footstep clip chosen from the appropriate set based on whether
    /// the player is currently inside or outside the booth.
    /// </summary>
    public void PlayFootstep()
    {
        if (disable)
            return;

        if (audioSource == null)
            return;

        AudioClip[] clips = ResolveClipSet();

        if (clips == null || clips.Length == 0)
            return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];

        audioSource.pitch = 1f + Random.Range(-pitchRandomness, pitchRandomness);
        audioSource.PlayOneShot(clip);
    }

    private AudioClip[] ResolveClipSet()
    {
        bool isOutside = PlayerInstance.Instance != null && PlayerInstance.Instance.IsOutside;
        return isOutside ? outsideFootstepClips : insideFootstepClips;
    }
}
