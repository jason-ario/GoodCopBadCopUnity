using DG.Tweening;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A lever-style power switch located at the power station.
///
/// Interaction rules:
///   – The player grabs the handle and drags Mouse Y to pull it down.
///   – On release the handle snaps to the nearest end (up or down).
///   – When committed to the DOWN position, a loud completion sound plays on all
///     clients. If <see cref="FuseBoxPuzzleController.IsReady"/>, the server also
///     calls <see cref="ElectricityController.PowerOn"/> to restore electricity.
///   – A <see cref="Reset"/> method (server-only) snaps the switch back to the UP
///     position — call this when a new power outage begins.
///
/// Setup notes:
///   - Requires a <see cref="NetworkObject"/> on the same prefab root.
///   - Assign <see cref="_fuseBoxController"/> and <see cref="_electricityController"/>.
///   - Assign the handle child to <see cref="_handle"/> (rotates on drag).
///   - Optionally add child Transforms for camera, IK targets, and look target.
/// </summary>
public class PowerSwitch : Interactable, IHeldItemPassthrough
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private FuseBoxPuzzleController _fuseBoxController;
    [SerializeField] private ElectricityController   _electricityController;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [Tooltip("Plays on all clients when the switch is committed to the DOWN position.")]
    [SerializeField] private AudioClip _activateSound;
    [SerializeField] private AudioClip _switchOnSound;
    [SerializeField] private AudioClip _switchOffSound;

    [Header("Camera & IK")]
    [Tooltip("Child Transform the camera DOTweens to during the interaction. Optional.")]
    [SerializeField] private Transform _camPos;
    [Tooltip("Right-arm IK target while the player holds the switch. Optional.")]
    [SerializeField] private Transform _rightIkTarget;
    [Tooltip("Left-arm IK target while the player holds the switch. Optional.")]
    [SerializeField] private Transform _leftIkTarget;
    [Tooltip("Head look-at pin during the interaction. Falls back to this transform if unset.")]
    [SerializeField] private Transform _lookTarget;
    [SerializeField] private float _cameraMoveDuration   = 0.5f;
    [SerializeField] private float _cameraReturnDuration = 0.25f;

    [Header("Handle")]
    [Tooltip("The child Transform that visually represents the switch handle.")]
    [SerializeField] private Transform _handle;
    [Tooltip("Local euler angles when the switch is fully UP (resting position).")]
    [SerializeField] private Vector3 _upRot    = new Vector3( 60f, -180f, 0f);
    [Tooltip("Local euler angles when the switch is fully DOWN (activated position).")]
    [SerializeField] private Vector3 _downRot  = new Vector3(-60f, -180f, 0f);
    [Tooltip("Mouse Y sensitivity multiplier during dragging.")]
    [SerializeField] private float _dragSensitivity = 0.05f;
    [Tooltip("Duration of the snap tween when committing to an end position.")]
    [SerializeField] private float _snapDuration = 0.1f;

    // ── Network state ─────────────────────────────────────────────────────────

    /// <summary>True while the switch is in the activated (down) position.</summary>
    private NetworkVariable<bool> _isDown = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ── Local interaction state ───────────────────────────────────────────────

    /// <summary>Normalised handle position: 0 = up, 1 = down.</summary>
    private float _dragT = 0f;
    private bool _localIsDown;
    private bool _inControl     = false;
    private bool _isInteractable = true;
    private bool _usingRightArm  = true;
    private PlayerInteractionController _currentPlayer;
    private Coroutine _exitCoroutine;

    private const string RightGripBool = "RightGrip";
    private const string LeftGripBool  = "LeftGrip";

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isDown.OnValueChanged += OnSwitchStateChanged;
        SnapHandleToState(_isDown.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isDown.OnValueChanged -= OnSwitchStateChanged;
    }

    private bool LmbHeld => Input.GetMouseButton(0)   || (Gamepad.current?.rightTrigger.isPressed            ?? false);
    private bool LmbUp   => Input.GetMouseButtonUp(0) || (Gamepad.current?.rightTrigger.wasReleasedThisFrame ?? false);

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_inControl) return;
        if (_currentPlayer == null || !_currentPlayer.IsLocalPlayer) return;

        if (LmbHeld)
        {
            // Dragging down = positive Mouse Y maps to negative because "down" is visually pulling.
            _dragT = Mathf.Clamp01(_dragT - Input.GetAxis("Mouse Y") * _dragSensitivity);
            ApplyDragRotation();
            CheckAudioThreshold();
        }

        if (LmbUp)
        {
            CommitSwitch();
            _exitCoroutine = StartCoroutine(ExitSwitchView());
        }
    }

    // ── Interact ──────────────────────────────────────────────────────────────

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        if (!_isInteractable) return;
        if (_inControl) return;

        if (_exitCoroutine != null)
        {
            StopCoroutine(_exitCoroutine);
            _exitCoroutine = null;
            player.playerMovementController.CameraTransform.DOKill();
        }

        PlayerAnimationController anim   = player.playerAnimationController;
        PlayerPickupController    pickup = player.GetComponent<PlayerPickupController>();

        bool rightArmBusy = anim.RightArmRig.weight > 0.5f || (pickup != null && pickup.HeldObject != null);
        bool leftArmBusy  = anim.LeftArmRig.weight  > 0.5f;
        if (rightArmBusy && leftArmBusy) return;

        _usingRightArm = !rightArmBusy;
        _dragT         = _isDown.Value ? 1f : 0f;
        _localIsDown   = _isDown.Value;
        _currentPlayer = player;
        _inControl     = true;

        StartCoroutine(EnterSwitchSequence(player));
    }

    // ── Camera / IK enter & exit ──────────────────────────────────────────────

    private IEnumerator EnterSwitchSequence(PlayerInteractionController player)
    {
        PlayerMovementController  movement = player.playerMovementController;
        PlayerAnimationController anim     = player.playerAnimationController;

        movement.SetCanControl(false);
        movement.LookAtTarget(transform);

        Transform lookPoint = _lookTarget != null ? _lookTarget : transform;
        anim.OverrideHeadLookAt(lookPoint.position);

        Transform ikTarget = _usingRightArm ? _rightIkTarget : _leftIkTarget;
        if (ikTarget != null)
        {
            if (_usingRightArm)
            {
                anim.RightArmIKTarget       = ikTarget;
                anim.CamRightArmRigIKTarget = ikTarget;
            }
            else
            {
                anim.LeftArmIKTarget       = ikTarget;
                anim.CamLeftArmRigIKTarget = ikTarget;
            }
        }

        if (_camPos != null)
        {
            movement.CameraTransform.DOMove(_camPos.position, _cameraMoveDuration);
            movement.CameraTransform.DORotate(_camPos.rotation.eulerAngles, _cameraMoveDuration)
                .OnUpdate(movement.SyncPitch);
        }

        yield return new WaitForSeconds(0.1f);
        if (!_inControl) yield break;

        anim.SetAnimBool(_usingRightArm ? RightGripBool : LeftGripBool, true);

        if (_usingRightArm)
        {
            anim.EnableRightArmMask();
            anim.SetRightArmRigWeightSmooth(1f, 0.2f);
        }
        else
        {
            anim.EnableLeftArmMask();
            anim.SetLeftArmRigWeightSmooth(1f, 0.2f);
        }
    }

    private IEnumerator ExitSwitchView()
    {
        if (!_inControl) yield break;
        _inControl = false;

        PlayerInteractionController player = _currentPlayer;
        _currentPlayer = null;
        if (player == null) yield break;

        PlayerMovementController  movement = player.playerMovementController;
        PlayerAnimationController anim     = player.playerAnimationController;

        movement.CameraTransform.DOKill();

        anim.SetAnimBool(_usingRightArm ? RightGripBool : LeftGripBool, false);

        if (_usingRightArm)
        {
            anim.RightArmIKTarget       = null;
            anim.CamRightArmRigIKTarget = null;
            anim.SetRightArmRigWeightSmooth(0f, 0.2f);
            anim.DisableRightArmMask();
        }
        else
        {
            anim.LeftArmIKTarget       = null;
            anim.CamLeftArmRigIKTarget = null;
            anim.SetLeftArmRigWeightSmooth(0f, 0.2f);
            anim.DisableLeftArmMask();
        }

        anim.OverrideHeadLookAt(null);
        anim.SetBodyLeanDirect(0f);
        movement.ResetCameraPos(false, _cameraReturnDuration);

        yield return new WaitForSeconds(_cameraReturnDuration);

        movement.SetCanControl(true);
        _exitCoroutine = null;
    }

    // ── Drag helpers ──────────────────────────────────────────────────────────

    private void ApplyDragRotation()
    {
        if (_handle == null) return;
        _handle.localRotation = Quaternion.Lerp(
            Quaternion.Euler(_upRot),
            Quaternion.Euler(_downRot),
            _dragT
        );
    }

    /// <summary>Plays audio feedback when crossing the drag thresholds.</summary>
    private void CheckAudioThreshold()
    {
        if (_dragT >= 0.9f && !_localIsDown)
        {
            _localIsDown = true;
            if (_audioSource != null && _switchOffSound != null)
                _audioSource.PlayOneShot(_switchOffSound);
        }
        else if (_dragT <= 0.1f && _localIsDown)
        {
            _localIsDown = false;
            if (_audioSource != null && _switchOnSound != null)
                _audioSource.PlayOneShot(_switchOnSound);
        }
    }

    /// <summary>
    /// Snaps _dragT to the nearest end and sends an RPC if the state changed.
    /// Activating the DOWN position triggers the power-restore attempt on the server.
    /// </summary>
    private void CommitSwitch()
    {
        bool newIsDown = _dragT >= 0.5f;
        _dragT = newIsDown ? 1f : 0f;

        if (_handle != null)
        {
            _handle.DOLocalRotate(newIsDown ? _downRot : _upRot, _snapDuration)
                .SetEase(Ease.OutBack);
        }

        // Sync audio for any threshold not yet crossed during the drag.
        if (newIsDown != _localIsDown)
        {
            _localIsDown = newIsDown;
            if (_audioSource != null)
            {
                AudioClip clip = newIsDown ? _switchOffSound : _switchOnSound;
                if (clip != null) _audioSource.PlayOneShot(clip);
            }
        }

        // Always send the RPC — server guards against redundant state sets.
        SetSwitchServerRpc(newIsDown, NetworkManager.Singleton.LocalClientId);
    }

    private void SnapHandleToState(bool isDown)
    {
        if (_handle == null) return;
        _handle.localRotation = Quaternion.Euler(isDown ? _downRot : _upRot);
    }

    // ── Network ───────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void SetSwitchServerRpc(bool isDown, ulong senderClientId)
    {
        _isDown.Value = isDown;

        // Broadcast visuals to all other clients.
        BroadcastSwitchStateClientRpc(isDown, senderClientId);

        // Only the DOWN commit triggers the power-restore attempt.
        if (!isDown) return;

        // Play the big activation sound on all clients regardless of fuse state,
        // then conditionally restore power.
        PlayActivateSoundClientRpc();

        if (_fuseBoxController != null && _fuseBoxController.IsReady
            && _electricityController != null && !_electricityController.IsPowerOn)
        {
            _electricityController.PowerOn();
            Debug.Log("[PowerSwitch] Fuse box ready — power restored.");
        }
        else
        {
            Debug.Log("[PowerSwitch] Committed DOWN — fuse box not ready or power already on.");
        }
    }

    [ClientRpc]
    private void BroadcastSwitchStateClientRpc(bool isDown, ulong excludeClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == excludeClientId) return;

        SnapHandleToState(isDown);

        if (_audioSource != null)
        {
            AudioClip clip = isDown ? _switchOffSound : _switchOnSound;
            if (clip != null) _audioSource.PlayOneShot(clip);
        }
    }

    [ClientRpc]
    private void PlayActivateSoundClientRpc()
    {
        if (_audioSource != null && _activateSound != null)
            _audioSource.PlayOneShot(_activateSound);
    }

    private void OnSwitchStateChanged(bool oldValue, bool newValue)
    {
        // Late-join catch-up.
        SnapHandleToState(newValue);
    }

    // ── Server utilities ──────────────────────────────────────────────────────

    /// <summary>
    /// Resets the switch to the UP position on all clients.
    /// Call this server-side at the start of a new power outage.
    /// </summary>
    public void Reset()
    {
        if (!IsServer) return;
        if (!_isDown.Value) return;
        _isDown.Value = false;
        BroadcastSwitchStateClientRpc(false, ulong.MaxValue);
    }

    /// <summary>Enables or disables player interaction. Server-only.</summary>
    public void SetInteractable(bool interactable)
    {
        if (!IsServer) return;
        _isInteractable = interactable;
        Highlight(false);
        SetInteractableClientRpc(interactable);
    }

    [ClientRpc]
    private void SetInteractableClientRpc(bool interactable)
    {
        _isInteractable = interactable;
        Highlight(false);
    }
}
