using UnityEngine;
using UnityEngine.Audio;

public class PlaySoundAnimationEvent : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;

    /// <summary>Plays a single audio clip as a one-shot sound.</summary>
    public void PlaySound(AudioClip clip)
    {
        audioSource.PlayOneShot(clip);
    }
    
    /// <summary>Plays a sound from an AudioRandomContainer by assigning it as the audio source's resource and calling Play.</summary>
    public void PlayRandomContainer(AudioResource container)
    {
        audioSource.resource = container;
        audioSource.Play();
    }
}
