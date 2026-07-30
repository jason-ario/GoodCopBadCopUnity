using System.Collections;
using UnityEngine;

/// <summary>
/// Controls the turning knob on the electrical panel.
///
/// Rotation progress ranges 0 (Off) → 1 (On). The diegetic controller feeds
/// screen-space angle deltas each frame via <see cref="AddDragDelta"/>. When the
/// player releases the mouse, <see cref="OnRelease"/> springs the knob back to Off
/// unless progress has reached <see cref="IsAtOnPosition"/>.
/// </summary>
public class TurningNobController : MonoBehaviour
{
    [Header("Transforms")]
    [Tooltip("The knob mesh transform that visually rotates.")]
    [SerializeField] private Transform _nobMesh;

    [Tooltip("Marker whose local rotation represents the knob at the Off position.")]
    [SerializeField] private Transform _offReference;

    [Tooltip("Marker whose local rotation represents the knob at the On position.")]
    [SerializeField] private Transform _onReference;

    [Header("Feel")]
    [Tooltip("Total screen-space rotation (degrees) required to travel from Off all the way to On.")]
    [SerializeField] private float _degreesForFullTravel = 360f;

    [Tooltip("Spring-back speed in normalised-progress units per second.")]
    [SerializeField] private float _springBackSpeed = 1.5f;

    [Tooltip("Progress fraction that counts as 'at the On position'.")]
    [SerializeField, Range(0.85f, 1f)] private float _onThreshold = 0.95f;

    [Header("Audio")]
    [Tooltip("AudioSource used to play the knob sounds. Falls back to a component on this GameObject if left empty.")]
    [SerializeField] private AudioSource _audioSource;

    [Tooltip("Looping sound played while the knob is being turned.")]
    [SerializeField] private AudioClip _rotatingSound;

    [Tooltip("One-shot sound played the moment the knob reaches its On position.")]
    [SerializeField] private AudioClip _fullyRotatedSound;

    [Tooltip("Additional one-shot sound played alongside _fullyRotatedSound when the knob reaches its On position.")]
    [SerializeField] private AudioClip _fullyRotatedSoundSecondary;

    // ─── State ────────────────────────────────────────────────────────────────

    private float _progress; // 0 = Off, 1 = On
    private Coroutine _springCoroutine;

    /// <summary>True when the knob has been turned far enough to trigger power restore.</summary>
    public bool IsAtOnPosition => _progress >= _onThreshold;

    private void Awake()
    {
        if (_audioSource == null)
            _audioSource = GetComponent<AudioSource>();
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Immediately snaps the knob to the Off position. Called on power outage.</summary>
    public void SnapToOff()
    {
        CancelSpring();
        StopRotatingSound();
        _progress = 0f;
        ApplyRotation();
    }

    /// <summary>
    /// Advances or retracts the knob by a screen-space angle delta (degrees).
    /// Clockwise screen rotation (negative delta) moves toward On.
    /// Call this each frame while the player is dragging.
    /// </summary>
    public void AddDragDelta(float screenAngleDelta)
    {
        CancelSpring();

        bool wasAtOnPosition = IsAtOnPosition;

        // Negative screenAngleDelta = CW = advancing toward On.
        _progress = Mathf.Clamp01(_progress - screenAngleDelta / _degreesForFullTravel);
        ApplyRotation();

        if (!wasAtOnPosition && IsAtOnPosition)
            PlayFullyRotatedSound();
        else if (!IsAtOnPosition)
            PlayRotatingSound();
    }

    /// <summary>Called when the player releases the drag. Springs back if not yet at On.</summary>
    public void OnRelease()
    {
        StopRotatingSound();
        if (IsAtOnPosition) return;
        _springCoroutine = StartCoroutine(SpringBackRoutine());
    }

    /// <summary>
    /// Forces the knob to spring back to Off regardless of its current position. Used when the
    /// puzzle resets because the knob reached On while not all circuit switches were On.
    /// </summary>
    public void ForceSpringBack()
    {
        StopRotatingSound();
        CancelSpring();
        _springCoroutine = StartCoroutine(SpringBackRoutine());
    }

    // ─── Private ─────────────────────────────────────────────────────────────

    private void ApplyRotation()
    {
        if (_nobMesh == null) return;

        Quaternion from = _offReference != null
            ? _offReference.localRotation
            : Quaternion.Euler(90f, 0f, 0f);

        Quaternion to = _onReference != null
            ? _onReference.localRotation
            : Quaternion.Euler(90f, 270f, 0f);

        _nobMesh.localRotation = Quaternion.Lerp(from, to, _progress);
    }

    private IEnumerator SpringBackRoutine()
    {
        while (_progress > 0f)
        {
            _progress = Mathf.MoveTowards(_progress, 0f, _springBackSpeed * Time.deltaTime);
            ApplyRotation();
            yield return null;
        }

        _springCoroutine = null;
    }

    private void CancelSpring()
    {
        if (_springCoroutine == null) return;
        StopCoroutine(_springCoroutine);
        _springCoroutine = null;
    }

    private void PlayRotatingSound()
    {
        if (_audioSource == null || _rotatingSound == null) return;
        if (_audioSource.isPlaying && _audioSource.clip == _rotatingSound) return;

        _audioSource.clip = _rotatingSound;
        _audioSource.loop = true;
        _audioSource.Play();
    }

    private void StopRotatingSound()
    {
        if (_audioSource == null) return;
        if (_audioSource.isPlaying && _audioSource.clip == _rotatingSound)
            _audioSource.Stop();
    }

    private void PlayFullyRotatedSound()
    {
        StopRotatingSound();
        if (_audioSource == null) return;

        if (_fullyRotatedSound != null)
            _audioSource.PlayOneShot(_fullyRotatedSound);
        if (_fullyRotatedSoundSecondary != null)
            _audioSource.PlayOneShot(_fullyRotatedSoundSecondary);
    }
}
