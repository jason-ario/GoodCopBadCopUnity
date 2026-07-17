using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Plays looping radiation-intensity sounds based on the rate at which the player is
/// accumulating radiation (radiation units per second).
///
/// Threshold guide (default passiveRadiationPerSecond is ~0.15/s):
///   Medium : hotspot is actively irradiating the player at a moderate pace.
///   High   : player is in a strong radiation source or radiation is stacking fast.
///
/// NOTE: Radiation is server-authoritative and not replicated via a NetworkVariable,
/// so audio currently only plays on the host. When <see cref="PlayerRadiation"/> exposes
/// a networked radiation value, gate on <c>IsOwner</c> instead of <c>IsServer</c>.
/// </summary>
[RequireComponent(typeof(PlayerRadiation))]
public class PlayerRadiationAudio : NetworkBehaviour
{
    [Header("Audio Clips")]
    [SerializeField] private AudioClip mediumRadiationClip;
    [SerializeField] private AudioClip highRadiationClip;

    [Header("Rate Thresholds (radiation units per second)")]
    [Tooltip("Radiation gain rate above which the medium sound plays.")]
    [SerializeField] private float mediumRateThreshold = 1f;
    [Tooltip("Radiation gain rate above which the high sound plays (overrides medium).")]
    [SerializeField] private float highRateThreshold = 3f;

    [Header("Smoothing")]
    [Tooltip("EMA time constant in seconds. Lower = more responsive, higher = smoother.")]
    [SerializeField] private float rateSmoothingWindow = 0.4f;

    [Header("Audio")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    // ── Internals ──────────────────────────────────────────────────────────────

    private PlayerRadiation _playerRadiation;
    private AudioSource _audioSource;

    private float _previousRadiation;
    private float _smoothedRate;

    private enum RadiationSoundState { None, Medium, High }
    private RadiationSoundState _currentState = RadiationSoundState.None;

    // ── Unity lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _playerRadiation = GetComponent<PlayerRadiation>();

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.loop = true;
        _audioSource.spatialBlend = 0f; // 2D — personal headphones-style effect
        _audioSource.playOnAwake = false;
        _audioSource.volume = volume;
    }

    private void Update()
    {
        // Radiation state is only authoritative on the server — match that gate.
        if (!IsServer) return;

        float current = _playerRadiation.CurrentRadiation;

        float instantRate = (current - _previousRadiation) / Time.deltaTime;
        _previousRadiation = current;

        // Clamp negative rates (pill drain) to zero so draining never triggers sounds.
        instantRate = Mathf.Max(0f, instantRate);

        // Exponential moving average to smooth per-frame spikes.
        float alpha = Mathf.Clamp01(Time.deltaTime / Mathf.Max(rateSmoothingWindow, 0.001f));
        _smoothedRate = Mathf.Lerp(_smoothedRate, instantRate, alpha);

        UpdateSound(_smoothedRate);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        StopLoop();
    }

    // ── Sound state machine ────────────────────────────────────────────────────

    private void UpdateSound(float rate)
    {
        RadiationSoundState target;

        if (rate >= highRateThreshold)
            target = RadiationSoundState.High;
        else if (rate >= mediumRateThreshold)
            target = RadiationSoundState.Medium;
        else
            target = RadiationSoundState.None;

        if (target == _currentState) return;

        _currentState = target;

        switch (target)
        {
            case RadiationSoundState.High:
                PlayLoop(highRadiationClip);
                break;
            case RadiationSoundState.Medium:
                PlayLoop(mediumRadiationClip);
                break;
            case RadiationSoundState.None:
                StopLoop();
                break;
        }
    }

    private void PlayLoop(AudioClip clip)
    {
        if (clip == null)
        {
            Debug.LogWarning($"[PlayerRadiationAudio] AudioClip is not assigned for state '{_currentState}'.");
            return;
        }

        if (_audioSource.clip == clip && _audioSource.isPlaying) return;

        _audioSource.clip = clip;
        _audioSource.Play();
    }

    private void StopLoop()
    {
        _audioSource.Stop();
        _audioSource.clip = null;
    }
}
