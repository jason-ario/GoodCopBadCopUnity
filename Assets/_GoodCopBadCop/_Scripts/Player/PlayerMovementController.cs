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
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float maxLookAngle = 80f;
    [SerializeField] private float acceleration = 20f;
    [SerializeField] private float drag = 5f;

    private float _cameraPitch = 0f;
    private Vector3 _currentVelocity;
    
    // Public properties for animation controller to access
    public float MoveXRaw { get; private set; }
    public float MoveZRaw { get; private set; }
    
    bool canControl = true;

    public bool CanMove;
    public bool CanLook;
    
    public Transform CameraTransform => cameraTransform;
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

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
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
        
        Move();
        Rotate();
    }

    void Move()
    {
        // Store input values for animation
        MoveXRaw = Input.GetAxisRaw("Horizontal");
        MoveZRaw = Input.GetAxisRaw("Vertical");
        float MoveX = Input.GetAxis("Horizontal");
        float MoveZ = Input.GetAxis("Vertical");
        
        // Calculate desired direction based on input
        Vector3 inputDir = new Vector3(MoveX, 0, MoveZ);
        inputDir = transform.TransformDirection(inputDir);

        if (inputDir.magnitude > 0.1f)
        {
            // Apply acceleration (pushing the chair)
            _currentVelocity += inputDir * acceleration * Time.deltaTime;
        }

        // Apply drag/friction (wheels slowing down)
        _currentVelocity -= _currentVelocity * drag * Time.deltaTime;

        // Clamp speed to characterSpeed
        if (_currentVelocity.magnitude > characterSpeed)
        {
            _currentVelocity = _currentVelocity.normalized * characterSpeed;
        }

        // Apply movement
        _characterController.Move(_currentVelocity * Time.deltaTime);

        return;
    }

    void Rotate()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
        
        // Rotate player (Y axis) based on horizontal mouse movement
        transform.Rotate(Vector3.up * mouseX);
        
        // Rotate camera (X axis) based on vertical mouse movement
        if (cameraTransform != null)
        {
            _cameraPitch -= mouseY;
            _cameraPitch = Mathf.Clamp(_cameraPitch, -maxLookAngle, maxLookAngle);
            cameraTransform.localEulerAngles = new Vector3(_cameraPitch, 0f, 0f);
        }
    }

    public void SetCanControl(bool value)
    {
        CanControl = value;
        MoveXRaw = 0;
        MoveZRaw = 0;
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
}