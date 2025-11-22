using UnityEngine;

public class FootstepsAudio : MonoBehaviour
{
    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip[] footstepClips;

    [Header("Pitch Variation")]
    [Range(0f, 0.5f)]
    public float pitchRandomness = 0.1f;

    public bool disable;

    public void PlayFootstep()
    {   
        if (disable)
        {
            return;
        }
        
        if (footstepClips.Length == 0 || audioSource == null)
            return;

        // Pick random clip
        AudioClip clip = footstepClips[Random.Range(0, footstepClips.Length)];

        // Add subtle pitch variation
        audioSource.pitch = 1f + Random.Range(-pitchRandomness, pitchRandomness);

        audioSource.PlayOneShot(clip);
    }
}