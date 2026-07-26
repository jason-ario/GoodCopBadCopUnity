using System;
using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class Drawer : Interactable, IHeldItemPassthrough
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

    [Header("IK")]
    [Tooltip("Child Transform the right-arm IK anchors to while the player holds the drawer.")]
    [SerializeField] private Transform _rightIkTarget;

    [Tooltip("Child Transform the left-arm IK anchors to while the player holds the drawer.")]
    [SerializeField] private Transform _leftIkTarget;

    [Header("Drag")]
    [Tooltip("Drag speed magnitude. The drag direction is computed automatically from the camera angle relative to the drawer's slide axis — sign is ignored.")]
    [SerializeField] private float _dragSensitivity = 0.01f;

    [Tooltip("Units per second the drawer travels at full right-stick deflection (controller only).")]
    [SerializeField] private float _controllerDragSpeed = 1.5f;

    [Tooltip("Duration of the smooth lerp when remote clients receive a state change.")]
    [SerializeField] private float _snapDuration = 0.2f;

    private const string RightGripBool = "RightGrip";
    private const string LeftGripBool  = "LeftGrip";

    /// <summary>How often (in seconds) the dragging client pushes its position to the server.</summary>
    private const float DragSyncInterval = 0.05f; // ~20 Hz

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

    /// <summary>Continuous drawer position shared across the network (0 = closed, 1 = open).</summary>
    private NetworkVariable<float> _networkDragT = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>Normalised drawer travel: 0 = closed, 1 = fully open.</summary>
    private float _dragT = 0f;

    private float _lastDragSyncTime = -1f;
    private bool _inControl = false;
    private bool _usingRightArm = false;
    private PlayerInteractionController _currentPlayer;
    private Coroutine _exitCoroutine;

    /// <summary>Normalised drawer position sampled on the previous frame, used to derive travel speed for movement audio. Negative = uninitialised.</summary>
    private float _previousMeshDragT = -1f;

    /// <summary>Edge-trigger guards so the open/close sound plays once per arrival at an end, not every frame it's held there.</summary>
    private bool _hasPlayedOpenEndSound = false;
    private bool _hasPlayedClosedEndSound = false;

    /// <summary>True while a non-local client is actively receiving live drag updates from the network (i.e. someone else is dragging it right now).</summary>
    private float _lastRemoteDragSyncTime = -1f;
    private const float RemoteDragSyncActiveWindow = 0.15f; // Slightly wider than DragSyncInterval so brief gaps between syncs don't cut the loop.

    /// <summary>True while the commit snap-tween (DOTween) is actively moving the mesh to its final open/closed position.</summary>
    private bool _isSnapping = false;

    /// <summary>
    /// Fired locally whenever this drawer transitions to open.
    /// </summary>
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
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isOpen.OnValueChanged      += OnDrawerStateChanged;
        _isLocked.OnValueChanged   += OnLockedChanged;
        _networkDragT.OnValueChanged += OnNetworkDragTChanged;

        // Use the continuous position if available, otherwise fall back to binary state.
        if (_drawerMesh != null)
            _drawerMesh.localPosition = Vector3.Lerp(_closedPos, _openPos, _networkDragT.Value);
        else
            SnapDrawerMeshToState(isOpen.Value);
    }

    public override void OnNetworkDespawn()
    {
        isOpen.OnValueChanged      -= OnDrawerStateChanged;
        _isLocked.OnValueChanged   -= OnLockedChanged;
        _networkDragT.OnValueChanged -= OnNetworkDragTChanged;

        if (_movementAudioSource != null)
            _movementAudioSource.Stop();
    }

    private bool LmbHeld => Input.GetMouseButton(0)   || (Gamepad.current?.rightTrigger.isPressed            ?? false);
    private bool LmbUp   => Input.GetMouseButtonUp(0) || (Gamepad.current?.rightTrigger.wasReleasedThisFrame ?? false);

    // ── Input loop ────────────────────────────────────────────────────────────

    private void Update()
    {
        // Only evaluate movement audio while the drawer is actually being manipulated —
        // local drag, a remote player's live drag sync, or the post-commit snap tween.
        // This avoids false triggers from one-off repositioning (e.g. initial network spawn sync).
        if (_inControl || _isSnapping || IsRemoteDragActive())
            UpdateMovementAudio();
        else
            StopMovementLoop();

        if (!_inControl) return;
        if (_currentPlayer == null || !_currentPlayer.IsLocalPlayer) return;

        // Held → scrub drawer position. Accept both LMB / RT and E so either input can drag.
        if (LmbHeld || Input.GetKey(KeyCode.E))
        {
            _dragT = Mathf.Clamp01(_dragT + ComputeDragDelta());
            ApplyDragPosition();
            SyncDragTIfNeeded();
        }

        // Released → commit and exit. Fire when whichever input triggered the grab is released.
        if (LmbUp || Input.GetKeyUp(KeyCode.E))
        {
            CommitDrawer();
            _exitCoroutine = StartCoroutine(ExitDrawerInteraction());
        }
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    /// <summary>
    /// Both E and LMB open the drawer via the same grab sequence.
    /// The drag loop in Update handles release for both inputs.
    /// </summary>
    public override void InteractAlternate(PlayerInteractionController player) => Interact(player);

    public override void Interact(PlayerInteractionController player)
    {
        if (_isLocked.Value) return;
        base.Interact(player);
        if (_inControl) return;

        // Kill any in-progress exit coroutine before it can clean up our new interaction.
        if (_exitCoroutine != null)
        {
            StopCoroutine(_exitCoroutine);
            _exitCoroutine = null;
        }

        PlayerAnimationController anim     = player.playerAnimationController;
        PlayerPickupController    pickup   = player.GetComponent<PlayerPickupController>();

        // Right arm is busy if it's IK-active OR if the hand is physically holding an item
        // (some items occupy the hand without driving the IK rig).
        bool rightArmBusy = anim.RightArmRig.weight > 0.5f || (pickup != null && pickup.HeldObject != null);
        bool leftArmBusy  = anim.LeftArmRig.weight  > 0.5f;

        if (rightArmBusy && leftArmBusy) return; // both arms in active use

        _usingRightArm = leftArmBusy; // prefer left, fall back to right

        // Seed _dragT from the actual current mesh position so the next grab
        // starts from wherever the drawer was left, not from a binary open/closed state.
        _dragT = _drawerMesh != null
            ? Mathf.InverseLerp(_closedPos.z, _openPos.z, _drawerMesh.localPosition.z)
            : (isOpen.Value ? 1f : 0f);
        _currentPlayer = player;
        _inControl = true;

        player.playerMovementController.SetMovementLocked(true);

        if (_usingRightArm)
        {
            if (_rightIkTarget != null)
            {
                anim.RightArmIKTarget       = _rightIkTarget;
                anim.CamRightArmRigIKTarget = _rightIkTarget;
            }
            anim.SetAnimBool(RightGripBool, true);
            anim.EnableRightArmMask();
            anim.SetRightArmRigWeightSmooth(1f, 0.2f);
        }
        else
        {
            if (_leftIkTarget != null)
            {
                anim.LeftArmIKTarget        = _leftIkTarget;
                anim.CamLeftArmRigIKTarget  = _leftIkTarget;
            }
            anim.SetAnimBool(LeftGripBool, true);
            anim.EnableLeftArmMask();
            anim.SetLeftArmRigWeightSmooth(1f, 0.2f);
        }
    }

    private IEnumerator ExitDrawerInteraction()
    {
        // Capture and clear state synchronously before any yield so the Update
        // guard (_inControl) disables input on the very next frame.
        PlayerInteractionController player = _currentPlayer;
        _currentPlayer = null;
        _inControl = false;

        if (player == null) yield break;

        PlayerMovementController movement = player.playerMovementController;
        PlayerAnimationController anim    = player.playerAnimationController;

        if (_usingRightArm)
            anim.SetRightArmRigWeightSmooth(0f, 0.2f);
        else
            anim.SetLeftArmRigWeightSmooth(0f, 0.2f);

        // Wait for the IK weight to finish ramping down before releasing the mask.
        yield return new WaitForSeconds(0.25f);

        if (_usingRightArm)
        {
            anim.RightArmIKTarget       = null;
            anim.CamRightArmRigIKTarget = null;
            anim.SetAnimBool(RightGripBool, false);
            anim.SetRightArmRigWeightSmooth(0f, 0.2f);
            anim.DisableRightArmMask();
        }
        else
        {
            anim.LeftArmIKTarget       = null;
            anim.CamLeftArmRigIKTarget = null;
            anim.SetAnimBool(LeftGripBool, false);
            anim.SetLeftArmRigWeightSmooth(0f, 0.2f);
            anim.DisableLeftArmMask();
        }

        movement.SetMovementLocked(false);
        _exitCoroutine = null;
    }

    // ── Drawer position ───────────────────────────────────────────────────────

    /// <summary>
    /// Commits the open/closed network state based on which side of the midpoint
    /// the drawer was released on. The mesh is NOT snapped — it stays at the
    /// current drag position.
    /// </summary>
    private void CommitDrawer()
    {
        bool newIsOpen = _dragT >= 0.5f;

        if (newIsOpen != isOpen.Value)
        {
            if (newIsOpen) OnOpened?.Invoke();
            SetDrawerServerRpc(newIsOpen, NetworkManager.Singleton.LocalClientId);
        }
    }

    /// <summary>
    /// Returns the _dragT delta for this frame based on mouse input and the camera's
    /// current angle relative to the drawer's slide axis.
    /// - Facing front (slide axis into screen): Mouse Y drives the drag — dragging down opens.
    /// - Facing from the side (slide axis horizontal on screen): mouse is projected onto
    ///   the drawer's screen-space direction.
    /// - Intermediate angles blend smoothly between both.
    /// </summary>
    private float ComputeDragDelta()
    {
        if (_currentPlayer == null || _drawerMesh == null) return 0f;

        Transform cam = _currentPlayer.playerMovementController.CameraTransform;

        // World-space direction the drawer travels when opening.
        Transform parent = _drawerMesh.parent;
        Vector3 slideDir = parent != null
            ? parent.TransformDirection((_openPos - _closedPos).normalized)
            : (_openPos - _closedPos).normalized;

        // Project the slide direction onto the camera's screen plane.
        float screenX  = Vector3.Dot(slideDir, cam.right);
        float screenY  = Vector3.Dot(slideDir, cam.up);
        var   screen2D = new Vector2(screenX, screenY);
        float screenLen = screen2D.magnitude;

        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        // Controller fallback: inject right stick into the projection math.
        // Scale so that full deflection produces _controllerDragSpeed units/sec of _dragT travel.
        if (Mathf.Abs(mouseX) < 0.001f && Mathf.Abs(mouseY) < 0.001f && Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.rightStick.ReadValue();
            if (stick.sqrMagnitude > 0.001f)
            {
                // Divide out _dragSensitivity so the scale cancels when it's applied below.
                float scale = _controllerDragSpeed * Time.deltaTime / Mathf.Max(Mathf.Abs(_dragSensitivity), 0.0001f);
                mouseX = stick.x * scale;
                mouseY = stick.y * scale;
            }
        }

        // Planar delta: how much mouse movement aligns with the drawer's screen-space direction.
        float planarDelta = screenLen > 0.01f
            ? Vector2.Dot(screen2D.normalized, new Vector2(mouseX, mouseY))
            : 0f;

        // Depth fallback: when the drawer slides into/out of screen (front-facing), drag down = open.
        float depthDelta = -mouseY;

        // Blend: depth dominates when facing front (screenLen ≈ 0),
        //        planar dominates when facing from the side (screenLen ≈ 1).
        return Mathf.Lerp(depthDelta, planarDelta, screenLen) * Mathf.Abs(_dragSensitivity);
    }

    private void ApplyDragPosition()
    {
        if (_drawerMesh == null) return;
        _drawerMesh.localPosition = Vector3.Lerp(_closedPos, _openPos, _dragT);
    }

    private void SnapDrawerMeshToState(bool open, float duration = 0f)
    {
        if (_drawerMesh == null) return;
        Vector3 target = open ? _openPos : _closedPos;
        if (duration <= 0f)
        {
            // Instant reposition (e.g. initial network spawn / late-join catch-up) — not player-driven, no movement audio.
            _drawerMesh.localPosition = target;
        }
        else
        {
            _isSnapping = true;
            _drawerMesh.DOLocalMove(target, duration)
                .SetEase(Ease.OutCubic)
                .OnComplete(() => _isSnapping = false);
        }
    }

    // ── Movement audio ────────────────────────────────────────────────────────

    /// <summary>Whether a remote drag sync was received recently enough to be considered "live" right now.</summary>
    private bool IsRemoteDragActive()
    {
        return _lastRemoteDragSyncTime >= 0f
            && (Time.unscaledTime - _lastRemoteDragSyncTime) < RemoteDragSyncActiveWindow;
    }

    /// <summary>
    /// Drives the looping slide sound and the end-of-track open/close stingers from the
    /// drawer mesh's actual position each frame. Only called while the drawer is confirmed
    /// to be under active player control (see the guard in Update()).
    /// </summary>
    private void UpdateMovementAudio()
    {
        if (_drawerMesh == null) return;

        float currentDragT = Mathf.InverseLerp(_closedPos.z, _openPos.z, _drawerMesh.localPosition.z);
        float deltaTime = Time.deltaTime;

        // First frame after enabling — just seed the reference position, don't evaluate speed yet.
        if (_previousMeshDragT < 0f)
        {
            _previousMeshDragT = currentDragT;
            return;
        }

        if (deltaTime > 0f)
        {
            float speed = Mathf.Abs(currentDragT - _previousMeshDragT) / deltaTime;

            if (speed >= _moveVelocityThreshold)
                PlayMovementLoop();
            else
                StopMovementLoop();
        }

        const float endOfTrackEpsilon = 0.001f;
        if (currentDragT >= 1f - endOfTrackEpsilon)
        {
            if (!_hasPlayedOpenEndSound)
            {
                _hasPlayedOpenEndSound = true;
                _hasPlayedClosedEndSound = false;
                PlayEndOfTrackSound(open: true);
            }
        }
        else if (currentDragT <= endOfTrackEpsilon)
        {
            if (!_hasPlayedClosedEndSound)
            {
                _hasPlayedClosedEndSound = true;
                _hasPlayedOpenEndSound = false;
                PlayEndOfTrackSound(open: false);
            }
        }
        else
        {
            // Left both ends — re-arm so the stinger can fire again next time either end is reached.
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

    // ── Networking ────────────────────────────────────────────────────────────

    /// <summary>
    /// Pushes the live drag position to the server at most once per DragSyncInterval.
    /// </summary>
    private void SyncDragTIfNeeded()
    {
        if (Time.unscaledTime - _lastDragSyncTime < DragSyncInterval) return;
        _lastDragSyncTime = Time.unscaledTime;
        UpdateDragTServerRpc(_dragT);
    }

    [ServerRpc(RequireOwnership = false)]
    private void UpdateDragTServerRpc(float dragT)
    {
        _networkDragT.Value = dragT;
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetDrawerServerRpc(bool open, ulong senderClientId)
    {
        isOpen.Value = open;
        _networkDragT.Value = open ? 1f : 0f;
        BroadcastDrawerStateClientRpc(open, senderClientId);
    }

    [ClientRpc]
    private void BroadcastDrawerStateClientRpc(bool open, ulong excludeClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == excludeClientId) return;
        SnapDrawerMeshToState(open, _snapDuration);
        if (open) OnOpened?.Invoke();
    }

    private void OnDrawerStateChanged(bool oldValue, bool newValue)
    {
        // Catch-up for late-joining clients that missed the ClientRpc.
        SnapDrawerMeshToState(newValue, _snapDuration);
        if (newValue) OnOpened?.Invoke();
    }

    /// <summary>
    /// Applied on all non-controlling clients every time the server updates the live drag position.
    /// </summary>
    private void OnNetworkDragTChanged(float oldVal, float newVal)
    {
        if (_inControl) return; // local player is driving — don't fight the input
        _lastRemoteDragSyncTime = Time.unscaledTime;
        if (_drawerMesh == null) return;
        _drawerMesh.localPosition = Vector3.Lerp(_closedPos, _openPos, newVal);
    }

    private void OnLockedChanged(bool oldValue, bool newValue) { }
}
