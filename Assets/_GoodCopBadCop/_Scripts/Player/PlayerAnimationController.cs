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

    private float targetLayer1Weight = 0f;
    private float targetLayer2Weight = 0f;
    private float currentLayer1Weight = 0f;
    private float currentLayer2Weight = 0f;
    
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
        
        // Smoothly lerp layer weights
        currentLayer1Weight = Mathf.Lerp(currentLayer1Weight, targetLayer1Weight, Time.deltaTime * animLerpSpeed);
        currentLayer2Weight = Mathf.Lerp(currentLayer2Weight, targetLayer2Weight, Time.deltaTime * animLerpSpeed);

        bodyAnimator.SetLayerWeight(1, currentLayer1Weight);
        armsAnimator.SetLayerWeight(1, currentLayer1Weight);
        bodyAnimator.SetLayerWeight(2, currentLayer2Weight);
        armsAnimator.SetLayerWeight(2, currentLayer2Weight);
    }

    public void EnableHoldObjectMask()
    {
        targetLayer1Weight = 1f;
        targetLayer2Weight = 0f;
    }
    
    public void DisableHoldObjectMask()
    {
        targetLayer1Weight = 0f;
        targetLayer2Weight = 0f;
    }

    public void EnableHoldObjectTwoArmsMask()
    {
        targetLayer1Weight = 0f;
        targetLayer2Weight = 1f;
    }

    
    public void OpenDoor()
    {
        SetAnimTrigger("OpenDoor");
    }
    
    public void SetAnimBool(string animString, bool value)
    {
        if (!IsOwner) return;
        
        SetAnimBoolServerRpc(animString, value);
    }

    [ServerRpc]
    private void SetAnimBoolServerRpc(string animString, bool value)
    {
        SetAnimBoolClientRpc(animString, value);
    }

    [ClientRpc]
    private void SetAnimBoolClientRpc(string animString, bool value)
    {
        bodyAnimator.SetBool(animString, value);
        armsAnimator.SetBool(animString, value);
    }

    public void SetAnimTrigger(string animString)
    {
        if (!IsOwner) return;
        
        SetAnimTriggerServerRpc(animString);
    }

    [ServerRpc]
    private void SetAnimTriggerServerRpc(string animString)
    {
        SetAnimTriggerClientRpc(animString);
    }

    [ClientRpc]
    private void SetAnimTriggerClientRpc(string animString)
    {
        bodyAnimator.SetTrigger(animString);
        armsAnimator.SetTrigger(animString);
    }
}