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
    
    private float _cameraPitch = 0f;
    
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
        
        Vector3 moveDir = new Vector3(MoveX, 0, MoveZ);
        moveDir = transform.TransformDirection(moveDir);
        moveDir *= characterSpeed;

        _characterController.Move(moveDir * Time.deltaTime);

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