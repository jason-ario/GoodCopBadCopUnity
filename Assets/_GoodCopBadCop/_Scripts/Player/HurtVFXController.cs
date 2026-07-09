using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Temporary legacy audio bridge for local player hurt clips.
/// Fullscreen and camera damage feedback now flow through GoodCopBadCop.Effects.
/// </summary>
public class HurtVFXController : NetworkBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;

    [Tooltip("Clips to play when the player is hurt. A random clip is chosen each time.")]
    [SerializeField] private AudioClip[] _hurtClips;

    [Range(0f, 0.5f)]
    [SerializeField] private float _pitchRandomness = 0.1f;

    private PlayerHealth _playerHealth;
    private ScreenDamage _screenDamage;
    private float _previousHealth;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
            return;

        _playerHealth = GetComponent<PlayerHealth>();
        if (_playerHealth == null)
        {
            Debug.LogError("[HurtVFXController] PlayerHealth component not found on the same GameObject.", this);
            return;
        }

        // Defer subscription until after all Start() calls have run so PlayerHealth
        // has completed its initial network spawn notifications.
        StartCoroutine(InitAfterStart());
    }

    private IEnumerator InitAfterStart()
    {
        // Wait one frame so every MonoBehaviour's Start() has executed.
        yield return null;

        _screenDamage = UIController.Instance != null ? UIController.Instance.ScreenDamage : null;
        _previousHealth = _playerHealth.Health;
        _playerHealth.OnHealthChanged += HandleHealthChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (_playerHealth != null)
            _playerHealth.OnHealthChanged -= HandleHealthChanged;
    }

    /// <summary>
    /// Plays the local hurt feedback without mutating networked health.
    /// Intended for editor/debug tooling that needs to preview damage presentation.
    /// </summary>
    public void PreviewDamageFeedback(float damageAmount, string sourceLabel = null)
    {
        if (!IsOwner)
            return;

        EnsurePreviewReferences();

        if (_playerHealth == null)
        {
            Debug.LogWarning("[HurtVFXController] Cannot preview damage feedback: PlayerHealth is missing.", this);
            return;
        }

        if (_screenDamage != null)
        {
            float currentHealth = _playerHealth.Health;
            float previewHealth = Mathf.Clamp(
                currentHealth - Mathf.Max(0f, damageAmount),
                1f,
                _playerHealth.MaxHealth);

            // Reset the asset's internal health first so repeating the same preview
            // still registers as a fresh hit and retriggers blur/blood feedback.
            _screenDamage.CurrentHealth = currentHealth;
            _screenDamage.CurrentHealth = previewHealth;
        }
        else
        {
            Debug.LogWarning("[HurtVFXController] Cannot preview screen damage: UIController.ScreenDamage is missing.", this);
        }
        PlayHurtAudio();

        if (!string.IsNullOrEmpty(sourceLabel))
            Debug.Log($"[HurtVFXController] Previewed damage feedback: {sourceLabel} ({damageAmount}).", this);
    }

    /// <summary>Plays legacy hurt audio when the owner's health decreases.</summary>
    private void HandleHealthChanged()
    {
        float currentHealth = _playerHealth.Health;

        if (currentHealth < _previousHealth && currentHealth > 0f)
            PlayHurtAudio();

        _previousHealth = currentHealth;
    }

    private void EnsurePreviewReferences()
    {
        if (_playerHealth == null)
            _playerHealth = GetComponent<PlayerHealth>();

        if (_screenDamage == null && UIController.Instance != null)
            _screenDamage = UIController.Instance.ScreenDamage;
    }

    /// <summary>Plays a random clip from <see cref="_hurtClips"/> with slight pitch variation.
    /// Skips playback if a clip is already playing.</summary>
    private void PlayHurtAudio()
    {
        if (_audioSource == null || _hurtClips == null || _hurtClips.Length == 0)
            return;

        if (_audioSource.isPlaying)
            return;

        AudioClip clip = _hurtClips[Random.Range(0, _hurtClips.Length)];
        _audioSource.pitch = 1f + Random.Range(-_pitchRandomness, _pitchRandomness);
        _audioSource.PlayOneShot(clip);
    }
}
