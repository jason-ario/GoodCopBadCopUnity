using System;
using DG.Tweening;
using GoodCopBadCop.Input;
using GoodCopBadCop.Settings;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

public interface IPlayerControlsSettingsReceiver
{
    void ApplyControlSettings(PlayerControlSettings settings);
}

[RequireComponent(typeof(CharacterController))]
public class PlayerMovementController : NetworkBehaviour, IPlayerControlsSettingsReceiver
{
    private CharacterController _characterController;
    [SerializeField] private float characterSpeed;
    [SerializeField] private float runSpeed = 8f;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float maxLookAngle = 80f;
    [SerializeField] private float mouseSmoothing = 10f;
    [SerializeField] private float acceleration = 20f;
    [SerializeField] private float drag = 5f;

    [Header("Controller Settings")]
    [Tooltip("Degrees per second for right stick look at full deflection.")]
    [SerializeField] private float controllerLookSensitivity = 200f;
    private Vector3 targetLookEuler;

    [Header("Recoil Settings")]
    [SerializeField] private float recoilVerticalAmount = 3f;
    [SerializeField] private float recoilHorizontalAmount = 1.5f;
    [SerializeField] private float recoilKickDuration = 0.07f;
    [SerializeField] private float recoilRecoverDuration = 0.2f;

    [Header("Camera Look Down Settings")]
    [SerializeField] private Transform cameraBasePos;
    [SerializeField] private Transform cameraLookDownPos;
    [SerializeField] private float lookDownLerpSpeed = 5f;
    [Tooltip("Camera offset magnitude (local units) at which body lean reaches its maximum. Tune to match your cameraLookDownPos distance.")]
    [SerializeField] private float maxCameraOffsetForLean = 0.3f;

    private Vector3 _recoilRotation; // Procedural offset for recoil
    private float _cameraPitch = 0f;
    private Vector3 _currentVelocity;
    private float _smoothedMouseX;
    private float _smoothedMouseY;
    private float _verticalVelocity;
    private float _baseMouseSensitivity;
    private float _settingsMouseSensitivity = 50f;
    private bool _invertYAxis;
    private EInputActivationMode _crouchMode = EInputActivationMode.Hold;
    private EInputActivationMode _sprintMode = EInputActivationMode.Hold;
    private bool _sprintToggleActive;

    [Header("Gravity Settings")]
    [SerializeField] private float gravity = -20f;

    [Header("Underwater Settings")]
    [Tooltip("Gravity multiplier applied when the camera is inside an underwater zone.")]
    [SerializeField] private float underwaterGravityMultiplier = 0.2f;
    [Tooltip("Movement speed multiplier applied when underwater.")]
    [SerializeField] private float underwaterSpeedMultiplier = 0.55f;
    [Tooltip("Maximum downward velocity while underwater (must be negative).")]
    [SerializeField] private float underwaterTerminalVelocity = -3f;
    [Tooltip("Target upward velocity reached when holding Jump underwater.")]
    [SerializeField] private float underwaterSwimUpSpeed = 3f;
    [Tooltip("How quickly vertical velocity ramps toward swim speed when Jump is held.")]
    [SerializeField] private float underwaterSwimAcceleration = 8f;

    private bool _isUnderwater;

    [Header("Jump Settings")]
    [SerializeField] private float jumpForce = 7f;
    [SerializeField] private AudioClip jumpSound;
    [SerializeField] private AudioClip landSound;
    [Tooltip("How many seconds before actual ground contact the land sound should play, predicted from the current fall speed and a lookahead ground scan.")]
    [SerializeField] private float landSoundAnticipation = 0.5f;

    private bool _wasGrounded;
    private bool _landSoundPlayedForCurrentFall;

    // Input helpers — combine legacy Input Manager with gamepad polling so both
    // keyboard/mouse and controller work simultaneously without migrating to
    // the new Input System's action map callbacks.
    private bool IsJumpHeld => Input.GetButton("Jump") || (Gamepad.current?.buttonSouth.isPressed ?? false);
    private bool IsJumpDown => Input.GetButtonDown("Jump") || (Gamepad.current?.buttonSouth.wasPressedThisFrame ?? false);

    [Header("Ground Check")]
    [Tooltip("Extra distance below the capsule base to scan for ground. Lower = stricter.")]
    [SerializeField] private float groundCheckDistance = 0.08f;
    [Tooltip("Layers treated as ground. Exclude the Player layer to avoid self-detection.")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Crouch Settings")]
    [SerializeField] private Transform camCrouchPos;
    [SerializeField] private Transform camCrouchLookDownPos;
    [SerializeField] private float crouchSpeed = 2f;
    [SerializeField] private float crouchControllerHeight = 1.2f;
    [SerializeField] private float crouchControllerCenterY = 0.6f;

    private float _standingControllerHeight;
    private float _standingControllerCenterY;
    private bool _isCrouching = false;
    public bool IsCrouching => _isCrouching;

    public bool CanMove;
    public bool CanLook;
    
    // Public properties for animation controller to access
    public float MoveXRaw { get; private set; }
    public float MoveZRaw { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsGrounded { get; private set; }

    /// <summary>
    /// Raw physics ground check result for this frame, unaffected by the jump
    /// animation suppression window that <see cref="IsGrounded"/> applies.
    /// Used to drive the animator's "Grounded" parameter so the Fall/Land
    /// transitions react the instant the player actually touches the ground.
    /// </summary>
    public bool RawGrounded { get; private set; }

    /// <summary>
    /// Current vertical look pitch in degrees, clamped to [-maxLookAngle, maxLookAngle].
    /// Negative values = looking up, positive = looking down.
    /// </summary>
    public float CameraPitch => _cameraPitch;
    
    private Vector3 camStartPos;
    private Quaternion camStartRot;
        
    bool canControl = true;

    [Header("Sitting and Standing")]
    [SerializeField] private Transform camSitPos;
    [SerializeField] private Transform camStandPos;
    [SerializeField] private float sitStandDuration = 0.4f;
    [SerializeField] AudioClip sitSound;
    [SerializeField] AudioClip standSound;

    private bool _isSitting = false;
    public bool IsSitting => _isSitting;
    private Chair chairSeatedAt;

    public Transform CameraTransform => cameraTransform;
    
    bool _canSitOrStand = true;

    public void SetCantSitOrStand(bool value)
    {
        _canSitOrStand = value;
    }
    public bool CanControl
    {
        get => canControl;
        set
        {
            // Guard: never re-enable control while a scripted cutscene or dialogue session
            // is active. Interaction coroutines (phone grab, diegetic-view close, etc.) use
            // delayed WaitForSeconds and can call SetCanControl(true) after EnterScriptedDialogueMode
            // has locked things, re-locking the cursor and restoring look. Disabling calls are
            // always allowed so the cutscene entry path works correctly.
            if (value && (ScriptedDialogueRunner.IsScriptedModeActive || DialogueChoiceSystem.IsInDialogueMode))
                return;

            canControl = value;

            if (canControl)
            {
                if (!CanLook) return;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                GetComponent<PlayerInteractionController>().SetReticleActive(true);

            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                GetComponent<PlayerInteractionController>().SetReticleActive(false);
            }
        }
    }

    private PlayerAnimationController _playerAnimationController;
    public PlayerAnimationController PlayerAnimationController => _playerAnimationController;

    private FootstepsAudio _footstepsAudio;
    private PlayerCameraController _playerCameraController;
    [SerializeField] private Camera camera;
    public Camera Camera => camera;

    // Syncs the camera's LOCAL position and rotation (relative to the player root) so
    // spectating clients derive world position from the same interpolated NetworkTransform
    // root as the body mesh — eliminating the camera/body drift caused by tick-rate mismatch.
    private NetworkVariable<Vector3> _netCameraLocalPos =
        new NetworkVariable<Vector3>(writePerm: NetworkVariableWritePermission.Owner);

    private NetworkVariable<Quaternion> _netCameraLocalRot =
        new NetworkVariable<Quaternion>(Quaternion.identity, writePerm: NetworkVariableWritePermission.Owner);

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _playerAnimationController = GetComponent<PlayerAnimationController>();
        _footstepsAudio = GetComponent<FootstepsAudio>();
        _playerCameraController = GetComponent<PlayerCameraController>();
        
        CanMove = true;
        CanLook = true;

        _standingControllerHeight = _characterController.height;
        _standingControllerCenterY = _characterController.center.y;
        _baseMouseSensitivity = mouseSensitivity;
    }

    private void Start()
    {
        camStartPos = cameraTransform.localPosition;
        camStartRot = cameraTransform.localRotation;
        
        // If transforms aren't assigned, create virtual positions
        if (cameraBasePos == null)
        {
            cameraBasePos = cameraTransform;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsLocalPlayer)
        {
            // Deactivate the VCam so it doesn't influence any client's CinemachineBrain.
            cameraTransform.gameObject.SetActive(false);
            // Deactivate the Unity Camera (CinemachineBrain + AudioListener) so remote
            // players don't produce extra render passes or duplicate AudioListener warnings.
            if (camera != null) camera.gameObject.SetActive(false);
        }

        if (IsLocalPlayer)
        {
            GoodCopBadCop.EnvironmentSystem.UnderwaterZone.RegisterPlayerBody(transform);
            // Entry activates on camera submersion (head goes under) so the player
            // falls at full speed through the surface before physics slow down.
            GoodCopBadCop.EnvironmentSystem.UnderwaterZone.OnUnderwaterStateChanged += HandleCameraUnderwaterChanged;
            // Exit + jump fires when the body root (feet) leave the zone so the
            // player swims all the way to the top before launching out.
            GoodCopBadCop.EnvironmentSystem.UnderwaterZone.OnPlayerBodyUnderwaterStateChanged += HandleBodyUnderwaterChanged;
            // Day transitions can happen while the player is mid-sit-animation (e.g. asleep
            // in a chair at end of day); force the animator out of the sitting pose so the
            // next day never starts with a stuck "Sitting" animation.
            CampaignManager.OnDayChanged += HandleDayChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        GoodCopBadCop.EnvironmentSystem.UnderwaterZone.UnregisterPlayerBody(transform);
        GoodCopBadCop.EnvironmentSystem.UnderwaterZone.OnUnderwaterStateChanged -= HandleCameraUnderwaterChanged;
        GoodCopBadCop.EnvironmentSystem.UnderwaterZone.OnPlayerBodyUnderwaterStateChanged -= HandleBodyUnderwaterChanged;
        CampaignManager.OnDayChanged -= HandleDayChanged;
    }

    // Ensures the player animator never carries the "Sitting" pose across a day boundary.
    private void HandleDayChanged(int newDay)
    {
        _isSitting = false;
        _playerAnimationController?.SetAnimBool("Sitting", false);
    }

    // Camera crosses INTO zone → activate underwater physics (player is fully submerged)
    private void HandleCameraUnderwaterChanged(bool isUnderwater)
    {
        if (!isUnderwater || _isUnderwater) return;

        _isUnderwater = true;
        if (_verticalVelocity < -2f)
            _verticalVelocity *= 0.15f;
    }

    // Body root crosses OUT of zone → deactivate and jump if surfacing
    private void HandleBodyUnderwaterChanged(bool isUnderwater)
    {
        if (isUnderwater || !_isUnderwater) return;

        _isUnderwater = false;

        if (IsJumpHeld && !_isCrouching && _verticalVelocity > 0f)
        {
            _verticalVelocity = jumpForce;
            _playerAnimationController?.TriggerJumpAnim();
            SFXController.Instance?.Play(jumpSound);
        }
    }
    

    private void Update()
    {
        if(IsLocalPlayer == false) return;
        if (canControl == false)
        {
            return;
        }
        
        if (CanMove) Move();
        if (CanLook) Rotate();

        if (!_isSitting && CanMove)
        {
            UpdateCrouchInput();
        }

        if (_isSitting && _canSitOrStand)
        {
            bool standUpInput = Input.GetKeyDown(KeyCode.Escape)
                                || (Gamepad.current?.buttonEast.wasPressedThisFrame ?? false);
            if (standUpInput && UIController.Instance.IsPaused == false)
            {
                StandUp();
            }
        }
    }

    void Move() 
    {
        // Keyboard/D-pad input (legacy Input Manager)
        float keyboardXRaw = Input.GetAxisRaw("Horizontal");
        float keyboardZRaw = Input.GetAxisRaw("Vertical");
        float keyboardX    = Input.GetAxis("Horizontal");
        float keyboardZ    = Input.GetAxis("Vertical");

        // Gamepad left stick — combined with keyboard so both devices work simultaneously
        Vector2 leftStick = Gamepad.current?.leftStick.ReadValue() ?? Vector2.zero;
        float MoveX = Mathf.Clamp(keyboardX + leftStick.x, -1f, 1f);
        float MoveZ = Mathf.Clamp(keyboardZ + leftStick.y, -1f, 1f);

        // Store raw values for animation
        MoveXRaw = Mathf.Clamp(keyboardXRaw + leftStick.x, -1f, 1f);
        MoveZRaw = Mathf.Clamp(keyboardZRaw + leftStick.y, -1f, 1f);

        // Check run input - blocked while crouching
        if (_isCrouching)
        {
            _sprintToggleActive = false;
        }

        bool isRunning = !_isCrouching && IsSprintInputActive();
        IsRunning = isRunning;
        if (_playerCameraController != null)
            _playerCameraController.UpdateMovementShake(isRunning);

        // Pick speed
        float currentSpeed = _isCrouching ? crouchSpeed : (isRunning ? runSpeed : characterSpeed);
        if (_isUnderwater) currentSpeed *= underwaterSpeedMultiplier;

        // Calculate desired direction based on input
        Vector3 inputDir = new Vector3(MoveX, 0, MoveZ);
        inputDir = transform.TransformDirection(inputDir);

        // Apply gravity
        bool isGrounded = CheckGrounded();
        RawGrounded = isGrounded;

        if (isGrounded)
        {
            // Landing: transitioned from airborne to grounded while falling
            if (!_wasGrounded && _verticalVelocity < 0f)
            {
                // Fallback in case the predictive lookahead below never caught this fall
                // (e.g. an uneven or steep surface the lookahead ray missed).
                if (!_landSoundPlayedForCurrentFall)
                    SFXController.Instance.Play(landSound);
            }

            _landSoundPlayedForCurrentFall = false;

            if (_isUnderwater && IsJumpHeld && !_isCrouching)
            {
                // Swim up from the seafloor — ramp toward swim speed instead of snapping to -2
                _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, underwaterSwimUpSpeed, underwaterSwimAcceleration * Time.deltaTime);
            }
            else
            {
                _verticalVelocity = -2f; // Small constant to keep grounded

                if (!_isUnderwater && IsJumpDown && !_isCrouching)
                {
                    _verticalVelocity = jumpForce;
                    _playerAnimationController.TriggerJumpAnim();
                    SFXController.Instance.Play(jumpSound);
                }
            }
        }
        else
        {
            float effectiveGravity = _isUnderwater ? gravity * underwaterGravityMultiplier : gravity;
            _verticalVelocity += effectiveGravity * Time.deltaTime;

            if (_isUnderwater)
            {
                // Hold Jump to swim upward through the water column
                if (IsJumpHeld && !_isCrouching)
                    _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, underwaterSwimUpSpeed, underwaterSwimAcceleration * Time.deltaTime);

                if (_verticalVelocity < underwaterTerminalVelocity)
                    _verticalVelocity = underwaterTerminalVelocity;
            }
            else if (!_landSoundPlayedForCurrentFall && _verticalVelocity < 0f)
            {
                // Predict how far the player will fall in the next landSoundAnticipation
                // seconds (using basic kinematics under the current gravity) and scan that
                // far below for ground. If found, play the land sound now so it lands on the
                // player's ear roughly landSoundAnticipation seconds before actual contact.
                float fallSpeed = -_verticalVelocity;
                float lookaheadDistance = fallSpeed * landSoundAnticipation
                    + 0.5f * -effectiveGravity * landSoundAnticipation * landSoundAnticipation;

                if (CheckGroundedAhead(lookaheadDistance))
                {
                    SFXController.Instance.Play(landSound);
                    _landSoundPlayedForCurrentFall = true;
                }
            }
        }

        _wasGrounded = isGrounded;

        // Keep IsGrounded false for the entire jump animation window so systems
        // that read this property (e.g. the animation controller) don't see a
        // grounded flicker the instant the player's feet leave the ground.
        // Also suppress it when underwater: the player is always "swimming", never walking.
        bool jumpAnimActive = _playerAnimationController != null && _playerAnimationController.IsJumpAnimPlaying;
        IsGrounded = isGrounded && !jumpAnimActive && !_isUnderwater;

        Vector3 moveVector = inputDir * currentSpeed + Vector3.up * _verticalVelocity;

        // Apply movement
        _characterController.Move(moveVector * Time.deltaTime);
    }

    /// <summary>
    /// Same downward sphere cast as <see cref="CheckGrounded"/>, but with a caller-supplied
    /// scan distance. Used to predict ground contact before it actually happens (e.g. to
    /// anticipate the land sound) rather than to drive the authoritative grounded state.
    /// </summary>
    private bool CheckGroundedAhead(float distance)
    {
        if (distance <= 0f) return false;

        Vector3 bottomSphereCentre = transform.position
            + _characterController.center
            + Vector3.down * (_characterController.height * 0.5f - _characterController.radius);

        return Physics.SphereCast(
            bottomSphereCentre,
            _characterController.radius * 0.9f,
            Vector3.down,
            out _,
            distance,
            groundMask,
            QueryTriggerInteraction.Ignore
        );
    }

    /// <summary>
    /// Casts a sphere downward from the bottom of the CharacterController capsule.
    /// More accurate than <c>CharacterController.isGrounded</c>, which uses skin width
    /// and can register contact before the visual mesh reaches the ground.
    /// </summary>
    private bool CheckGrounded()
    {
        // Centre of the bottom hemisphere of the capsule
        Vector3 bottomSphereCentre = transform.position
            + _characterController.center
            + Vector3.down * (_characterController.height * 0.5f - _characterController.radius);

        // Slightly smaller radius avoids false positives against steep walls
        return Physics.SphereCast(
            bottomSphereCentre,
            _characterController.radius * 0.9f,
            Vector3.down,
            out _,
            groundCheckDistance,
            groundMask,
            QueryTriggerInteraction.Ignore
        );
    }

    void Rotate()
    {
        float appliedMouseSensitivity = _baseMouseSensitivity * (_settingsMouseSensitivity / 50f);
        float verticalDirection = _invertYAxis ? -1f : 1f;

        // Mouse delta — run through smoothing to eliminate jitter
        float mouseX = Input.GetAxis("Mouse X") * appliedMouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * appliedMouseSensitivity * verticalDirection;
        _smoothedMouseX = Mathf.Lerp(_smoothedMouseX, mouseX, mouseSmoothing * Time.deltaTime);
        _smoothedMouseY = Mathf.Lerp(_smoothedMouseY, mouseY, mouseSmoothing * Time.deltaTime);

        // Gamepad right stick — already a continuous axis value; scale by deltaTime for
        // frame-rate-independent rotation. No extra smoothing layer to avoid added latency.
        Vector2 rightStick = Gamepad.current?.rightStick.ReadValue() ?? Vector2.zero;
        float controllerX =  rightStick.x * controllerLookSensitivity * Time.deltaTime;
        float controllerY =  rightStick.y * controllerLookSensitivity * Time.deltaTime * verticalDirection;

        float totalX = _smoothedMouseX + controllerX;
        float totalY = _smoothedMouseY + controllerY;

        // Rotate player (Y axis) based on horizontal look input
        transform.Rotate(Vector3.up * totalX);
        
        // Rotate camera (X axis) based on vertical look input
        if (cameraTransform != null)
        {
            _cameraPitch -= totalY;
            _cameraPitch = Mathf.Clamp(_cameraPitch, -maxLookAngle, maxLookAngle);
            
            // Combine base aim pitch with procedural recoil rotation
            targetLookEuler = new Vector3(_cameraPitch + _recoilRotation.x, _recoilRotation.y, 0f);
            cameraTransform.localEulerAngles = targetLookEuler;
            
            // Update camera position based on look angle
            UpdateCameraPositionBasedOnLook();
        }
    }

    
    public void ApplyRecoil()
    {
        if (!IsLocalPlayer) return;

        // Reset any existing recoil tween to prevent stacking issues
        DOTween.Kill("recoilRotate");

        Vector3 targetRecoil = new Vector3(-recoilVerticalAmount, UnityEngine.Random.Range(-recoilHorizontalAmount, recoilHorizontalAmount), 0);

        // Sequence: Kick up/side then recover to zero
        DOTween.To(() => _recoilRotation, x => _recoilRotation = x, targetRecoil, recoilKickDuration)
            .SetId("recoilRotate")
            .SetEase(Ease.OutQuad)
            .OnComplete(() => {
                DOTween.To(() => _recoilRotation, x => _recoilRotation = x, Vector3.zero, recoilRecoverDuration)
                    .SetId("recoilRotate")
                    .SetEase(Ease.InOutSine);
            });

        // Positional kickback for feel
        cameraTransform.DOComplete();
        cameraTransform.DOPunchPosition(new Vector3(0, 0, -0.05f), recoilKickDuration + recoilRecoverDuration, 2, 0.5f);
    }

    public void ApplyControlSettings(PlayerControlSettings settings)
    {
        _settingsMouseSensitivity = Mathf.Clamp(
            settings.MouseSensitivity,
            SettingsService.MinimumMouseSensitivity,
            SettingsService.MaximumMouseSensitivity);
        _invertYAxis = settings.InvertYAxis;
        _crouchMode = settings.CrouchMode;
        _sprintMode = settings.SprintMode;

        if (_sprintMode == EInputActivationMode.Hold)
        {
            _sprintToggleActive = false;
        }
    }

    public void SetCanControl(bool value)
    {
        CanControl = value;
        cameraTransform.DOKill();
        transform.DOKill();

        if (!value)
        {
            // Clear the cached raw-input values so FootstepsAudio and the animation
            // controller don't read stale non-zero movement while controls are suspended.
            // CanMove is deliberately left unchanged — it's separate state that must
            // survive the cutscene and be restored on exit.
            MoveXRaw = 0f;
            MoveZRaw = 0f;
            IsRunning = false;
            _sprintToggleActive = false;
            if (_playerCameraController != null)
                _playerCameraController.UpdateMovementShake(false);
        }
    }

    public void SetCanMove(bool value)
    {
        CanMove = value;

        if (!value)
        {
            MoveXRaw = 0f;
            MoveZRaw = 0f;
            IsRunning = false;
            _sprintToggleActive = false;
            if (_playerCameraController != null)
                _playerCameraController.UpdateMovementShake(false);
        }
    }

    public void SetCanLook(bool value)
    {
        // Guard: same race-condition protection as the CanControl setter. Deferred coroutines
        // (dumpster deposit, coin-slot insertion, etc.) call SetCanLook(true) after yielding;
        // if a cutscene started during that yield, look must stay disabled.
        if (value && (ScriptedDialogueRunner.IsScriptedModeActive || DialogueChoiceSystem.IsInDialogueMode))
            return;
        CanLook = value;
    }

    /// <summary>
    /// Resets the camera pitch and local rotation to a neutral forward-looking orientation.
    /// Call this after teleporting the player to a new spawn point so the camera doesn't
    /// retain a stale look angle from a previous location (e.g. after the intro cutscene).
    /// </summary>
    public void ResetCameraRotation()
    {
        if (cameraTransform == null) return;

        _cameraPitch = 0f;
        _recoilRotation = Vector3.zero;
        _smoothedMouseX = 0f;
        _smoothedMouseY = 0f;
        targetLookEuler = Vector3.zero;

        cameraTransform.DOKill();
        cameraTransform.localEulerAngles = Vector3.zero;
    }

    /// <summary>
    /// Locks player movement while still allowing camera look/rotation.
    /// </summary>
    public void SetMovementLocked(bool locked)
    {
        CanMove = !locked;
        CanLook = true;
    }

    public void LookAtTarget(Transform target)
    {
        // Rotate player (Y axis) based on horizontal mouse movement
        Vector3 direction = target.position - transform.position;
        direction.y = 0; // Keep the rotation only on the Y axis
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.DORotateQuaternion(targetRotation, 0.5f);
        }
        
        // Rotate camera (X axis) based on vertical mouse movement
        if (cameraTransform != null)
        {
            // Update _cameraPitch to match the new rotation to prevent snapping when mouse moves
            cameraTransform.DOLookAt(target.position, 0.5f).OnUpdate(SyncPitch);
        }
    }

    /// <summary>
    /// Synchronizes the internal _cameraPitch with the actual local X rotation of the camera.
    /// Use this when the camera is moved by external systems (e.g. DOTween) to ensure
    /// the look-down lean and networked pitch remain accurate.
    /// </summary>
    public void SyncPitch()
    {
        if (cameraTransform == null) return;
        float pitch = cameraTransform.localEulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        _cameraPitch = pitch;
    }

    void UpdateCameraPositionBasedOnLook()
    {
        // Calculate how far down we're looking (0 to 1, where 1 is maximum down)
        float lookDownAmount = Mathf.Clamp01((_cameraPitch) / maxLookAngle);

        // Pick the correct position pair based on crouch state.
        Transform basePosTransform     = (_isCrouching && camCrouchPos         != null) ? camCrouchPos         : cameraBasePos;
        Transform lookDownPosTransform = (_isCrouching && camCrouchLookDownPos != null) ? camCrouchLookDownPos : cameraLookDownPos;

        Vector3 targetPos;

        if (lookDownPosTransform != null)
        {
            targetPos = Vector3.Lerp(basePosTransform.localPosition, lookDownPosTransform.localPosition, lookDownAmount);
        }
        else
        {
            Vector3 forwardOffset = Vector3.forward * lookDownAmount * 0.3f;
            targetPos = basePosTransform.localPosition + forwardOffset;
        }

        // Smoothly lerp the camera position
        cameraTransform.localPosition = Vector3.Lerp(cameraTransform.localPosition, targetPos, lookDownLerpSpeed * Time.deltaTime);

        // Drive body lean based on how far the camera has moved from its base position.
        if (_playerAnimationController != null && maxCameraOffsetForLean > 0f)
        {
            float offsetMagnitude = Vector3.Distance(cameraTransform.localPosition, basePosTransform.localPosition);
            float leanFactor = Mathf.Clamp01(offsetMagnitude / maxCameraOffsetForLean);
            _playerAnimationController.SetLocalBodyLeanFactor(leanFactor);
        }
    }
    
    public void ResetCameraPos(bool instant = true, float duration = 0.5f, UnityAction callback = null)
    {
        cameraTransform.DOKill();

        if (instant)
        {
            cameraTransform.localPosition = _isSitting ? camSitPos.localPosition : camStandPos.localPosition;
            callback?.Invoke();
        }
        else
        {
            Vector3 targetPos = _isSitting ? camSitPos.localPosition : camStandPos.localPosition;
            cameraTransform.DOLocalMove(targetPos, duration).OnComplete(() =>  callback?.Invoke());
            cameraTransform.DOLocalRotate(targetLookEuler, duration);
        }
    }

    private void UpdateCrouchInput()
    {
        bool gamepadCrouchDown = Gamepad.current?.buttonEast.wasPressedThisFrame ?? false;
        bool gamepadCrouchHeld = Gamepad.current?.buttonEast.isPressed ?? false;

        if (_crouchMode == EInputActivationMode.Toggle)
        {
            if (RebindableInput.GetKeyDown(GameAction.Crouch) || gamepadCrouchDown)
            {
                SetCrouching(!_isCrouching);
            }

            return;
        }

        bool crouchHeld = RebindableInput.GetKeyHeld(GameAction.Crouch) || gamepadCrouchHeld;
        if (crouchHeld && !_isCrouching)
            SetCrouching(true);
        else if (!crouchHeld && _isCrouching)
            SetCrouching(false);
    }

    private bool IsSprintInputActive()
    {
        bool sprintHeld    = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                             || (Gamepad.current?.leftStickButton.isPressed ?? false);
        bool sprintPressed = Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift)
                             || (Gamepad.current?.leftStickButton.wasPressedThisFrame ?? false);

        if (_sprintMode == EInputActivationMode.Hold)
        {
            return sprintHeld;
        }

        if (sprintPressed)
        {
            _sprintToggleActive = !_sprintToggleActive;
        }

        return _sprintToggleActive;
    }

    /// <summary>
    /// Sets the crouch state: resizes the CharacterController capsule and drives the animator bool.
    /// Camera position is handled each frame by UpdateCameraPositionBasedOnLook(), which switches
    /// between the standing and crouching position pair automatically.
    /// </summary>
    private void SetCrouching(bool crouch)
    {
        _isCrouching = crouch;

        // Adjust CharacterController capsule height and center.
        _characterController.height = crouch ? crouchControllerHeight : _standingControllerHeight;
        _characterController.center = new Vector3(
            0f,
            crouch ? crouchControllerCenterY : _standingControllerCenterY,
            0f
        );

        // Drive the animator on both body and arms.
        _playerAnimationController.SetAnimBool("IsCrouched", crouch);

        // Procedurally tilt the upper body forward so the spine doesn't curve unnaturally.
        _playerAnimationController.SetCrouchLean(crouch);
    }

    public void Sit(Chair chair)
    {
        if (_isSitting || camSitPos == null) return;

        // Exit crouch cleanly before sitting.
        if (_isCrouching) SetCrouching(false);

        SetMovementLocked(true);
        UIController.Instance.ShowBackButton(StandUp);
        _isSitting = true;
        chairSeatedAt = chair;
        cameraTransform.DOKill();
        cameraTransform.DOLocalMove(camSitPos.localPosition, sitStandDuration).SetEase(Ease.InOutSine);
        _playerAnimationController.SetAnimBool("Sitting", true);
        SFXController.Instance.Play(sitSound);
    }

    public void StandUp()
    {
        if (!_isSitting || camStandPos == null) return;
        _isSitting = false;
        UIController.Instance.HideBackButton();

        transform.DOMove(chairSeatedAt.StandingPos.position, sitStandDuration);
        chairSeatedAt.OnStoodUp();
        chairSeatedAt = null;
        
        SFXController.Instance.Play(standSound);
        cameraTransform.DOKill();
        cameraTransform.DOLocalMove(camStandPos.localPosition, sitStandDuration).SetEase(Ease.InOutSine).OnComplete(() => SetMovementLocked(false));
        _playerAnimationController.SetAnimBool("Sitting", false);
    }

    public void StopMoving()
    {
        SetCanControl(false);
        SetCanMove(false);
    }

    public void MoveCameraTo(Transform cameraTransform, float moveTime = 0.5f)
    {
        CameraTransform.DOMove(cameraTransform.position, moveTime);
        CameraTransform.DORotate(cameraTransform.rotation.eulerAngles, moveTime);
    }

    /// <summary>
    /// Called by FootstepsAudio on the local player. Plays the footstep locally
    /// then notifies all other clients to play it on their copy of this player.
    /// </summary>
    public void PlayFootstepNetworked()
    {
        _footstepsAudio.PlayFootstep();
        PlayFootstepClientRpc();
    }

    [ClientRpc]
    private void PlayFootstepClientRpc()
    {
        // Skip the owner — they already played it above.
        if (IsOwner) return;
        _footstepsAudio.PlayFootstep();
    }

    private void LateUpdate()
    {
        if (!IsSpawned) return;

        if (IsOwner)
        {
            // Publish this client's camera LOCAL position and rotation so spectating
            // clients reconstruct world position as: root.interpolated + local offset.
            // This keeps camera and body in sync because both share the same
            // NetworkTransform root interpolation, eliminating the tick-rate drift.
            if (cameraTransform != null)
            {
                _netCameraLocalPos.Value = cameraTransform.localPosition;
                _netCameraLocalRot.Value = cameraTransform.localRotation;
            }
        }
        else
        {
            // Apply synced local transform so the camera inherits the root's
            // NetworkTransform interpolation automatically.
            if (cameraTransform != null)
            {
                cameraTransform.localPosition = _netCameraLocalPos.Value;
                cameraTransform.localRotation = _netCameraLocalRot.Value;
            }
        }
    }
}