using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Rendering;
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
    private float targetLayer4Weight = 0f;
    private float currentLayer1Weight = 0f;
    private float currentLayer2Weight = 0f;
    private float currentLayer4Weight = 0f;

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
    public Rig CamRightArmRig => camRightArmRig;

    public Rig LeftArmRig => leftArmRig;
    public Rig CamLeftArmRig => camLeftArmRig;


    public Transform RightArmIKTarget => rightArmIKTarget;
    public Transform LeftArmIKTarget => leftArmIKTarget;

    public Transform RightArmRigIKTarget { get; set; } //USE THIS ONE

    public Transform LeftArmRigIKTarget { get; set; } // USE THIS ONE

    public Transform CamRightArmIKTarget
    {
        get => camRightArmIKTarget;
        set
        {
            camRightArmIKTarget = value;
        }
    } 
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

    private NetworkVariable<float> netMoveX =
        new NetworkVariable<float>(writePerm: NetworkVariableWritePermission.Owner);

    private NetworkVariable<float> netMoveZ =
        new NetworkVariable<float>(writePerm: NetworkVariableWritePermission.Owner);

    private NetworkVariable<bool> netIsRunning =
        new NetworkVariable<bool>(writePerm: NetworkVariableWritePermission.Owner);

    private NetworkVariable<float> netLayer1Weight =
        new NetworkVariable<float>(writePerm: NetworkVariableWritePermission.Owner);

    private NetworkVariable<float> netLayer2Weight =
        new NetworkVariable<float>(writePerm: NetworkVariableWritePermission.Owner);

    private NetworkVariable<float> netLayer4Weight =
        new NetworkVariable<float>(writePerm: NetworkVariableWritePermission.Owner);
    
    private Coroutine rightRigOnOffCoroutine;
    private Coroutine leftRigOnOffCoroutine;

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
        else
        {
            armsOnBody.GetComponent<SkinnedMeshRenderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            // Hide the head bone so it doesn't clip into the local player's camera view.
            Transform headBone = bodyAnimator.GetBoneTransform(HumanBodyBones.Head);
            if (headBone != null)
            {
                headBone.localScale = Vector3.zero;
            }
            else
            {
                Debug.LogWarning("[PlayerAnimationController] Head bone not found on bodyAnimator. Ensure the avatar is configured as Humanoid.", this);
            }
        }
    }

    private void UpdateAnimations()
    {
        if(IsLocalPlayer == false)
        {
            headLookAtTransform.position = headLookAtPos.Value;
            chestLookAtTransform.position = chestLookAtPos.Value;

            // Apply movement and running state from the owner to this proxy's animators.
            bodyAnimator.SetFloat("MoveX", netMoveX.Value);
            bodyAnimator.SetFloat("MoveZ", netMoveZ.Value);
            armsAnimator.SetFloat("MoveX", netMoveX.Value);
            armsAnimator.SetFloat("MoveZ", netMoveZ.Value);
            bodyAnimator.SetBool("IsRunning", netIsRunning.Value);

            // Apply layer weights from the owner.
            bodyAnimator.SetLayerWeight(1, netLayer1Weight.Value);
            armsAnimator.SetLayerWeight(1, netLayer1Weight.Value);
            bodyAnimator.SetLayerWeight(2, netLayer2Weight.Value);
            armsAnimator.SetLayerWeight(2, netLayer2Weight.Value);
            bodyAnimator.SetLayerWeight(4, netLayer4Weight.Value);
            armsAnimator.SetLayerWeight(4, netLayer4Weight.Value);
            return;
        }

        headLookAtPos.Value = headLookAtTransform.position;
        chestLookAtPos.Value = chestLookAtTransform.position;

        // Smoothly lerp between current and target values
        currentMoveX = Mathf.Lerp(currentMoveX, _playerMovementController.MoveXRaw, Time.deltaTime * animLerpSpeed);
        currentMoveZ = Mathf.Lerp(currentMoveZ, _playerMovementController.MoveZRaw, Time.deltaTime * animLerpSpeed);

        bool isRunning = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        netMoveX.Value = currentMoveX;
        netMoveZ.Value = currentMoveZ;
        netIsRunning.Value = isRunning;

        bodyAnimator.SetBool("IsRunning", isRunning);
        
        // Set the smoothed values to the animator
        bodyAnimator.SetFloat("MoveX", currentMoveX);
        bodyAnimator.SetFloat("MoveZ", currentMoveZ);
        armsAnimator.SetFloat("MoveX", currentMoveX);
        armsAnimator.SetFloat("MoveZ", currentMoveZ);
        
        // Smoothly lerp layer weights
        currentLayer1Weight = Mathf.Lerp(currentLayer1Weight, targetLayer1Weight, Time.deltaTime * animLerpSpeed);
        currentLayer2Weight = Mathf.Lerp(currentLayer2Weight, targetLayer2Weight, Time.deltaTime * animLerpSpeed);
        currentLayer4Weight = Mathf.Lerp(currentLayer4Weight, targetLayer4Weight, Time.deltaTime * animLerpSpeed);

        netLayer1Weight.Value = currentLayer1Weight;
        netLayer2Weight.Value = currentLayer2Weight;
        netLayer4Weight.Value = currentLayer4Weight;

        bodyAnimator.SetLayerWeight(1, currentLayer1Weight);
        armsAnimator.SetLayerWeight(1, currentLayer1Weight);
        bodyAnimator.SetLayerWeight(2, currentLayer2Weight);
        armsAnimator.SetLayerWeight(2, currentLayer2Weight);
        bodyAnimator.SetLayerWeight(4, currentLayer4Weight);
        armsAnimator.SetLayerWeight(4, currentLayer4Weight);
    }

    public void EnableRightArmMask()
    {
        Debug.Log("Enable Right Arm Mask");
        targetLayer1Weight = 1f;
        targetLayer2Weight = 0f;
    }
    
    public void DisableRightArmMask()
    {
        targetLayer1Weight = 0f;
    }

    public void EnableHoldObjectTwoArmsMask()
    {
        Debug.Log("Disable Right Arm Mask");

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

    /// <summary>
    /// Sets an animator bool directly on the local animators without ownership checks or RPC.
    /// Use this when the calling coroutine already runs on every client (e.g. stamp sequence).
    /// </summary>
    public void SetAnimBoolLocal(string animString, bool value)
    {
        bodyAnimator.SetBool(animString, value);
        armsAnimator.SetBool(animString, value);
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

    public void TurnRightArmRigOnAndOff(float smoothOnDuration, float onDuration)
    {
        if (rightRigOnOffCoroutine != null)
        {
            StopCoroutine(rightRigOnOffCoroutine);
        }
        
        rightRigOnOffCoroutine = StartCoroutine(TurnRightArmRigOnAndOffCR(smoothOnDuration, onDuration));
    }

    IEnumerator TurnRightArmRigOnAndOffCR(float smoothOnDuration, float onDuration)
    {
        float elapsed = 0;
        // Phase 1: Lerp Up to 1
        while (elapsed < smoothOnDuration)
        {
            elapsed += Time.deltaTime;
            RightArmRig.weight = Mathf.Lerp(0, 1, elapsed / smoothOnDuration);
            CamRightArmRig.weight = Mathf.Lerp(0, 1, elapsed / smoothOnDuration);
            yield return null;
        }

        RightArmRig.weight = 1;
        yield return new WaitForSeconds(onDuration);

        // Phase 2: Lerp Down to 0 (Faster)
        elapsed = 0f;
        while (elapsed < smoothOnDuration)
        {
            elapsed += Time.deltaTime;
            RightArmRig.weight = Mathf.Lerp(1, 0, elapsed / smoothOnDuration);
            CamRightArmRig.weight = Mathf.Lerp(1, 0, elapsed / smoothOnDuration);

            yield return null;
        }
        
        RightArmRig.weight = 0;
    }

    public void TurnLeftRigOnAndOff(float smoothOnDuration, float onDuration)
    {
        if (leftRigOnOffCoroutine != null)
        {
            StopCoroutine(leftRigOnOffCoroutine);
        }
        
        leftRigOnOffCoroutine = StartCoroutine(TurnLeftArmRigOnAndOffCR(smoothOnDuration, onDuration));
    }
    
    IEnumerator TurnLeftArmRigOnAndOffCR(float smoothOnDuration, float onDuration)
    {
        float elapsed = 0;
        // Phase 1: Lerp Up to 1
        while (elapsed < smoothOnDuration)
        {
            elapsed += Time.deltaTime;
            LeftArmRig.weight = Mathf.Lerp(0, 1, elapsed / smoothOnDuration);
            CamLeftArmRig.weight = Mathf.Lerp(0, 1, elapsed / smoothOnDuration);
            yield return null;
        }

        LeftArmRig.weight = 1;
        yield return new WaitForSeconds(onDuration);

        // Phase 2: Lerp Down to 0 (Faster)
        elapsed = 0f;
        while (elapsed < smoothOnDuration)
        {
            elapsed += Time.deltaTime;
            LeftArmRig.weight = Mathf.Lerp(1, 0, elapsed / smoothOnDuration);
            CamLeftArmRig.weight = Mathf.Lerp(1, 0, elapsed / smoothOnDuration);

            yield return null;
        }
        
        LeftArmRig.weight = 0;
    }

    public void EnableLeftArmMask()
    {
        targetLayer4Weight = 1;
    }

    public void DisableLeftArmMask()
    {
        targetLayer4Weight = 0;
    }
}