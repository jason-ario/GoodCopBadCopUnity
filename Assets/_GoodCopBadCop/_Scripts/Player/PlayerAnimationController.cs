using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Serialization;

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

    [Header("Body Arm Rigs")]
    [SerializeField] private Rig rightArmRig;
    [SerializeField] private Rig leftArmRig;
    [SerializeField] private Rig shoulderRig;

    [SerializeField] private Transform rightArmIKTarget; 
    [SerializeField] private Transform leftArmIKTarget;

    [Header("Camera Arm Rigs")]
    [SerializeField] private Rig camRightArmRig;
    [SerializeField] private Rig camLeftArmRig;
    [SerializeField] private Transform camRightArmIKTarget; 
    [SerializeField] private Transform camLeftArmIKTarget;

    public Rig RightArmRig => rightArmRig;
    public Rig LeftArmRig => leftArmRig;

    public Transform RightArmIKTarget => rightArmIKTarget;
    public Transform LeftArmIKTarget => leftArmIKTarget;
    public Transform RightArmRigIKTarget { get; set; }
    public Transform LeftArmRigIKTarget { get; set; }
    
    public Transform CamRightArmIKTarget => camRightArmIKTarget;
    public Transform CamLeftArmIKTarget => camLeftArmIKTarget;
    public Transform CamRightArmRigIKTarget { get; set; }
    public Transform CamLeftArmRigIKTarget { get; set; }


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
    
    private void LateUpdate()
    {
        if (RightArmRigIKTarget != null)
        {
            rightArmIKTarget.position = RightArmRigIKTarget.position;
            rightArmIKTarget.rotation = RightArmRigIKTarget.rotation;
        }
        
        if (LeftArmRigIKTarget != null)
        {
            leftArmIKTarget.position = LeftArmRigIKTarget.position;
            leftArmIKTarget.rotation = LeftArmRigIKTarget.rotation;
        }
        
        if (CamRightArmRigIKTarget != null)
        {
            camRightArmIKTarget.position = CamRightArmRigIKTarget.position;
            camRightArmIKTarget.rotation = CamRightArmRigIKTarget.rotation;
        }
        
        if (CamLeftArmRigIKTarget != null)
        {
            camLeftArmIKTarget.position = CamLeftArmRigIKTarget.position;
            camLeftArmIKTarget.rotation = CamLeftArmRigIKTarget.rotation;
        }
        
        if (IsOwner == false)
        {
            return;
        }
        else
        {
            UpdateAnimations();
        }
        
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            ShrugEmote();
        }
    }

    void ShrugEmote()
    {
        StartCoroutine(ShrugEmoteCoroutine());
    }

    IEnumerator ShrugEmoteCoroutine()
    {
        SetAnimBool("Shrug", true);
        yield return new WaitForSeconds(1);
        SetAnimBool("Shrug", false);
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

    public void SetRightArmRigWeightSmooth(float smoothWeight, float smoothTime)
    {
        DOTween.Kill(rightArmRig.weight);
        DOTween.To(() => rightArmRig.weight, x => rightArmRig.weight = x, smoothWeight, smoothTime);
        
        if (camRightArmRig != null)
        {
            DOTween.Kill(camRightArmRig.weight);
            DOTween.To(() => camRightArmRig.weight, x => camRightArmRig.weight = x, smoothWeight, smoothTime);
        }
    }
    
    public void SetLeftArmRigWeightSmooth(float smoothWeight, float smoothTime)
    {
        DOTween.Kill(leftArmRig.weight);
        DOTween.To(() => leftArmRig.weight, x => leftArmRig.weight = x, smoothWeight, smoothTime);

        if (camLeftArmRig != null)
        {
            DOTween.Kill(camLeftArmRig.weight);
            DOTween.To(() => camLeftArmRig.weight, x => camLeftArmRig.weight = x, smoothWeight, smoothTime);
        }
    }

    public void SetAnimFloat(string animString, float value)
    {
        bodyAnimator.SetFloat(animString, value);
        armsAnimator.SetFloat(animString, value);
    }

    public void SetAimRigWeightSmooth(float smoothWeight, float smoothTime)
    {
        DOTween.Kill(shoulderRig.weight);
        DOTween.To(() => shoulderRig.weight, x => shoulderRig.weight = x, smoothWeight, smoothTime);

        if (camLeftArmRig != null)
        {
            DOTween.Kill(shoulderRig.weight);
            DOTween.To(() => shoulderRig.weight, x => shoulderRig.weight = x, smoothWeight, smoothTime);
        }
    }
}