using DG.Tweening;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;
    
    [SerializeField] private AudioSource ambientAudio;
    [SerializeField] private AudioSource rainAmbience;
    [SerializeField] private float rainAmbienceFadeSeconds = 2f;
    private float ambientAudioOriginalVolume;
    private float rainAmbienceOriginalVolume;

    private void Awake()
    {
        ambientAudioOriginalVolume = ambientAudio.volume;
        rainAmbienceOriginalVolume = rainAmbience != null ? rainAmbience.volume : 1f;
        Instance = this;
    }
    
    public void FadeOutAmbientAudio()
    {
        ambientAudio.DOFade(0, 3).OnComplete(ambientAudio.Stop);
    }

    /// <summary>Cancels any in-progress fade-out and immediately starts ambient audio at full volume.</summary>
    public void StartAmbientAudio()
    {
        ambientAudio.DOKill();
        ambientAudio.volume = ambientAudioOriginalVolume;
        ambientAudio.Play();
    }

    /// <summary>Fades the rain ambience AudioSource in or out, playing/stopping it as needed.</summary>
    public void SetRainAmbience(bool enabled)
    {
        if (rainAmbience == null)
        {
            return;
        }

        rainAmbience.DOKill();

        if (enabled)
        {
            rainAmbience.gameObject.SetActive(true);
            if (!rainAmbience.isPlaying)
            {
                rainAmbience.volume = 0f;
                rainAmbience.Play();
            }
            rainAmbience.DOFade(rainAmbienceOriginalVolume, rainAmbienceFadeSeconds);
        }
        else
        {
            rainAmbience.DOFade(0f, rainAmbienceFadeSeconds).OnComplete(() =>
            {
                rainAmbience.Stop();
                rainAmbience.gameObject.SetActive(false);
            });
        }
    }
}
