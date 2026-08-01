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
/// NOTE: PlayerRadiation.CurrentRadiation is now replicated via a NetworkVariable, so this
/// component correctly plays each player's own radiation ticker on their own machine. Gating on
/// <c>IsOwner</c> (rather than <c>IsServer</c>) is still required — on the host, IsServer is true
/// for every player's NetworkObject, so gating on IsServer would make the host compute and play
/// every player's radiation audio simultaneously instead of just its own.
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
        // Only the locally-owned player's radiation ticker should play on this machine.
        // Gating on IsServer here (instead of IsOwner) was the bug: on the host, IsServer
        // is true for every player's NetworkObject, so the host ended up computing and
        // playing every player's radiation audio simultaneously, not just its own.
        if (!IsOwner) return;

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
