using System;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Booth drawer. Tap Interact (E / gamepad West) to open, tap again to close.
/// The local client predicts the slide immediately; the server owns the authoritative open state
/// and every other client tweens to it when it changes.
/// </summary>
public class Drawer : Interactable
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip drawerOpenSound;
    [SerializeField] private AudioClip drawerCloseSound;

    [Header("Movement Audio")]
    [Tooltip("Dedicated AudioSource used for the looping slide sound. Kept separate from 'audioSource' so it can loop independently of the open/close one-shots.")]
    [SerializeField] private AudioSource _movementAudioSource;

    [Tooltip("Looping clip played while the drawer is actively sliding.")]
    [SerializeField] private AudioClip _drawerMoveLoopClip;

    [Tooltip("Minimum drawer speed (normalized travel per second, 0=closed to 1=open) required to keep the movement loop playing. Below this, the loop stops.")]
    [SerializeField] private float _moveVelocityThreshold = 0.05f;

    [Header("Drawer Mesh")]
    [Tooltip("The child Transform that physically represents the drawer body (driven by localPosition).")]
    [SerializeField] private Transform _drawerMesh;

    [Tooltip("Local position of the drawer mesh when fully closed.")]
    [SerializeField] private Vector3 _closedPos = Vector3.zero;

    [Tooltip("Local position of the drawer mesh when fully open.")]
    [SerializeField] private Vector3 _openPos = new Vector3(0.001f, 0f, 0.357f);

    [Header("Slide")]
    [Tooltip("Seconds the drawer takes to slide fully open or closed after a tap.")]
    [SerializeField] private float _slideDuration = 0.35f;

    [Tooltip("Easing applied to the open/close slide.")]
    [SerializeField] private Ease _slideEase = Ease.OutCubic;

    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> _isLocked = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>State the mesh is currently heading to on this client (may lead the network value while a local tap is in flight).</summary>
    private bool _targetOpen;

    /// <summary>True while the slide tween is moving the mesh.</summary>
    private bool _isSnapping;

    private Tween _slideTween;

    /// <summary>Normalised drawer position sampled on the previous frame, used to derive travel speed for movement audio. Negative = uninitialised.</summary>
    private float _previousMeshDragT = -1f;

    /// <summary>Edge-trigger guards so the open/close sound plays once per arrival at an end.</summary>
    private bool _hasPlayedOpenEndSound;
    private bool _hasPlayedClosedEndSound;

    /// <summary>Fired locally whenever this drawer transitions to open.</summary>
    public event Action OnOpened;

    // ── Locking ──────────────────────────────────────────────────────────────

    /// <summary>Prevents interaction when true. Safe to call from any client.</summary>
    public void SetLocked(bool locked)
    {
        if (IsServer) _isLocked.Value = locked;
        else SetLockedServerRpc(locked);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetLockedServerRpc(bool locked) => _isLocked.Value = locked;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        // Position is driven manually — disable the Animator so it does not fight the script.
        Animator animator = GetComponent<Animator>();
        if (animator != null)
            animator.enabled = false;

        // Drawer audio plays on every client (driven by the synced mesh motion), so the
        // sources must be fully 3D — a 2D source would be audible map-wide.
        ForceSpatial(audioSource);
        ForceSpatial(_movementAudioSource);
    }

    private static void ForceSpatial(AudioSource source)
    {
        if (source == null) return;
        source.spatialBlend = 1f;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isOpen.OnValueChanged += OnDrawerStateChanged;

        // Late-join / initial state: place instantly, no audio.
        _targetOpen = isOpen.Value;
        if (_drawerMesh != null)
            _drawerMesh.localPosition = _targetOpen ? _openPos : _closedPos;
    }

    public override void OnNetworkDespawn()
    {
        isOpen.OnValueChanged -= OnDrawerStateChanged;
        _slideTween?.Kill();
        _isSnapping = false;

        if (_movementAudioSource != null)
            _movementAudioSource.Stop();

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (_isSnapping)
        {
            UpdateMovementAudio();
        }
        else
        {
            StopMovementLoop();
            _previousMeshDragT = -1f;
        }
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    public override string GetInteractVerb(PlayerInteractionController player)
    {
        if (!string.IsNullOrEmpty(interactVerb)) return interactVerb;
        return _targetOpen ? "Close" : "Open";
    }

    /// <summary>Tap Interact toggles the drawer open/closed.</summary>
    public override void Interact(PlayerInteractionController player)
    {
        if (_isLocked.Value) return;
        base.Interact(player);

        bool newOpen = !_targetOpen;

        // Predict locally so the slide starts on the press frame, then let the server confirm.
        SlideTo(newOpen);
        SetDrawerServerRpc(newOpen);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetDrawerServerRpc(bool open)
    {
        if (_isLocked.Value) return;
        isOpen.Value = open;
    }

    private void OnDrawerStateChanged(bool oldValue, bool newValue)
    {
        SlideTo(newValue);
        if (newValue) OnOpened?.Invoke();
    }

    // ── Drawer position ───────────────────────────────────────────────────────

    private void SlideTo(bool open)
    {
        if (_drawerMesh == null)
        {
            _targetOpen = open;
            return;
        }

        // Already heading there (e.g. our own predicted tap being confirmed by the server).
        if (_targetOpen == open && (_isSnapping || _drawerMesh.localPosition == (open ? _openPos : _closedPos)))
            return;

        _targetOpen = open;
        Vector3 target = open ? _openPos : _closedPos;

        // Scale duration by remaining distance so a mid-slide reversal doesn't feel sluggish.
        float total = Vector3.Distance(_closedPos, _openPos);
        float remaining = Vector3.Distance(_drawerMesh.localPosition, target);
        float duration = total > 0.0001f ? _slideDuration * (remaining / total) : 0f;

        _slideTween?.Kill();

        if (duration <= 0.001f)
        {
            _drawerMesh.localPosition = target;
            _isSnapping = false;
            return;
        }

        _isSnapping = true;
        _slideTween = _drawerMesh.DOLocalMove(target, duration)
            .SetEase(_slideEase)
            .OnComplete(() =>
            {
                // Evaluate audio once at the end so the open/close stinger fires on arrival.
                UpdateMovementAudio();
                _isSnapping = false;
                _slideTween = null;
            });
    }

    // ── Movement audio ────────────────────────────────────────────────────────

    /// <summary>
    /// Drives the looping slide sound and the end-of-track open/close stingers from the
    /// drawer mesh's actual position. Only evaluated while the slide tween is running.
    /// </summary>
    private void UpdateMovementAudio()
    {
        if (_drawerMesh == null) return;

        float currentDragT = Mathf.InverseLerp(_closedPos.z, _openPos.z, _drawerMesh.localPosition.z);
        float deltaTime = Time.deltaTime;

        if (_previousMeshDragT < 0f)
        {
            _previousMeshDragT = currentDragT;
            return;
        }

        if (deltaTime > 0f)
        {
            float speed = Mathf.Abs(currentDragT - _previousMeshDragT) / deltaTime;
            if (speed >= _moveVelocityThreshold) PlayMovementLoop();
            else                                 StopMovementLoop();
        }

        const float endOfTrackEpsilon = 0.001f;
        if (currentDragT >= 1f - endOfTrackEpsilon)
        {
            if (!_hasPlayedOpenEndSound)
            {
                _hasPlayedOpenEndSound = true;
                _hasPlayedClosedEndSound = false;
                StopMovementLoop();
                PlayEndOfTrackSound(open: true);
            }
        }
        else if (currentDragT <= endOfTrackEpsilon)
        {
            if (!_hasPlayedClosedEndSound)
            {
                _hasPlayedClosedEndSound = true;
                _hasPlayedOpenEndSound = false;
                StopMovementLoop();
                PlayEndOfTrackSound(open: false);
            }
        }
        else
        {
            _hasPlayedOpenEndSound = false;
            _hasPlayedClosedEndSound = false;
        }

        _previousMeshDragT = currentDragT;
    }

    private void PlayMovementLoop()
    {
        if (_movementAudioSource == null || _drawerMoveLoopClip == null) return;
        if (_movementAudioSource.isPlaying && _movementAudioSource.clip == _drawerMoveLoopClip) return;

        _movementAudioSource.clip = _drawerMoveLoopClip;
        _movementAudioSource.loop = true;
        _movementAudioSource.Play();
    }

    private void StopMovementLoop()
    {
        if (_movementAudioSource == null) return;
        if (_movementAudioSource.isPlaying) _movementAudioSource.Stop();
    }

    private void PlayEndOfTrackSound(bool open)
    {
        if (audioSource == null) return;
        AudioClip clip = open ? drawerOpenSound : drawerCloseSound;
        if (clip != null) audioSource.PlayOneShot(clip);
    }
}
