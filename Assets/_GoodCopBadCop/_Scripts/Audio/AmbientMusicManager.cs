using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Local (per-client) player for the rare ambient music tracks. The schedule itself (when to
/// play and which track) is owned by the server through <see cref="AmbientMusicDirector"/>,
/// which replicates a track index + server start time so every player hears the same track
/// at the same position. This component only:
/// - Holds the ambient track list and timing/fade configuration (read by the director).
/// - Plays the replicated track through <see cref="MusicManager"/> at
///   <see cref="MusicPriority.Ambient"/>, seeked to the current server-time position.
/// - Ducks the ambient track locally while this client's player is near an "on"
///   <see cref="Radio"/> (proximity is per-player, so ducking intentionally stays local).
///
/// Encounter-priority music always wins over ambience inside <see cref="MusicManager"/>.
/// </summary>
public class AmbientMusicManager : MonoBehaviour
{
    public static AmbientMusicManager Instance { get; private set; }

    [Header("Ambient Tracks")]
    [SerializeField] private AudioClip[] _ambientTracks;

    [Header("Silence Timing (server)")]
    [Tooltip("Minimum seconds of silence before the server chooses a new ambient track.")]
    [SerializeField] private float _minSilenceDuration = 300f; // 5 minutes
    [Tooltip("Maximum seconds of silence before the server chooses a new ambient track.")]
    [SerializeField] private float _maxSilenceDuration = 600f; // 10 minutes

    [Header("Fades")]
    [SerializeField] private float _fadeInDuration = 4f;
    [SerializeField] private float _radioDuckFadeDuration = 2f;
    [Tooltip("Fade-out used when the server ends/cancels the ambient track or the session ends.")]
    [SerializeField] private float _stopFadeDuration = 2f;

    [Header("Radio Proximity")]
    [Tooltip("Extra distance added on top of each radio's own AmbientDuckRange before ambient starts ducking.")]
    [SerializeField] private float _radioProximityBuffer = 0f;
    [Tooltip("How often (seconds) to re-check distance to on radios while an ambient track is active.")]
    [SerializeField] private float _proximityCheckInterval = 0.5f;
    [Tooltip("How often (seconds) to refresh the cached list of Radios in the scene.")]
    [SerializeField] private float _radioCacheRefreshInterval = 5f;

    private Coroutine _playRoutine;
    private bool _duckedByRadio;
    private Radio[] _radiosCache;
    private float _radiosCacheTime = -999f;

    public int TrackCount => _ambientTracks != null ? _ambientTracks.Length : 0;

    public AudioClip GetTrack(int index) =>
        _ambientTracks != null && index >= 0 && index < _ambientTracks.Length ? _ambientTracks[index] : null;

    public float NextSilenceDuration() => Random.Range(_minSilenceDuration, _maxSilenceDuration);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnDisable()
    {
        CancelPlayRoutine();
    }

    // ── Network-driven playback (called by AmbientMusicDirector) ─────────────

    /// <summary>
    /// Plays ambient track <paramref name="trackIndex"/> so that it lines up with
    /// <paramref name="startServerTime"/> (server network time). Waits if the start time is
    /// still in the future, seeks forward if it has already passed (late join / latency).
    /// </summary>
    public void PlaySynced(int trackIndex, double startServerTime)
    {
        AudioClip clip = GetTrack(trackIndex);
        if (clip == null)
        {
            Debug.LogWarning($"[AmbientMusicManager] No ambient track at index {trackIndex}.");
            return;
        }

        CancelPlayRoutine();
        _playRoutine = StartCoroutine(PlaySyncedRoutine(clip, startServerTime));
    }

    /// <summary>Stops tracking the ambient track and fades it out if it is still the active music.</summary>
    public void StopSynced()
    {
        CancelPlayRoutine();

        MusicManager music = MusicManager.Instance;
        if (music != null && music.IsPlaying && music.CurrentPriority == MusicPriority.Ambient)
            music.FadeOut(_stopFadeDuration);
    }

    private IEnumerator PlaySyncedRoutine(AudioClip clip, double startServerTime)
    {
        // Wait for the shared start moment (server schedules slightly ahead to absorb latency).
        while (ServerTimeNow() < startServerTime)
            yield return null;

        float elapsed = (float)(ServerTimeNow() - startServerTime);
        if (elapsed >= clip.length - 0.1f) { _playRoutine = null; yield break; }

        MusicManager music = MusicManager.Instance;
        if (music == null) { _playRoutine = null; yield break; }

        // Local higher-priority music (encounter) keeps playing; MusicManager ignores us.
        if (music.IsPlaying && music.CurrentPriority != MusicPriority.Ambient) { _playRoutine = null; yield break; }

        _duckedByRadio = IsPlayerNearOnRadio();

        // Late joiners (well past the start) skip the long fade so they land at the right volume quickly.
        float fadeIn = _duckedByRadio ? 0f : Mathf.Max(0f, _fadeInDuration - elapsed);
        music.Play(clip, false, fadeIn, MusicPriority.Ambient, elapsed);
        if (_duckedByRadio)
            music.SetDuck(true, 0f);

        // Local radio ducking for the rest of the track.
        while (ServerTimeNow() - startServerTime < clip.length)
        {
            if (MusicManager.Instance == null || MusicManager.Instance.CurrentPriority != MusicPriority.Ambient)
                break;

            yield return new WaitForSeconds(_proximityCheckInterval);

            if (MusicManager.Instance == null || MusicManager.Instance.CurrentPriority != MusicPriority.Ambient)
                break;

            UpdateRadioDucking();
        }

        _playRoutine = null;
    }

    private void CancelPlayRoutine()
    {
        if (_playRoutine == null) return;
        StopCoroutine(_playRoutine);
        _playRoutine = null;
    }

    private static double ServerTimeNow()
    {
        NetworkManager nm = NetworkManager.Singleton;
        return nm != null && nm.IsListening ? nm.ServerTime.Time : Time.timeAsDouble;
    }

    // ── Radio proximity ──────────────────────────────────────────────────────

    private void UpdateRadioDucking()
    {
        bool nearOnRadio = IsPlayerNearOnRadio();
        if (nearOnRadio == _duckedByRadio) return;

        _duckedByRadio = nearOnRadio;
        MusicManager.Instance?.SetDuck(_duckedByRadio, _radioDuckFadeDuration);
    }

    private bool IsPlayerNearOnRadio()
    {
        Transform player = PlayerInstance.Instance != null ? PlayerInstance.Instance.transform : null;
        if (player == null) return false;

        RefreshRadioCache();
        if (_radiosCache == null || _radiosCache.Length == 0) return false;

        foreach (Radio radio in _radiosCache)
        {
            if (radio == null || !radio.IsOn) continue;

            float range = radio.AmbientDuckRange + _radioProximityBuffer;
            float sqrDist = (radio.transform.position - player.position).sqrMagnitude;
            if (sqrDist <= range * range) return true;
        }

        return false;
    }

    private void RefreshRadioCache()
    {
        if (_radiosCache != null && Time.time - _radiosCacheTime < _radioCacheRefreshInterval) return;

        _radiosCache = FindObjectsByType<Radio>(FindObjectsSortMode.None);
        _radiosCacheTime = Time.time;
    }
}
