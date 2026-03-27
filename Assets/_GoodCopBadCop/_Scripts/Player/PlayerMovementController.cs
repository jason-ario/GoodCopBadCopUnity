using System;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovementController : NetworkBehaviour
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

    [Header("Recoil Settings")]
    [SerializeField] private float recoilVerticalAmount = 3f;
    [SerializeField] private float recoilHorizontalAmount = 1.5f;
    [SerializeField] private float recoilKickDuration = 0.07f;
    [SerializeField] private float recoilRecoverDuration = 0.2f;

    private Vector3 _recoilRotation; // Procedural offset for recoil
    private float _cameraPitch = 0f;
    private Vector3 _currentVelocity;
    private float _smoothedMouseX;
    private float _smoothedMouseY;
    private float _verticalVelocity;

    [Header("Gravity Settings")]
    [SerializeField] private float gravity = -20f;

    public bool CanMove;
    public bool CanLook;
    
    // Public properties for animation controller to access
    public float MoveXRaw { get; private set; }
    public float MoveZRaw { get; private set; }
    
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
        get { return canControl; }
        set
        {
            canControl = value;

            if (canControl)
            {
                if (CanLook)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    GetComponent<PlayerInteractionController>().SetReticleActive(true);
                }
               
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

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _playerAnimationController = GetComponent<PlayerAnimationController>();
        
        CanMove = true;
        CanLook = true;
        
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Start()
    {
        camStartPos = cameraTransform.localPosition;
        camStartRot = cameraTransform.localRotation;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsLocalPlayer == false)
        {
            cameraTransform.gameObject.SetActive(false);
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

        if (_isSitting && _canSitOrStand)
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                StandUp();
            }
        }
    }

    void Move() 
    {
        // Store input values for animation
        MoveXRaw = Input.GetAxisRaw("Horizontal");
        MoveZRaw = Input.GetAxisRaw("Vertical");
        float MoveX = Input.GetAxis("Horizontal");
        float MoveZ = Input.GetAxis("Vertical");

        // Check run input
        bool isRunning = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // Pick speed
        float currentSpeed = isRunning ? runSpeed : characterSpeed;

        // Calculate desired direction based on input
        Vector3 inputDir = new Vector3(MoveX, 0, MoveZ);
        inputDir = transform.TransformDirection(inputDir);

        // Apply gravity
        if (_characterController.isGrounded)
        {
            _verticalVelocity = -2f; // Small constant to keep grounded
        }
        else
        {
            _verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 moveVector = inputDir * currentSpeed + Vector3.up * _verticalVelocity;

        // Apply movement
        _characterController.Move(moveVector * Time.deltaTime);
    }

    void Rotate()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // Smooth the raw mouse input
        _smoothedMouseX = Mathf.Lerp(_smoothedMouseX, mouseX, mouseSmoothing * Time.deltaTime);
        _smoothedMouseY = Mathf.Lerp(_smoothedMouseY, mouseY, mouseSmoothing * Time.deltaTime);

        // Rotate player (Y axis) based on horizontal mouse movement
        transform.Rotate(Vector3.up * _smoothedMouseX);
        
        // Rotate camera (X axis) based on vertical mouse movement
        if (cameraTransform != null)
        {
            _cameraPitch -= _smoothedMouseY;
            _cameraPitch = Mathf.Clamp(_cameraPitch, -maxLookAngle, maxLookAngle);
            
            // Combine base aim pitch with procedural recoil rotation
            cameraTransform.localEulerAngles = new Vector3(_cameraPitch + _recoilRotation.x, _recoilRotation.y, 0f);
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

    public void SetCanControl(bool value)
    {
        CanControl = value;
        cameraTransform.DOKill();
        transform.DOKill();
    }

    public void SetCanMove(bool value)
    {
        CanMove = value;
    }

    public void SetCanLook(bool value)
    {
        CanLook = value;
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
            cameraTransform.DOLookAt(target.position, 0.5f);
            
            // Optional: Update _cameraPitch to match the new rotation to prevent snapping when mouse moves
            cameraTransform.DOLookAt(target.position, 0.5f).OnUpdate(() => {
                _cameraPitch = cameraTransform.localEulerAngles.x;
                if (_cameraPitch > 180) _cameraPitch -= 360;
            });
        }
    }
    
    public void ResetCameraPos(bool instant = true, float duration = 0.5f)
    {
        cameraTransform.DOKill();

        if (instant)
        {
            cameraTransform.localPosition = _isSitting ? camSitPos.localPosition : camStandPos.localPosition;
        }
        else
        {
            Vector3 targetPos = _isSitting ? camSitPos.localPosition : camStandPos.localPosition;
            cameraTransform.DOLocalMove(targetPos, duration);
        }
    }

    public void Sit(Chair chair)
    {
        if (_isSitting || camSitPos == null) return;
        SetMovementLocked(true);
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
        UIController.Instance.ShowLeaveChairUI(false);

        transform.DOMove(chairSeatedAt.StandingPos.position, sitStandDuration);
        chairSeatedAt.transform.parent = null;
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
}