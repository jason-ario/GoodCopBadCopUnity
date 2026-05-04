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

    /// <summary>
    /// Synced vertical look pitch in degrees. Owner writes local camera pitch;
    /// proxy clients read it to drive procedural head/neck/spine bone rotation.
    /// </summary>
    private NetworkVariable<float> netPitch =
        new NetworkVariable<float>(writePerm: NetworkVariableWritePermission.Owner);

    /// <summary>
    /// Synced body lean factor [0, 1]. Owner writes whenever a scripted interaction
    /// drives a lean; proxy clients read it to replicate the bone bend.
    /// </summary>
    private NetworkVariable<float> netLeanFactor =
        new NetworkVariable<float>(writePerm: NetworkVariableWritePermission.Owner);

    /// <summary>
    /// Synced lean direction: +1 = forward, -1 = backward.
    /// </summary>
    private NetworkVariable<float> netLeanDirection =
        new NetworkVariable<float>(1f, writePerm: NetworkVariableWritePermission.Owner);

    [Header("Vertical Look Bone Rotation")]
    [SerializeField] [Range(0f, 1f)] private float headPitchWeight  = 0.40f;
    [SerializeField] [Range(0f, 1f)] private float neckPitchWeight  = 0.30f;
    [SerializeField] [Range(0f, 1f)] private float spinePitchWeight = 0.30f;
    [Tooltip("How much the upper arm rotates toward the look direction when holding an object. Only applied on proxy clients.")]
    [SerializeField] [Range(0f, 1f)] private float armHoldPitchWeight = 0.50f;

    [Header("Local Body Lean (Camera Offset)")]
    [Tooltip("Maximum pitch (degrees) applied to the spine bone at full lean.")]
    [SerializeField] private float leanSpineMax  = 12f;
    [Tooltip("Maximum pitch (degrees) applied to the neck bone at full lean.")]
    [SerializeField] private float leanNeckMax   = 6f;
    [Tooltip("How quickly the lean smooths in and out.")]
    [SerializeField] private float leanLerpSpeed = 6f;

    // Current smoothed lean factor [0, 1] set by PlayerMovementController each frame.
    private float _targetLeanFactor;
    private float _currentLeanFactor;
    // +1 = lean forward/down, -1 = lean back/up. Derived from camera pitch by default.
    private float _leanDirection = 1f;

    // Cached bone transforms resolved once after spawn.
    private Transform _headBone;
    private Transform _neckBone;
    private Transform _spineBone;
    private Transform _rightUpperArmBone;
    private Transform _leftUpperArmBone;

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
            // Proxy: drive animations from NetworkVariables, then apply bone-level pitch.
            UpdateAnimations();
            ApplyProxyPitchBones();
            return;
        }

        UpdateAnimations();
        
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            ShrugEmote();
        }

        ApplyLocalBodyLean();
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

        // Cache bones used for procedural vertical-look rotation on both owner and proxies.
        _headBone          = bodyAnimator.GetBoneTransform(HumanBodyBones.Head);
        _neckBone          = bodyAnimator.GetBoneTransform(HumanBodyBones.Neck);
        _spineBone         = bodyAnimator.GetBoneTransform(HumanBodyBones.Spine);
        _rightUpperArmBone = bodyAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        _leftUpperArmBone  = bodyAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);

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
            if (_headBone != null)
            {
                _headBone.localScale = Vector3.zero;
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

        // Sync the local camera pitch so proxy clients can drive bone rotation.
        netPitch.Value = _playerMovementController.CameraPitch;

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

    /// <summary>
    /// Applies a procedural pitch rotation to head, neck, and spine bones on proxy clients,
    /// and — when an arm is actively holding something — swings that upper arm bone up/down
    /// to match the observed player's vertical look direction.
    /// Must be called in LateUpdate so it runs after the Animator and Animation Rigging have evaluated.
    /// </summary>
    private void ApplyProxyPitchBones()
    {
        float pitch = netPitch.Value;

        if (_headBone != null)
        {
            _headBone.localRotation *= Quaternion.Euler(pitch * headPitchWeight, 0f, 0f);
        }

        if (_neckBone != null)
        {
            _neckBone.localRotation *= Quaternion.Euler(pitch * neckPitchWeight, 0f, 0f);
        }

        if (_spineBone != null)
        {
            _spineBone.localRotation *= Quaternion.Euler(pitch * spinePitchWeight, 0f, 0f);
        }

        // Apply synced body lean on top of the pitch rotation.
        float leanFactor = Mathf.Lerp(
            _currentLeanFactor,
            netLeanFactor.Value,
            leanLerpSpeed * Time.deltaTime
        );
        _currentLeanFactor = leanFactor;

        if (leanFactor > 0.001f)
        {
            float dir = netLeanDirection.Value;

            if (_spineBone != null)
            {
                _spineBone.localRotation *= Quaternion.Euler(dir * leanFactor * leanSpineMax, 0f, 0f);
            }

            if (_neckBone != null)
            {
                _neckBone.localRotation *= Quaternion.Euler(dir * leanFactor * leanNeckMax, 0f, 0f);
            }
        }

        // Swing the upper arm bones up/down when the owner's camera arm has something equipped.
        // netLayer1Weight (single right-hand hold) and netLayer2Weight (two-hand hold) are synced
        // from the owner and are non-zero whenever the camera arm is holding something — the
        // local currentLayer* fields are only updated on the owner and are always 0 on proxies.
        // Rotation axis is transform.right: the held arm sits in front of the character, so
        // rotating around the lateral axis pitches it up/down in the same plane as the camera.
        bool rightArmHolding = netLayer1Weight.Value > 0.01f || netLayer2Weight.Value > 0.01f;
        bool leftArmHolding  = netLayer4Weight.Value > 0.01f || netLayer2Weight.Value > 0.01f;

        if (rightArmHolding && _rightUpperArmBone != null)
        {
            _rightUpperArmBone.rotation =
                Quaternion.AngleAxis(pitch * armHoldPitchWeight, transform.right)
                * _rightUpperArmBone.rotation;
        }

        if (leftArmHolding && _leftUpperArmBone != null)
        {
            _leftUpperArmBone.rotation =
                Quaternion.AngleAxis(pitch * armHoldPitchWeight, transform.right)
                * _leftUpperArmBone.rotation;
        }
    }

    /// <summary>
    /// Called by PlayerMovementController each frame with a normalized [0, 1] value
    /// representing how far the camera has drifted from its base position.
    /// The lean is applied to spine and neck bones in LateUpdate on the local player only.
    /// </summary>
    public void SetLocalBodyLeanFactor(float factor)
    {
        _targetLeanFactor = Mathf.Clamp01(factor);
        // Direction is derived live from camera pitch in the camera-offset driven path.
        _leanDirection = _playerMovementController.CameraPitch >= 0f ? 1f : -1f;

        if (IsOwner)
        {
            netLeanFactor.Value    = _targetLeanFactor;
            netLeanDirection.Value = _leanDirection;
        }
    }

    /// <summary>
    /// Directly sets the body lean factor to a target value, bypassing the camera-offset
    /// calculation. Use this when the camera is moved externally (e.g. scripted interactions)
    /// and you want to drive the lean manually.
    /// Pass direction as +1 (lean forward) or -1 (lean back).
    /// </summary>
    public void SetBodyLeanDirect(float factor, float direction = 1f)
    {
        _targetLeanFactor = Mathf.Clamp01(factor);
        _leanDirection    = Mathf.Sign(direction != 0f ? direction : 1f);

        if (IsOwner)
        {
            netLeanFactor.Value    = _targetLeanFactor;
            netLeanDirection.Value = _leanDirection;
        }
    }

    /// <summary>
    /// Procedurally tilts the spine and neck bones toward the camera offset direction
    /// on the local player so the character appears to lean when looking far away.
    /// Must run in LateUpdate after the Animator has evaluated.
    /// </summary>
    private void ApplyLocalBodyLean()
    {
        _currentLeanFactor = Mathf.Lerp(_currentLeanFactor, _targetLeanFactor, leanLerpSpeed * Time.deltaTime);

        if (_currentLeanFactor < 0.001f) return;

        if (_spineBone != null)
        {
            _spineBone.localRotation *= Quaternion.Euler(_leanDirection * _currentLeanFactor * leanSpineMax, 0f, 0f);
        }

        if (_neckBone != null)
        {
            _neckBone.localRotation *= Quaternion.Euler(_leanDirection * _currentLeanFactor * leanNeckMax, 0f, 0f);
        }
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