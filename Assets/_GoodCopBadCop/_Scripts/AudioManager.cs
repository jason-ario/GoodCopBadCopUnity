using DG.Tweening;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;
    
    [SerializeField] private AudioSource ambientAudio;
    private float ambientAudioOriginalVolume;
    private void Awake()
    {
        ambientAudioOriginalVolume = ambientAudio.volume;
        Instance = this;
    }
    
    public void FadeOutAmbientAudio()
    {
        ambientAudio.DOFade(0, 3).OnComplete(ambientAudio.Stop);
    }

    public void StartAmbientAudio()
    {
        ambientAudio.volume = ambientAudioOriginalVolume;
        ambientAudio.Play();
    }
}
