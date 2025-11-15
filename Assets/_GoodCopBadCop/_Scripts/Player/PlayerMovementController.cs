using System;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovementController : MonoBehaviour
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

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    { 
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
}