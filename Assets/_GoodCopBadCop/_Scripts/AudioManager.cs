using System.Collections.Generic;
using DG.Tweening;
using Dissonance.Audio.Playback;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;
    
    [SerializeField] private AudioSource ambientAudio;
    [SerializeField] private AudioSource rainAmbience;
    [SerializeField] private float rainAmbienceFadeSeconds = 2f;
    private float ambientAudioOriginalVolume;
    private float rainAmbienceOriginalVolume;

    // World-audio silence state (see SilenceWorldAudio / RestoreWorldAudio).
    private bool _worldAudioSilenced;
    private Tween _worldAudioPauseTween;
    private readonly List<(AudioSource source, float volume)> _silencedSources = new();
    private readonly List<(AudioSource source, bool ignoreListenerPause)> _exemptSources = new();

    private void Awake()
    {
        ambientAudioOriginalVolume = ambientAudio.volume;
        rainAmbienceOriginalVolume = rainAmbience != null ? rainAmbience.volume : 1f;
        Instance = this;
    }

    private void OnDestroy()
    {
        // AudioListener.pause is global and survives scene loads — never leave it stuck on.
        RestoreWorldAudio();
        if (Instance == this) Instance = null;
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

    /// <summary>
    /// Fades out every playing world AudioSource (ambience, footsteps, hums, mutants, remote
    /// player footsteps, ...) over <paramref name="fadeSeconds"/>, then pauses the AudioListener so
    /// nothing new can be heard. Dissonance voice-chat playback is left untouched (it already
    /// ignores the listener pause). The <see cref="MusicManager"/> source and any sources under
    /// <paramref name="exemptRoots"/> (e.g. UI) are marked ignoreListenerPause and keep playing.
    /// Used by the Thanks For Playing end screen. Undo with <see cref="RestoreWorldAudio"/>.
    /// </summary>
    public void SilenceWorldAudio(float fadeSeconds, params Transform[] exemptRoots)
    {
        if (_worldAudioSilenced)
            return;

        _worldAudioSilenced = true;

        var exempt = new HashSet<AudioSource>();
        if (MusicManager.Instance != null && MusicManager.Instance.TryGetComponent(out AudioSource musicSource))
            exempt.Add(musicSource);

        foreach (Transform root in exemptRoots)
        {
            if (root != null)
                exempt.UnionWith(root.GetComponentsInChildren<AudioSource>(true));
        }

        _exemptSources.Clear();
        foreach (AudioSource source in exempt)
        {
            _exemptSources.Add((source, source.ignoreListenerPause));
            source.ignoreListenerPause = true;
        }

        _silencedSources.Clear();
        foreach (AudioSource source in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
        {
            // Ambient/rain have their own fade-and-stop (FadeOutAmbientAudio / SetRainAmbience);
            // they're still muted by the listener pause, but their tweens aren't hijacked here.
            if (exempt.Contains(source) || !source.isPlaying || source == ambientAudio || source == rainAmbience)
                continue;

            // Voice chat must keep working on the end screen. Dissonance playback sources already
            // ignore the listener pause; just don't fade them out.
            if (source.TryGetComponent(out VoicePlayback _))
                continue;

            _silencedSources.Add((source, source.volume));
            source.DOKill();
            source.DOFade(0f, fadeSeconds).SetUpdate(true);
        }

        _worldAudioPauseTween = DOVirtual.DelayedCall(fadeSeconds, () => AudioListener.pause = true, ignoreTimeScale: true);
    }

    /// <summary>Reverses <see cref="SilenceWorldAudio"/>: unpauses the listener and restores volumes/flags.</summary>
    public void RestoreWorldAudio()
    {
        if (!_worldAudioSilenced)
            return;

        _worldAudioSilenced = false;
        _worldAudioPauseTween?.Kill();
        _worldAudioPauseTween = null;
        AudioListener.pause = false;

        foreach ((AudioSource source, float volume) in _silencedSources)
        {
            if (source == null) continue;
            source.DOKill();
            source.volume = volume;
        }

        foreach ((AudioSource source, bool ignoreListenerPause) in _exemptSources)
        {
            if (source != null)
                source.ignoreListenerPause = ignoreListenerPause;
        }

        _silencedSources.Clear();
        _exemptSources.Clear();
    }
}
