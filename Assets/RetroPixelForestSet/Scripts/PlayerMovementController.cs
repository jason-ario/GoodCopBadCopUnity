using UnityEngine;
using UnityEngine.InputSystem;

namespace RetroPixelForestSet {
    /// <summary>
    /// Simple player movement controller.
    /// It requires character controller, orientation transform, and input actions to be explicitly set in inspector.
    /// </summary>
    public class PlayerMovementController : MonoBehaviour
    {
        private const float maxFallingSpeed = 2;
        private const float playerSpeed = 5f;
        private const float jumpHeight = 1.5f;
        private const float gravityValue = -9.81f;

        private Vector3 playerVelocity = Vector3.zero;

        [Header("Input Actions")]
        public InputAction moveAction;
        public InputAction jumpAction;

        public Transform orientation;
        public CharacterController characterController;

        private void OnEnable()
        {
            moveAction.Enable();
            jumpAction.Enable();
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void OnDisable()
        {
            moveAction.Disable();
            jumpAction.Disable();
        }

        private void Update()
        {
            var isGrounded = characterController.isGrounded;
            if (isGrounded && playerVelocity.y < -maxFallingSpeed)
            {
                playerVelocity.y = -maxFallingSpeed;
            }
            var input = moveAction.ReadValue<Vector2>();
            var targetForward = new Vector3(orientation.forward.x, 0, orientation.forward.z).normalized;
            var targetRight = new Vector3(orientation.right.x, 0, orientation.right.z).normalized;
            var move = targetForward * input.y + targetRight * input.x;
            move = Vector3.ClampMagnitude(move, 1);
            if (isGrounded && jumpAction.WasPressedThisFrame())
            {
                playerVelocity.y = Mathf.Sqrt(jumpHeight * -2 * gravityValue);
            }
            playerVelocity.y += gravityValue * Time.deltaTime;
            var finalMove = move * playerSpeed + Vector3.up * playerVelocity.y;
            characterController.Move(finalMove * Time.deltaTime);
        }
    }
}