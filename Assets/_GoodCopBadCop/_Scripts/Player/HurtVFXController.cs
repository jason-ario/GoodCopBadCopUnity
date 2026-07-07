using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Keeps <see cref="UIController.ScreenDamage"/> in sync with <see cref="PlayerHealth"/>
/// and plays a random hurt sound whenever the local owner takes damage.
/// Also triggers a Cinemachine impulse shake via <see cref="PlayerCameraController"/>.
/// Only reacts on the owning client; non-owners are ignored entirely.
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
    private PlayerCameraController _cameraController;
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

        _cameraController = GetComponentInChildren<PlayerCameraController>();
        if (_cameraController == null)
            Debug.LogWarning("[HurtVFXController] PlayerCameraController not found - hit impulse will be skipped.", this);

        // Defer subscription until after all Start() calls have run so ScreenDamage
        // is fully initialised (its animator field is assigned in Start()).
        StartCoroutine(InitAfterStart());
    }

    private IEnumerator InitAfterStart()
    {
        // Wait one frame so every MonoBehaviour's Start() has executed.
        yield return null;

        if (UIController.Instance == null || UIController.Instance.ScreenDamage == null)
        {
            Debug.LogError("[HurtVFXController] UIController.Instance or its ScreenDamage is not available.", this);
            yield break;
        }

        _screenDamage = UIController.Instance.ScreenDamage;
        _previousHealth = _playerHealth.Health;
        _screenDamage.CurrentHealth = _previousHealth;

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
        _cameraController?.TriggerHitImpulse();

        if (!string.IsNullOrEmpty(sourceLabel))
            Debug.Log($"[HurtVFXController] Previewed damage feedback: {sourceLabel} ({damageAmount}).", this);
    }

    /// <summary>Syncs screen damage and plays audio when the owner's health decreases.</summary>
    private void HandleHealthChanged()
    {
        float currentHealth = _playerHealth.Health;

        _screenDamage.CurrentHealth = currentHealth;

        if (currentHealth < _previousHealth)
        {
            PlayHurtAudio();
            _cameraController?.TriggerHitImpulse();
        }

        _previousHealth = currentHealth;
    }

    private void EnsurePreviewReferences()
    {
        if (_playerHealth == null)
            _playerHealth = GetComponent<PlayerHealth>();

        if (_cameraController == null)
            _cameraController = GetComponentInChildren<PlayerCameraController>();

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
