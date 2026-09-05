using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class Lever : Interactable, IHeldItemPassthrough
{
    [SerializeField] private AudioSource leverAudio;
    [SerializeField] private AudioClip leverOnSound;
    [SerializeField] private AudioClip leverOffSound;
    [SerializeField] private ShutterController shutter;

    [Header("Camera & IK")]
    [Tooltip("Child Transform the player camera DOTweens to during the interaction.")]
    [SerializeField] private Transform _camPos;

    [Tooltip("Child Transform the right-arm IK anchors to while the player holds the lever.")]
    [SerializeField] private Transform _rightIkTarget;

    [Tooltip("Child Transform the left-arm IK anchors to while the player holds the lever.")]
    [SerializeField] private Transform _leftIkTarget;

    [Tooltip("World Transform the player's head look-at is pinned to. Leave empty to fall back to the lever's own transform.")]
    [SerializeField] private Transform _lookTarget;

    [Tooltip("Seconds the camera takes to reach _camPos.")]
    [SerializeField] private float _cameraMoveDuration = 0.5f;

    [Tooltip("Seconds the camera takes to return to the normal position after releasing.")]
    [SerializeField] private float _cameraReturnDuration = 0.25f;

    [Header("Lever Arm")]
    [Tooltip("The child Transform that visually represents the lever arm.")]
    [SerializeField] private Transform _leverArm;

    [Tooltip("Local euler angles of the lever arm when fully UP (shutter open).")]
    [SerializeField] private Vector3 _topRot = new Vector3(60f, -180f, 0f);

    [Tooltip("Local euler angles of the lever arm when fully DOWN (shutter closed).")]
    [SerializeField] private Vector3 _bottomRot = new Vector3(-60f, -180f, 0f);

    [Tooltip("Multiplier applied to raw Mouse Y axis input. Higher = faster lever travel per mouse movement.")]
    [SerializeField] private float _dragSensitivity = 0.05f;

    [Tooltip("Units per second the lever travels at full right-stick deflection (controller only).")]
    [SerializeField] private float _controllerDragSpeed = 1.5f;

    [Tooltip("Duration of the snap tween when the lever commits to an end position on mouse release.")]
    [SerializeField] private float _snapDuration = 0.1f;

    private const string RightGripBool  = "RightGrip";
    private const string LeftGripBool   = "LeftGrip";

    private NetworkVariable<bool> _isUp = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsUp => _isUp.Value;

    /// <summary>Normalised lever position: 0 = bottom/down, 1 = top/up.</summary>
    private float _dragT = 0f;

    /// <summary>
    /// Tracks whether the local client has already triggered an open or close during the
    /// current drag. Seeded from <see cref="_isUp"/> on interaction start.
    /// </summary>
    private bool _localShutterOpen;

    private bool _inControl = false;
    private bool _usingRightArm = true;
    private bool _isInteractable = true;
    private PlayerInteractionController _currentPlayer;
    private Coroutine _exitCoroutine;

    public override void OnNetworkSpawn()
    {
        _isUp.OnValueChanged += OnLeverStateChanged;
        SnapLeverArmToState(_isUp.Value);

        if (_isUp.Value)
            shutter.OpenShutter();
        else
            shutter.CloseShutter();
    }

    public override void OnNetworkDespawn()
    {
        _isUp.OnValueChanged -= OnLeverStateChanged;
    }

    protected override void Awake()
    {
        base.Awake();

        // Rotation is driven manually — disable the Animator so it does not fight the script.
        Animator animator = GetComponent<Animator>();
        if (animator != null)
            animator.enabled = false;
    }

    private bool LmbHeld => Input.GetMouseButton(0)   || (Gamepad.current?.rightTrigger.isPressed            ?? false);

    private void Update()
    {
        if (!_inControl) return;
        if (_currentPlayer == null || !_currentPlayer.IsLocalPlayer) return;

        // The pause menu captures the control state that existed when it opened. Do not run
        // this interaction's exit while paused, or its delayed restore can be overwritten by
        // the pause menu's saved "control disabled" state on unpause.
        if (UIController.Instance != null && UIController.Instance.IsPaused) return;

        // Test the current held state instead of relying solely on the one-frame release event.
        // Release events can be missed while focus or pause input is captured; once gameplay
        // resumes, an inactive button must still complete this interaction and restore control.
        if (!LmbHeld)
        {
            CommitLever();
            _exitCoroutine = StartCoroutine(ExitLeverView());
            return;
        }

        // Drag while LMB / RT is held — accumulate into _dragT.
        // Mouse Y is already a per-frame delta; controller stick is continuous so scale by Time.deltaTime.
        float mouseY = Input.GetAxis("Mouse Y");
        float stickY = Gamepad.current?.rightStick.ReadValue().y ?? 0f;
        float delta  = Mathf.Abs(mouseY) > 0.001f
            ? mouseY * _dragSensitivity
            : stickY * _controllerDragSpeed * Time.deltaTime;
        _dragT = Mathf.Clamp01(_dragT + delta);
        ApplyDragRotation();
        CheckShutterThreshold();
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        if (!_isInteractable) return;
        if (_inControl) return;

        // Stop any in-flight exit coroutine before writing new interaction state.
        // Also kill the camera return tween it may have started so the new entry tween wins.
        if (_exitCoroutine != null)
        {
            StopCoroutine(_exitCoroutine);
            _exitCoroutine = null;
            player.playerMovementController.CameraTransform.DOKill();
        }

        PlayerAnimationController   anim   = player.playerAnimationController;
        PlayerPickupController      pickup = player.GetComponent<PlayerPickupController>();

        // Right arm is busy if IK-active or physically holding an item.
        bool rightArmBusy = anim.RightArmRig.weight > 0.5f || (pickup != null && pickup.HeldObject != null);
        bool leftArmBusy  = anim.LeftArmRig.weight  > 0.5f;

        if (rightArmBusy && leftArmBusy) return;

        _usingRightArm = !rightArmBusy;

        _dragT = _isUp.Value ? 1f : 0f;
        _localShutterOpen = _isUp.Value;
        _currentPlayer = player;
        _inControl = true;

        StartCoroutine(EnterLeverSequence(player));
    }

    private IEnumerator EnterLeverSequence(PlayerInteractionController player)
    {
        PlayerMovementController movement = player.playerMovementController;
        PlayerAnimationController anim    = player.playerAnimationController;

        movement.SetCanControl(false);
        movement.LookAtTarget(transform);

        Transform lookPoint = _lookTarget != null ? _lookTarget : transform;
        anim.OverrideHeadLookAt(lookPoint.position);

        // Set the IK target for whichever arm we're using (body + camera).
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

        // Start the grab reach shortly after camera begins moving.
        yield return new WaitForSeconds(0.1f);
        if (!_inControl) yield break; // Player already released — ExitLeverView handles cleanup.

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

    private IEnumerator ExitLeverView()
    {
        if (!_inControl) yield break;

        _inControl = false;

        PlayerInteractionController player = _currentPlayer;
        _currentPlayer = null;

        if (player == null) yield break;

        PlayerMovementController movement = player.playerMovementController;
        PlayerAnimationController anim    = player.playerAnimationController;

        // Kill any in-progress camera tweens before starting the return.
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

        // Exit immediately — restore head look and lean, then return the camera.
        anim.OverrideHeadLookAt(null);
        anim.SetBodyLeanDirect(0f);
        movement.ResetCameraPos(false, _cameraReturnDuration);

        // Wait for the camera return tween before re-enabling controls.
        yield return new WaitForSeconds(_cameraReturnDuration);

        // If pause opened during the return tween, it captured this interaction's disabled
        // state. Restoring now would be overwritten by ClosePauseMenu and strand the player.
        while (UIController.Instance != null && UIController.Instance.IsPaused)
            yield return null;

        movement.SetCanControl(true);
        _exitCoroutine = null;
    }

    /// <summary>
    /// Snaps _dragT to the nearest end, tweens the arm there, and syncs the shutter
    /// and network state. Shutter is driven against _localShutterOpen so it never
    /// fires twice for a state already applied during the drag.
    /// </summary>
    private void CommitLever()
    {
        bool newIsUp = _dragT >= 0.5f;
        _dragT = newIsUp ? 1f : 0f;

        if (_leverArm != null)
        {
            _leverArm.DOLocalRotate(newIsUp ? _topRot : _bottomRot, _snapDuration)
                .SetEase(Ease.OutBack);
        }

        // If the committed state doesn't match what the local client already applied via
        // threshold (e.g. released at 0.6 — above 0.5 but never crossed 0.9), sync now.
        if (newIsUp != _localShutterOpen)
        {
            _localShutterOpen = newIsUp;
            if (newIsUp) shutter.OpenShutter(); else shutter.CloseShutter();
            leverAudio.PlayOneShot(newIsUp ? leverOnSound : leverOffSound);
        }

        // Only send RPC if the network variable needs to change.
        if (newIsUp != _isUp.Value)
            SetLeverServerRpc(newIsUp, NetworkManager.Singleton.LocalClientId);
    }

    /// <summary>
    /// Fires shutter open/close at the 90 % / 10 % drag thresholds during a live drag.
    /// Uses hysteresis so crossing back into the dead zone does not re-trigger.
    /// </summary>
    private void CheckShutterThreshold()
    {
        if (_dragT >= 0.9f && !_localShutterOpen)
        {
            _localShutterOpen = true;
            shutter.OpenShutter();
            leverAudio.PlayOneShot(leverOnSound);
        }
        else if (_dragT <= 0.1f && _localShutterOpen)
        {
            _localShutterOpen = false;
            shutter.CloseShutter();
            leverAudio.PlayOneShot(leverOffSound);
        }
    }

    private void ApplyDragRotation()
    {
        if (_leverArm == null) return;
        _leverArm.localRotation = Quaternion.Lerp(
            Quaternion.Euler(_bottomRot),
            Quaternion.Euler(_topRot),
            _dragT
        );
    }

    private void SnapLeverArmToState(bool isUp)
    {
        if (_leverArm == null) return;
        _leverArm.localRotation = Quaternion.Euler(isUp ? _topRot : _bottomRot);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetLeverServerRpc(bool isUp, ulong senderClientId)
    {
        _isUp.Value = isUp;
        BroadcastLeverStateClientRpc(isUp, senderClientId);
    }

    [ClientRpc]
    private void BroadcastLeverStateClientRpc(bool isUp, ulong excludeClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == excludeClientId) return;

        SnapLeverArmToState(isUp);
        leverAudio.PlayOneShot(isUp ? leverOnSound : leverOffSound);

        if (isUp)
            shutter.OpenShutter();
        else
            shutter.CloseShutter();
    }

    private void OnLeverStateChanged(bool oldValue, bool newValue)
    {
        // Catch-up for late-joining clients that missed the ClientRpc.
        SnapLeverArmToState(newValue);

        if (newValue)
            shutter.OpenShutter();
        else
            shutter.CloseShutter();
    }

    /// <summary>
    /// Enables or disables player interaction with the lever on all clients.
    /// Non-interactable levers suppress highlighting and silently reject Interact calls.
    /// Server-only.
    /// </summary>
    public void SetInteractable(bool interactable)
    {
        if (!IsServer) return;
        _isInteractable = interactable;
        Highlight(false);
        SetInteractableClientRpc(interactable);
    }

    /// <summary>
    /// Animates the lever arm to the open (up) position over <paramref name="duration"/> seconds
    /// and syncs the network state to open on all clients.
    /// The NetworkVariable change propagates to all clients via OnLeverStateChanged, which
    /// opens the shutter — so calling ShutterController.OpenShutter() separately is not required.
    /// Server-only.
    /// </summary>
    public void AnimateOpenServerSide(float duration = 1f)
    {
        if (!IsServer) return;
        _isUp.Value = true;
        AnimateLeverArmClientRpc(true, duration);
    }

    [ClientRpc]
    private void SetInteractableClientRpc(bool interactable)
    {
        _isInteractable = interactable;
        Highlight(false);
    }

    [ClientRpc]
    private void AnimateLeverArmClientRpc(bool isUp, float duration)
    {
        if (_leverArm == null) return;
        _leverArm.DOLocalRotate(isUp ? _topRot : _bottomRot, duration).SetEase(Ease.InOutSine);
    }

    /// <summary>
    /// Raises the lever on the server and broadcasts visuals to all clients,
    /// opening the shutter. Must be called on the server.
    /// </summary>
    public void OpenServerSide()
    {
        if (!IsServer) return;
        _isUp.Value = true;
        BroadcastLeverStateClientRpc(true, ulong.MaxValue);
    }

    /// <summary>
    /// Resets the lever to the down state on the server and broadcasts to all clients.
    /// No-ops if the lever is already down to avoid spurious audio.
    /// </summary>
    public void Reset()
    {
        if (!IsServer) return;
        if (!_isUp.Value) return;
        _isUp.Value = false;
        BroadcastLeverStateClientRpc(false, ulong.MaxValue);
    }
}
