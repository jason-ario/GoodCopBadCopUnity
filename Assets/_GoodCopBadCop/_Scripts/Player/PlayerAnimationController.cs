using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerAnimationController : NetworkBehaviour
{
    [SerializeField] private Animator bodyAnimator;
    [SerializeField] private Animator armsAnimator;
    private PlayerMovementController _playerMovementController;
    [SerializeField] private GameObject armsOnBody;
    
    [SerializeField] private float animLerpSpeed = 5f;
    
    private float currentMoveX = 0f;
    private float currentMoveZ = 0f;
    
    [SerializeField] Transform headLookAtTransform;
    [SerializeField] Transform chestLookAtTransform;
    
    private NetworkVariable<Vector3> headLookAtPos =
        new NetworkVariable<Vector3>(
            writePerm: NetworkVariableWritePermission.Owner
        );

    private NetworkVariable<Vector3> chestLookAtPos =
        new NetworkVariable<Vector3>(
            writePerm: NetworkVariableWritePermission.Owner
        );
    
    private void Awake()
    {
        _playerMovementController = GetComponent<PlayerMovementController>();
    }
    
    private void Update()
    {
        UpdateAnimations();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (IsLocalPlayer == false)
        {
            armsOnBody.layer = LayerMask.NameToLayer("Default");

            foreach (Transform child in armsOnBody.transform)
            {
                armsOnBody.layer = LayerMask.NameToLayer("Default");
            }
        }
    }

    private void UpdateAnimations()
    {
        if(IsLocalPlayer == false)
        {
            headLookAtTransform.position = headLookAtPos.Value;
            chestLookAtTransform.position = chestLookAtPos.Value;
            return;
        }

        headLookAtPos.Value = headLookAtTransform.position;
        chestLookAtPos.Value = chestLookAtTransform.position;

        // Smoothly lerp between current and target values
        currentMoveX = Mathf.Lerp(currentMoveX, _playerMovementController.MoveXRaw, Time.deltaTime * animLerpSpeed);
        currentMoveZ = Mathf.Lerp(currentMoveZ, _playerMovementController.MoveZRaw, Time.deltaTime * animLerpSpeed);
        
        // Set the smoothed values to the animator
        bodyAnimator.SetFloat("MoveX", currentMoveX);
        bodyAnimator.SetFloat("MoveZ", currentMoveZ);
        armsAnimator.SetFloat("MoveX", currentMoveX);
        armsAnimator.SetFloat("MoveZ", currentMoveZ);
    }

    public void EnableHoldObjectMask()
    {
        bodyAnimator.SetLayerWeight(1, 1);
        armsAnimator.SetLayerWeight(1,1);
        bodyAnimator.SetLayerWeight(2,0);
        armsAnimator.SetLayerWeight(2,0);
    }
    
    public void DisableHoldObjectMask()
    {
        bodyAnimator.SetLayerWeight(1, 0);
        armsAnimator.SetLayerWeight(1,0);
        bodyAnimator.SetLayerWeight(2,0);
        armsAnimator.SetLayerWeight(2,0);
    }

    public void OpenDoor()
    {
        bodyAnimator.SetTrigger("OpenDoor");
        armsAnimator.SetTrigger("OpenDoor");
    }

    public void EnableHoldObjectTwoArmsMask()
    {
        bodyAnimator.SetLayerWeight(1, 0);
        armsAnimator.SetLayerWeight(1,0);
        bodyAnimator.SetLayerWeight(2,1);
        armsAnimator.SetLayerWeight(2,1);
    }
}