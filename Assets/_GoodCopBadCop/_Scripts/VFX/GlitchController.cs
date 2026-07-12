using System.Collections;
using FronkonGames.Glitches.Interferences;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Drives a post-processing glitch volume (Interferences + Film Grain) whose effect
/// scales smoothly with proximity to the nearest fully-mutated suspect.
///
/// The values authored on the volume profile are treated as maximums: at full proximity
/// the profile values apply in full; further away they scale down to zero. Film grain
/// type reverts to the profile default below a configurable proximity threshold.
///
/// A booth-arrival event provides reliable full-intensity activation when the suspect
/// is sitting at the desk. Occasional distortion bursts fire in sync with a glitch
/// AudioSource, scaled by the current intensity.
/// </summary>
public class GlitchController : MonoBehaviour
{
    // ── References ────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("The glitch Post Processing Volume. Intensity is driven at runtime — set parameters on the profile as maximums.")]
    [SerializeField] private Volume _postProcessingVolume;

    // ── Detection ─────────────────────────────────────────────────────────────

    [Header("Detection")]
    [Tooltip("Within this radius the effect is always at full intensity.")]
    [SerializeField] private float _innerRadius = 5f;

    [Tooltip("Beyond this radius the effect is fully off. Effect fades linearly between inner and outer.")]
    [SerializeField] private float _outerRadius = 18f;

    [Tooltip("Seconds between proximity scan passes.")]
    [SerializeField] private float _scanInterval = 0.3f;

    // ── Fade ──────────────────────────────────────────────────────────────────

    [Header("Fade")]
    [Tooltip("How quickly the effect fades IN when a signal is detected.")]
    [SerializeField] private float _fadeInSpeed = 2f;

    [Tooltip("How quickly the effect fades OUT when the signal drops.")]
    [SerializeField] private float _fadeOutSpeed = 1.5f;

    // ── Film Grain ────────────────────────────────────────────────────────────

    [Header("Film Grain")]
    [Tooltip("Proximity signal below this threshold reverts film grain type to the profile default.")]
    [SerializeField, Range(0f, 1f)] private float _filmGrainTypeThreshold = 0.3f;

    // ── Glitch Bursts ─────────────────────────────────────────────────────────

    [Header("Glitch Bursts")]
    [Tooltip("AudioSource whose clip is seeked to a random position each burst. Loop on, Play On Awake off.")]
    [SerializeField] private AudioSource _glitchAudioSource;

    [Tooltip("Seconds of silence between glitch bursts.")]
    [SerializeField] private float _glitchIntervalMin = 3f;
    [SerializeField] private float _glitchIntervalMax = 8f;

    [Tooltip("Duration of each individual glitch burst.")]
    [SerializeField] private float _glitchDurationMin = 0.15f;
    [SerializeField] private float _glitchDurationMax = 1.5f;

    [Tooltip("Peak distortion amplitude injected during a burst (scaled further by current intensity).")]
    [SerializeField, Range(0f, 1f)] private float _glitchBurstIntensity = 0.5f;

    // ── Debug ─────────────────────────────────────────────────────────────────

    [Header("Debug")]
    [Tooltip("Force the glitch volume to full intensity without a nearby fully-mutated suspect.")]
    [SerializeField] private bool _debugForceGlitch;

    // ── Internals ─────────────────────────────────────────────────────────────

    private InterferencesVolume _interferences;
    private FilmGrain _filmGrain;

    // Max values read from the profile at startup — treated as the authored ceiling.
    private float _maxInterferencesIntensity;
    private float _maxFilmGrainIntensity;
    private FilmGrainLookup _defaultFilmGrainType;

    // Smoothed proximity signal [0, 1].
    private float _currentWeight;
    private float _targetWeight;

    // Additive distortion spike from the burst coroutine.
    private float _burstAmplitude;

    private float _scanTimer;

    // True while a fully-mutated suspect is presenting at the booth (event-driven, reliable on all clients).
    private bool _boothMutantActive;
    private SuspectCharacter _boothMutant;

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (_postProcessingVolume == null)
        {
            Debug.LogError("[GlitchController] Post Processing Volume is not assigned.", this);
            enabled = false;
            return;
        }

        // volume.profile gives a runtime clone — changes never write back to the asset on disk.
        VolumeProfile profile = _postProcessingVolume.profile;

        // ── Interferences ──────────────────────────────────────────────────
        if (!profile.TryGet(out _interferences))
        {
            _interferences = profile.Add<InterferencesVolume>(true);
            Debug.Log("[GlitchController] InterferencesVolume was absent from the profile — created at runtime.");
        }
        _interferences.active = true;
        _interferences.intensity.overrideState          = true;
        _interferences.distortionAmplitude.overrideState = true;

        // Read the profile's authored intensity as the URP blend ceiling — set once, never changed again.
        _maxInterferencesIntensity = _interferences.intensity.value;
        _interferences.intensity.value = _maxInterferencesIntensity;

        // ── Film Grain ─────────────────────────────────────────────────────
        if (!profile.TryGet(out _filmGrain))
            _filmGrain = profile.Add<FilmGrain>(true);
        _filmGrain.active = true;
        _filmGrain.type.overrideState      = true;
        _filmGrain.intensity.overrideState = true;

        // Store the profile defaults before we touch them at runtime.
        _defaultFilmGrainType  = _filmGrain.type.value;
        _maxFilmGrainIntensity = _filmGrain.intensity.value;
        _filmGrain.intensity.value = _maxFilmGrainIntensity;

        // Start fully off — weight = 0 means URP blends all overrides to zero.
        _postProcessingVolume.weight  = 0f;
        _postProcessingVolume.enabled = false;
    }

    private void OnEnable()
    {
        SuspectCharacter.OnSuspectPresentingUncanny += OnBoothMutantArrived;
        SuspectController.OnCurrentSuspectDespawned  += OnBoothMutantDespawned;
        StartCoroutine(GlitchBurstLoop());
    }

    private void OnDisable()
    {
        SuspectCharacter.OnSuspectPresentingUncanny -= OnBoothMutantArrived;
        SuspectController.OnCurrentSuspectDespawned  -= OnBoothMutantDespawned;

        _boothMutantActive = false;
        _boothMutant       = null;
        _currentWeight     = 0f;
        _targetWeight      = 0f;
        _burstAmplitude    = 0f;

        SetGlitchAudio(false);

        _postProcessingVolume.weight  = 0f;
        _postProcessingVolume.enabled = false;
    }

    private void Update()
    {
        // ── Recompute target on scan interval ──────────────────────────────
        _scanTimer -= Time.deltaTime;
        if (_scanTimer <= 0f)
        {
            _scanTimer    = _scanInterval;
            _targetWeight = ComputeTargetWeight();
        }

        // ── Smooth lerp (asymmetric attack / release) ──────────────────────
        float speed = (_currentWeight < _targetWeight) ? _fadeInSpeed : _fadeOutSpeed;
        _currentWeight = Mathf.Lerp(_currentWeight, _targetWeight, Time.deltaTime * speed);

        if (_currentWeight < 0.001f)
            _currentWeight = 0f;

        // ── Apply ──────────────────────────────────────────────────────────
        bool active = _currentWeight > 0f;
        _postProcessingVolume.enabled = active;

        if (active)
        {
            // weight drives URP blending — all overridden parameters scale proportionally
            // from zero up to the values authored on the profile.
            _postProcessingVolume.weight = _currentWeight;

            // Burst amplitude is written directly; weight already scales it down with distance.
            _interferences.distortionAmplitude.value = _burstAmplitude;

            // Film grain type switches at the proximity threshold — can't be lerped by weight.
            _filmGrain.type.value = _currentWeight >= _filmGrainTypeThreshold
                ? FilmGrainLookup.Large01
                : _defaultFilmGrainType;
        }
    }

    // ── Weight Computation ────────────────────────────────────────────────────

    private float ComputeTargetWeight()
    {
        if (_debugForceGlitch) return 1f;

        Transform origin = PlayerInstance.Instance != null ? PlayerInstance.Instance.transform : null;
        if (origin == null) return 0f;

        float maxSignal = 0f;

        // Booth suspect — tracked by reference so distance scaling works even when
        // InfectionScore is not synced to non-host clients.
        if (_boothMutantActive && _boothMutant != null)
        {
            float signal = SignalForDistance(Vector3.Distance(origin.position, _boothMutant.transform.position));
            if (signal > maxSignal) maxSignal = signal;
        }

        // World proximity — catches roaming mutants and supplements the booth check.
        foreach (var suspect in FindObjectsByType<SuspectCharacter>(FindObjectsSortMode.None))
        {
            if (suspect.InfectionScore < AnomalyController.FULLY_MUTATED_THRESHOLD) continue;

            float signal = SignalForDistance(Vector3.Distance(origin.position, suspect.transform.position));
            if (signal > maxSignal) maxSignal = signal;
        }

        return maxSignal;
    }

    /// <summary>
    /// Returns a 0–1 signal for a given distance.
    /// Full intensity within <see cref="_innerRadius"/>, fades to zero at <see cref="_outerRadius"/>.
    /// </summary>
    private float SignalForDistance(float dist)
    {
        if (dist <= _innerRadius) return 1f;
        return 1f - Mathf.Clamp01((dist - _innerRadius) / Mathf.Max(_outerRadius - _innerRadius, 0.001f));
    }

    // ── Booth Events ──────────────────────────────────────────────────────────

    private void OnBoothMutantArrived(SuspectCharacter suspect, int infectionScore)
    {
        _boothMutantActive = true;
        _boothMutant       = suspect;
    }

    private void OnBoothMutantDespawned()
    {
        _boothMutantActive = false;
        _boothMutant       = null;
    }

    // ── Burst Loop ────────────────────────────────────────────────────────────

    /// <summary>
    /// Randomly fires distortion bursts while the effect has any intensity,
    /// mirroring the pattern used in <see cref="LogoMaterialController"/>.
    /// </summary>
    private IEnumerator GlitchBurstLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(_glitchIntervalMin, _glitchIntervalMax));

            if (_currentWeight < 0.01f) continue;

            float duration = Random.Range(_glitchDurationMin, _glitchDurationMax);
            float rampTime = Mathf.Max(duration * 0.15f, 0.001f);

            SetGlitchAudio(true);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float inRamp  = Mathf.Clamp01(elapsed / rampTime);
                float outRamp = Mathf.Clamp01((duration - elapsed) / rampTime);
                _burstAmplitude = Mathf.Min(inRamp, outRamp) * _glitchBurstIntensity;
                yield return null;
            }

            _burstAmplitude = 0f;
            SetGlitchAudio(false);
        }
    }

    /// <summary>
    /// Enables or disables the glitch AudioSource and seeks to a random position in the clip
    /// so every burst starts from an unpredictable point in the sound.
    /// </summary>
    private void SetGlitchAudio(bool active)
    {
        if (_glitchAudioSource == null || _glitchAudioSource.enabled == active) return;

        _glitchAudioSource.enabled = active;

        if (active && _glitchAudioSource.clip != null)
            _glitchAudioSource.time = Random.Range(0f, _glitchAudioSource.clip.length);
    }

    // ── Gizmos ───────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (PlayerInstance.Instance == null) return;
        Vector3 pos = PlayerInstance.Instance.transform.position;
        Gizmos.color = new Color(0f, 0.9f, 1f, 0.5f);
        Gizmos.DrawWireSphere(pos, _innerRadius);
        Gizmos.color = new Color(0f, 0.9f, 1f, 0.2f);
        Gizmos.DrawWireSphere(pos, _outerRadius);
    }
}
