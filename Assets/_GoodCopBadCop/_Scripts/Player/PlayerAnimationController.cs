using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerAnimationController : MonoBehaviour
{
    [SerializeField] private Animator bodyAnimator;
    [SerializeField] private Animator armsAnimator;
    private PlayerMovementController _playerMovementController;
    
    [SerializeField] private float animLerpSpeed = 5f;
    
    private float currentMoveX = 0f;
    private float currentMoveZ = 0f;
    
    private void Awake()
    {
        _playerMovementController = GetComponent<PlayerMovementController>();
    }
    
    private void Update()
    {
        UpdateAnimations();
    }
    
    private void UpdateAnimations()
    {
        // Smoothly lerp between current and target values
        currentMoveX = Mathf.Lerp(currentMoveX, _playerMovementController.MoveXRaw, Time.deltaTime * animLerpSpeed);
        currentMoveZ = Mathf.Lerp(currentMoveZ, _playerMovementController.MoveZRaw, Time.deltaTime * animLerpSpeed);
        
        // Set the smoothed values to the animator
        bodyAnimator.SetFloat("MoveX", currentMoveX);
        bodyAnimator.SetFloat("MoveZ", currentMoveZ);
        armsAnimator.SetFloat("MoveX", currentMoveX);
        armsAnimator.SetFloat("MoveZ", currentMoveZ);
    }
}