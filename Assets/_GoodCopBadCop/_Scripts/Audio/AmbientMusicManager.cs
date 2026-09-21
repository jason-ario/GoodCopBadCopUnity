using System.Collections;
using UnityEngine;

/// <summary>
/// Periodically plays a random ambient music track during quiet moments, coexisting with
/// the Radio and with any higher-priority story/encounter music (Alexei chase, mutant
/// booth/breach music, etc.) driven through <see cref="MusicManager"/>.
///
/// Behaviour:
/// - Waits a random duration between <see cref="_minSilenceDuration"/> and
///   <see cref="_maxSilenceDuration"/> seconds of "silence" (no ambient/encounter music
///   playing), then plays a random ambient track once at <see cref="MusicPriority.Ambient"/>.
/// - Encounter-priority calls to <see cref="MusicManager.Play"/> (Alexei, mutant, etc.) are
///   always accepted over an active Ambient track — <see cref="MusicManager"/> immediately
///   cross-fades the ambient track out while the new music fades in. When that encounter
///   music later fades out or stops, priority resets to Ambient and this manager simply
///   resumes its normal quiet-period schedule (it does not force ambience back on).
/// - While an ambient track is active, if the local player is within an "on" Radio's
///   <see cref="Radio.AmbientDuckRange"/>, the ambient track smoothly ducks to silence
///   (without stopping/cancelling it) and fades back in once the player leaves range or the
///   radio is switched off — as long as nothing of higher priority has taken over meanwhile.
/// </summary>
public class AmbientMusicManager : MonoBehaviour
{
    public static AmbientMusicManager Instance { get; private set; }

    [Header("Ambient Tracks")]
    [SerializeField] private AudioClip[] _ambientTracks;

    [Header("Silence Timing")]
    [Tooltip("Minimum seconds of silence before a new ambient track is chosen.")]
    [SerializeField] private float _minSilenceDuration = 300f; // 5 minutes
    [Tooltip("Maximum seconds of silence before a new ambient track is chosen.")]
    [SerializeField] private float _maxSilenceDuration = 600f; // 10 minutes

    [Header("Fades")]
    [SerializeField] private float _fadeInDuration = 4f;
    [SerializeField] private float _radioDuckFadeDuration = 2f;

    [Header("Radio Proximity")]
    [Tooltip("Extra distance added on top of each radio's own AmbientDuckRange before ambient starts ducking.")]
    [SerializeField] private float _radioProximityBuffer = 0f;
    [Tooltip("How often (seconds) to re-check distance to on radios while an ambient track is active.")]
    [SerializeField] private float _proximityCheckInterval = 0.5f;
    [Tooltip("How often (seconds) to refresh the cached list of Radios in the scene.")]
    [SerializeField] private float _radioCacheRefreshInterval = 5f;

    private Coroutine _scheduleRoutine;
    private bool _duckedByRadio;
    private Radio[] _radiosCache;
    private float _radiosCacheTime = -999f;

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

    private void OnEnable()
    {
        _scheduleRoutine = StartCoroutine(ScheduleLoop());
    }

    private void OnDisable()
    {
        if (_scheduleRoutine != null)
        {
            StopCoroutine(_scheduleRoutine);
            _scheduleRoutine = null;
        }
    }

    // ── Main schedule ─────────────────────────────────────────────────────────

    private IEnumerator ScheduleLoop()
    {
        while (true)
        {
            float wait = Random.Range(_minSilenceDuration, _maxSilenceDuration);
            yield return new WaitForSeconds(wait);

            AudioClip clip = PickRandomTrack();
            if (clip == null) continue;

            // Don't step on higher-priority music that may already be playing.
            while (MusicManager.Instance == null || (MusicManager.Instance.IsPlaying && MusicManager.Instance.CurrentPriority != MusicPriority.Ambient))
                yield return null;

            yield return PlayAmbientTrack(clip);
        }
    }

    private IEnumerator PlayAmbientTrack(AudioClip clip)
    {
        _duckedByRadio = IsPlayerNearOnRadio();

        MusicManager.Instance.Play(clip, false, _duckedByRadio ? 0f : _fadeInDuration, MusicPriority.Ambient);
        if (_duckedByRadio)
            MusicManager.Instance.SetDuck(true, 0f);

        float elapsed = 0f;
        while (elapsed < clip.length)
        {
            // Something higher priority took over — abandon tracking this clip and
            // fall back to a fresh silence period.
            if (MusicManager.Instance == null || MusicManager.Instance.CurrentPriority != MusicPriority.Ambient)
                yield break;

            float sinceCheck = 0f;
            while (sinceCheck < _proximityCheckInterval && elapsed < clip.length)
            {
                yield return null;
                sinceCheck += Time.deltaTime;
                elapsed += Time.deltaTime;
            }

            if (MusicManager.Instance == null || MusicManager.Instance.CurrentPriority != MusicPriority.Ambient)
                yield break;

            UpdateRadioDucking();
        }
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private AudioClip PickRandomTrack()
    {
        if (_ambientTracks == null || _ambientTracks.Length == 0)
        {
            Debug.LogWarning("[AmbientMusicManager] No ambient tracks assigned.");
            return null;
        }

        return _ambientTracks[Random.Range(0, _ambientTracks.Length)];
    }
}
