using DG.Tweening;
using UnityEngine;

/// <summary>
/// Relative importance of a music request. Higher values win: a lower-priority
/// <see cref="MusicManager.Play"/> call is ignored while a higher-priority track is
/// active, so e.g. Alexei/mutant encounter music always cuts off ambient music and
/// ambient music can never interrupt an encounter. Once the active track is stopped
/// or fully faded out, priority resets to <see cref="Ambient"/> so lower-priority
/// music (like <see cref="AmbientMusicManager"/>) can reclaim the source.
/// </summary>
public enum MusicPriority
{
    Ambient = 0,
    Encounter = 1,
}

/// <summary>
/// Singleton music player for the game. Owns a single non-spatial AudioSource so no
/// per-feature AudioSource setup is needed — callers simply pass an AudioClip.
///
/// Supports optional fade-in on play and fade-out on stop. Multiple <see cref="Play"/>
/// calls while music is already running will cross-fade: the current track fades out over
/// <see cref="CrossFadeDuration"/> while the new one fades in simultaneously. Calls also
/// carry a <see cref="MusicPriority"/> so lower-priority music (ambience) never interrupts
/// higher-priority music (story/encounter stingers) — see <see cref="MusicPriority"/>.
///
/// All methods are client-local — they must be called from a ClientRpc (or directly on a
/// non-networked host) to reach every connected player.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    private const string MusicVolumeSettingsKey = "settings.audio.musicVolume";

    [Tooltip("Default duration in seconds for fade-in when calling Play() without an explicit fade.")]
    [SerializeField] private float _defaultFadeInDuration = 1f;

    [Tooltip("Default duration in seconds for FadeOut() when no duration is supplied.")]
    [SerializeField] private float _defaultFadeOutDuration = 3f;

    [Tooltip("Seconds over which a new Play() call cross-fades out the currently playing track.")]
    [SerializeField] private float _crossFadeDuration = 1.5f;

    [SerializeField] private float defaultVolume = .5f;

    [Tooltip("Volume multiplier applied only to Ambient-priority tracks, on top of defaultVolume (e.g. 0.5 = half as loud as normal music).")]
    [SerializeField] private float _ambientVolumeMultiplier = 0.5f;

    private AudioSource _source;
    private float _volumeScale = 1f;
    private MusicPriority _currentPriority = MusicPriority.Ambient;

    /// <summary>Priority of the track currently active on the source (or last active one).</summary>
    public MusicPriority CurrentPriority => _currentPriority;

    /// <summary>True while the underlying AudioSource is actually playing.</summary>
    public bool IsPlaying => _source != null && _source.isPlaying;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _source             = GetComponent<AudioSource>();
        _source.loop        = true;
        _source.playOnAwake = false;
        _source.spatialBlend = 0f; // Always 2-D / non-spatial.
        _volumeScale = PlayerPrefs.GetFloat(MusicVolumeSettingsKey, 70f) / 100f;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the volume multiplier (0-1) applied on top of <see cref="defaultVolume"/> / fade
    /// targets, driven by Settings > Audio > Music Volume. Applies immediately if music is
    /// currently playing (and not mid cross-fade-out).
    /// </summary>
    public void SetVolumeScale(float scale01)
    {
        _volumeScale = Mathf.Clamp01(scale01);

        if (_source != null && _source.isPlaying && !DOTween.IsTweening(_source))
        {
            _source.volume = TargetVolume(_currentPriority);
        }
    }

    /// <summary>
    /// Plays <paramref name="clip"/> immediately, optionally fading in over
    /// <paramref name="fadeInDuration"/> seconds. If music is already playing the
    /// current track cross-fades out over <see cref="_crossFadeDuration"/> while the
    /// new clip fades in.
    /// </summary>
    /// <param name="clip">The music clip to play.</param>
    /// <param name="loop">Whether to loop the clip. Defaults to true.</param>
    /// <param name="fadeInDuration">
    /// Fade-in duration in seconds. Pass 0 for instant start.
    /// Pass -1 to use <see cref="_defaultFadeInDuration"/>.
    /// </param>
    /// <param name="priority">
    /// Importance of this request. Defaults to <see cref="MusicPriority.Encounter"/> so
    /// existing story/encounter callers are unaffected. Requests below the currently
    /// active priority are ignored while that track is still playing — see
    /// <see cref="MusicPriority"/>.
    /// </param>
    public void Play(AudioClip clip, bool loop = true, float fadeInDuration = -1f, MusicPriority priority = MusicPriority.Encounter)
    {
        if (clip == null)
        {
            Debug.LogWarning("[MusicManager] Play called with a null clip.");
            return;
        }

        if (_source.isPlaying && priority < _currentPriority)
        {
            Debug.Log($"[MusicManager] Play ignored — '{clip.name}' priority {priority} is lower than active {_currentPriority}.");
            return;
        }

        _currentPriority = priority;

        float fadeIn = fadeInDuration < 0f ? _defaultFadeInDuration : fadeInDuration;

        Debug.Log($"[MusicManager] Play — clip: '{clip.name}', loop: {loop}, fadeIn: {fadeIn:F2}s, priority: {priority}, sourceVolume: {_source.volume:F2}, isPlaying: {_source.isPlaying}");

        _source.DOKill();

        if (_source.isPlaying)
        {
            // Cross-fade: quick fade-out of the current track, then swap and fade in.
            float crossFade = Mathf.Min(_crossFadeDuration, fadeIn > 0f ? fadeIn : _crossFadeDuration);
            _source.DOFade(0f, crossFade).OnComplete(() => SwapAndPlay(clip, loop, fadeIn));
        }
        else
        {
            SwapAndPlay(clip, loop, fadeIn);
        }
    }

    /// <summary>
    /// Fades the current track out over <paramref name="duration"/> seconds and stops playback.
    /// Resets priority to <see cref="MusicPriority.Ambient"/> so lower-priority music can
    /// resume once this completes.
    /// </summary>
    /// <param name="duration">
    /// Fade duration in seconds. Pass -1 to use <see cref="_defaultFadeOutDuration"/>.
    /// </param>
    public void FadeOut(float duration = -1f)
    {
        if (!_source.isPlaying) return;

        float d = duration < 0f ? _defaultFadeOutDuration : duration;
        _source.DOKill();
        _source.DOFade(0f, d).OnComplete(() =>
        {
            _source.Stop();
            _currentPriority = MusicPriority.Ambient;
        });
    }

    /// <summary>Stops playback immediately with no fade. Resets priority to <see cref="MusicPriority.Ambient"/>.</summary>
    public void Stop()
    {
        _source.DOKill();
        _source.Stop();
        _source.volume = 1f;
        _currentPriority = MusicPriority.Ambient;
    }

    /// <summary>
    /// Smoothly ducks (volume toward 0) or restores (volume toward the normal target) the
    /// currently playing track over <paramref name="duration"/> seconds, without touching
    /// playback state or priority. Intended for the ambient music system to yield to nearby
    /// diegetic sources (e.g. a Radio) — has no effect unless an Ambient-priority track is
    /// the one currently active, so encounter music is never affected by this call.
    /// </summary>
    public void SetDuck(bool ducked, float duration)
    {
        if (_source == null || !_source.isPlaying || _currentPriority != MusicPriority.Ambient) return;

        float target = ducked ? 0f : TargetVolume(_currentPriority);
        _source.DOKill();
        _source.DOFade(target, Mathf.Max(0f, duration));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Effective target volume for <paramref name="priority"/>: <see cref="defaultVolume"/> scaled by
    /// the settings volume, and further scaled by <see cref="_ambientVolumeMultiplier"/> for Ambient tracks.</summary>
    private float TargetVolume(MusicPriority priority)
    {
        float volume = defaultVolume * _volumeScale;
        if (priority == MusicPriority.Ambient)
            volume *= _ambientVolumeMultiplier;
        return volume;
    }

    private void SwapAndPlay(AudioClip clip, bool loop, float fadeIn)
    {
        _source.clip   = clip;
        _source.loop   = loop;
        float target = TargetVolume(_currentPriority);
        _source.volume = fadeIn > 0f ? 0f : target;
        _source.Play();

        if (fadeIn > 0f)
            _source.DOFade(target, fadeIn);
    }
}
