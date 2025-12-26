using UnityEngine;
using System.Collections.Generic;

public class SFXController : MonoBehaviour
{
    public static SFXController Instance;

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
}
