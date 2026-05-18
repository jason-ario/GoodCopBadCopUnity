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
    private PlayerPickupController _playerPickupController;
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

    /// <summary>
    /// Fixed Transform in the player hierarchy that the body right-arm rig constraint targets.
    /// Its world position is driven each frame by <see cref="RightArmIKTarget"/> when that is non-null.
    /// Never reassigned at runtime — use <see cref="RightArmIKTarget"/> to point the arm at something,
    /// or DOTween this directly when you want full manual control (e.g. stamp sequence).
    /// </summary>
    [SerializeField, FormerlySerializedAs("rightArmIKTarget")]
    private Transform rightArmRigIKTarget;

    /// <summary>
    /// Fixed Transform in the player hierarchy that the body left-arm rig constraint targets.
    /// Driven each frame by <see cref="LeftArmIKTarget"/> when that is non-null.
    /// </summary>
    [SerializeField, FormerlySerializedAs("leftArmIKTarget")]
    private Transform leftArmRigIKTarget;

    [Header("Camera Arm Rigs")]
    [SerializeField] private Rig camRightArmRig;
    [SerializeField] private Rig camLeftArmRig;
    [SerializeField] private Transform camRightArmIKTarget;
    [SerializeField] private Transform camLeftArmIKTarget;

    public Rig RightArmRig => rightArmRig;
    public Rig CamRightArmRig => camRightArmRig;

    public Rig LeftArmRig => leftArmRig;
    public Rig CamLeftArmRig => camLeftArmRig;

    /// <summary>Read-only accessor for the fixed rig constraint target. DOTween this directly for full manual control.</summary>
    public Transform RightArmRigIKTarget => rightArmRigIKTarget;

    /// <summary>Read-only accessor for the fixed left-arm rig constraint target.</summary>
    public Transform LeftArmRigIKTarget => leftArmRigIKTarget;

    /// <summary>
    /// External IK target for the body right arm. Set to any scene Transform to make the arm reach toward it.
    /// LateUpdate copies its world position/rotation to <see cref="RightArmRigIKTarget"/> each frame.
    /// Set to null to stop the passthrough and drive <see cref="RightArmRigIKTarget"/> directly.
    /// </summary>
    public Transform RightArmIKTarget { get; set; }

    /// <summary>
    /// When true, <see cref="ApplyLocalBodyLean"/> is suppressed so scripted sequences
    /// (e.g. the stamp DOTween) can lock the spine/shoulder without fighting the lean system.
    /// </summary>
    public bool SuppressLocalBodyLean { get; set; }

    /// <summary>
    /// When true, <see cref="rightArmRigIKTarget"/> is being driven directly by a local sequence
    /// (e.g. the stamp DOTween) and the network-position proxy passthrough must not overwrite it.
    /// Set to true before DOTweening <see cref="RightArmRigIKTarget"/> directly, and back to false
    /// when the sequence finishes and <see cref="RightArmIKTarget"/> is restored.
    /// </summary>
    public bool DriveRightArmRigTargetDirectly { get; set; }

    /// <summary>
    /// External IK target for the body left arm. Same passthrough pattern as <see cref="RightArmIKTarget"/>.
    /// </summary>
    public Transform LeftArmIKTarget { get; set; }

    public Transform CamRightArmIKTarget
    {
        get => camRightArmIKTarget;
        set => camRightArmIKTarget = value;
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

    /// <summary>
    /// Synced flag: true while the body right-arm IK rig is active (weight > 0).
    /// Owner writes when enabling/disabling the rig; proxy clients read to suppress
    /// the arm-pitch bone override that would otherwise fight the IK result.
    /// </summary>
    private NetworkVariable<bool> netRightArmRigActive =
        new NetworkVariable<bool>(false, writePerm: NetworkVariableWritePermission.Owner);

    /// <summary>
    /// Synced flag: true while the body left-arm IK rig is active (weight > 0).
    /// </summary>
    private NetworkVariable<bool> netLeftArmRigActive =
        new NetworkVariable<bool>(false, writePerm: NetworkVariableWritePermission.Owner);

    /// <summary>
    /// Synced world-space position of the right-arm IK target.
    /// Owner writes each frame when <see cref="RightArmIKTarget"/> is assigned;
    /// proxy clients read it to keep <see cref="rightArmRigIKTarget"/> up to date
    /// without needing a direct Transform reference.
    /// </summary>
    private NetworkVariable<Vector3> netRightArmIKPos =
        new NetworkVariable<Vector3>(writePerm: NetworkVariableWritePermission.Owner);

    /// <summary>
    /// Synced world-space rotation of the right-arm IK target.
    /// </summary>
    private NetworkVariable<Quaternion> netRightArmIKRot =
        new NetworkVariable<Quaternion>(Quaternion.identity, writePerm: NetworkVariableWritePermission.Owner);

    [Header("Vertical Look Bone Rotation")]
    [SerializeField] [Range(0f, 1f)] private float headPitchWeight  = 0.40f;
    [SerializeField] [Range(0f, 1f)] private float neckPitchWeight  = 0.30f;
    [SerializeField] [Range(0f, 1f)] private float spinePitchWeight = 0.30f;
    [Tooltip("How much the upper arm rotates toward the look direction when holding an object. Only applied on proxy clients.")]
    [SerializeField] [Range(0f, 1f)] private float armHoldPitchWeight = 0.50f;
    [Tooltip("Maximum downward arm pitch (positive degrees) applied when holding an object. Clamps the arm from rotating too far down.")]
    [SerializeField] private float armPitchClampDown = 20f;
    [Tooltip("Maximum upward arm pitch (positive degrees) applied when holding an object. Stored as a positive value and applied as a negative clamp.")]
    [SerializeField] private float armPitchClampUp   = 30f;
    [Tooltip("Maximum downward pitch (positive degrees) that body bones will follow. Clamps the body from bending too far down.")]
    [SerializeField] private float pitchClampDown = 40f;
    [Tooltip("Maximum upward pitch (positive degrees) that body bones will follow. Stored as a positive value and applied as a negative clamp.")]
    [SerializeField] private float pitchClampUp   = 30f;

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

    [Header("IK Reach Lean (Proxy)")]
    [Tooltip("Degrees of forward spine tilt applied per unit the IK target exceeds arm reach.")]
    [SerializeField] private float ikLeanResponseScale = 40f;
    [Tooltip("Maximum forward lean angle (degrees) the IK reach system can apply.")]
    [SerializeField] [Range(0f, 45f)] private float ikLeanMaxAngle = 20f;
    [Tooltip("How quickly the IK reach lean smooths in and out.")]
    [SerializeField] private float ikLeanSpeed = 8f;

    // Cached arm reach (upper arm + forearm lengths) measured once after spawn.
    private float _rightArmReach;
    // Smoothed spine tilt angle driven by the IK reach lean system.
    private float _currentIKLeanAngle;

    // Cached bone transforms resolved once after spawn.
    private Transform _headBone;
    private Transform _neckBone;
    private Transform _spineBone;
    private Transform _rightUpperArmBone;
    private Transform _leftUpperArmBone;
    private Transform _rightForeArmBone;
    private Transform _rightHandBone;

    private Coroutine rightRigOnOffCoroutine;
    private Coroutine leftRigOnOffCoroutine;

    private void Awake()
    {
        _playerMovementController = GetComponent<PlayerMovementController>();
        _playerPickupController   = GetComponent<PlayerPickupController>();
    }
    

    /// <summary>
    /// Feeds all IK rig targets their current-frame world position and rotation.
    /// Must run in Update so Animation Rigging — which evaluates between Update and LateUpdate —
    /// solves the constraint against up-to-date data rather than the previous frame's values.
    /// </summary>
    private void Update()
    {
        if (RightArmIKTarget != null)
        {
            rightArmRigIKTarget.position = RightArmIKTarget.position;
            rightArmRigIKTarget.rotation = RightArmIKTarget.rotation;

            // Publish the world-space target so proxy clients can mirror it without needing
            // a direct Transform reference to the button's ikTarget.
            if (IsOwner)
            {
                netRightArmIKPos.Value = RightArmIKTarget.position;
                netRightArmIKRot.Value = RightArmIKTarget.rotation;
            }
        }
        else if (!IsOwner && netRightArmRigActive.Value && !DriveRightArmRigTargetDirectly)
        {
            // Proxy: no local target set, but the rig is active — drive the rig target from
            // the synced world position so the arm reaches toward the same point as the owner.
            // Suppressed when a local sequence (e.g. stamp DOTween) is driving the target directly.
            rightArmRigIKTarget.position = netRightArmIKPos.Value;
            rightArmRigIKTarget.rotation = netRightArmIKRot.Value;
        }

        if (LeftArmIKTarget != null)
        {
            leftArmRigIKTarget.position = LeftArmIKTarget.position;
            leftArmRigIKTarget.rotation = LeftArmIKTarget.rotation;
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
    }

    private void LateUpdate()
    {
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

        // Proxy clients mirror the owner's rig weight by reacting to the netRightArmRigActive flag.
        // The owner drives its own weight directly inside the coroutine/DOTween, so it is excluded.
        if (!IsOwner)
            netRightArmRigActive.OnValueChanged += OnProxyRightArmRigActiveChanged;

        // Cache bones used for procedural vertical-look rotation on both owner and proxies.
        _headBone          = bodyAnimator.GetBoneTransform(HumanBodyBones.Head);
        _neckBone          = bodyAnimator.GetBoneTransform(HumanBodyBones.Neck);
        _spineBone         = bodyAnimator.GetBoneTransform(HumanBodyBones.Spine);
        _rightUpperArmBone = bodyAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        _leftUpperArmBone  = bodyAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        _rightForeArmBone  = bodyAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        _rightHandBone     = bodyAnimator.GetBoneTransform(HumanBodyBones.RightHand);

        // Measure the arm reach after one frame so the Animator has evaluated
        // and bone positions are valid in world space.
        StartCoroutine(MeasureArmReachCR());

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

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (!IsOwner)
            netRightArmRigActive.OnValueChanged -= OnProxyRightArmRigActiveChanged;
    }

    /// <summary>
    /// Called on proxy clients when the owner toggles the right-arm IK rig.
    /// Smoothly fades the local <see cref="rightArmRig"/> weight in or out so the
    /// IK constraint activates without needing a coroutine to run on every client.
    /// </summary>
    private void OnProxyRightArmRigActiveChanged(bool previous, bool current)
    {
        const float ProxyRigSmoothTime = 0.2f;
        SetRightArmRigWeightSmooth(current ? 1f : 0f, ProxyRigSmoothTime);
    }

    /// <summary>
    /// Waits one frame so the Animator has evaluated and bone world positions are valid,
    /// then measures the right arm reach (upper arm + forearm lengths) used by the IK lean system.
    /// </summary>
    private IEnumerator MeasureArmReachCR()
    {
        yield return null;

        if (_rightUpperArmBone != null && _rightForeArmBone != null && _rightHandBone != null)
        {
            float upperLen = Vector3.Distance(_rightUpperArmBone.position, _rightForeArmBone.position);
            float foreLen  = Vector3.Distance(_rightForeArmBone.position, _rightHandBone.position);
            // Use 90% of total reach so the lean starts slightly before full extension.
            _rightArmReach = (upperLen + foreLen) * 0.90f;
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
        float pitch = Mathf.Clamp(netPitch.Value, -pitchClampUp, pitchClampDown);

        // --- IK Reach Lean ---
        // When the right arm IK rig is active and the target is beyond comfortable arm reach,
        // lean the spine toward the IK target to sell the stretch. Runs before the arm snapshot
        // below so the body lean contributes naturally to the arm's world position.
        if (_rightArmReach > 0f && _rightUpperArmBone != null && _spineBone != null && rightArmRigIKTarget != null)
        {
            float targetIKLeanAngle = 0f;

            if (rightArmRig.weight > 0.01f)
            {
                float dist    = Vector3.Distance(_rightUpperArmBone.position, rightArmRigIKTarget.position);
                float deficit = Mathf.Max(0f, dist - _rightArmReach);
                targetIKLeanAngle = Mathf.Clamp(deficit * ikLeanResponseScale, 0f, ikLeanMaxAngle);
            }

            _currentIKLeanAngle = Mathf.Lerp(_currentIKLeanAngle, targetIKLeanAngle, ikLeanSpeed * Time.deltaTime);

            if (_currentIKLeanAngle > 0.01f)
            {
                Vector3 toTarget = (rightArmRigIKTarget.position - _spineBone.position).normalized;
                Vector3 leanAxis = Vector3.Cross(Vector3.up, toTarget).normalized;
                if (leanAxis.sqrMagnitude > 0.001f)
                    _spineBone.rotation = Quaternion.AngleAxis(_currentIKLeanAngle, leanAxis) * _spineBone.rotation;
            }
        }

        // --- Arm world-transform snapshot ---
        // Spine pitch and body lean run after Animation Rigging has already solved the arm chain.
        // Rotating the spine drags the shoulder with it and breaks the IK result in world space.
        // Solution: snapshot the upper arm world position+rotation after the reach lean (which is
        // intentional), apply all pitch/lean to the spine normally, then restore the arm so it
        // stays exactly where the IK solver placed it. Head and neck can follow freely.
        bool rightIKActive = netRightArmRigActive.Value || rightArmRig.weight > 0.01f;
        bool leftIKActive  = netLeftArmRigActive.Value  || leftArmRig.weight  > 0.01f;

        Vector3    savedRightArmPos = Vector3.zero;
        Quaternion savedRightArmRot = Quaternion.identity;
        Vector3    savedLeftArmPos  = Vector3.zero;
        Quaternion savedLeftArmRot  = Quaternion.identity;

        if (rightIKActive && _rightUpperArmBone != null)
        {
            savedRightArmPos = _rightUpperArmBone.position;
            savedRightArmRot = _rightUpperArmBone.rotation;
        }

        if (leftIKActive && _leftUpperArmBone != null)
        {
            savedLeftArmPos = _leftUpperArmBone.position;
            savedLeftArmRot = _leftUpperArmBone.rotation;
        }

        // --- Pitch and lean ---

        if (_headBone != null)
            _headBone.localRotation *= Quaternion.Euler(pitch * headPitchWeight, 0f, 0f);

        if (_neckBone != null)
            _neckBone.localRotation *= Quaternion.Euler(pitch * neckPitchWeight, 0f, 0f);

        if (_spineBone != null)
            _spineBone.localRotation *= Quaternion.Euler(pitch * spinePitchWeight, 0f, 0f);

        float leanFactor = Mathf.Lerp(_currentLeanFactor, netLeanFactor.Value, leanLerpSpeed * Time.deltaTime);
        _currentLeanFactor = leanFactor;

        if (leanFactor > 0.001f)
        {
            float dir = netLeanDirection.Value;

            if (_spineBone != null)
                _spineBone.localRotation *= Quaternion.Euler(dir * leanFactor * leanSpineMax, 0f, 0f);

            if (_neckBone != null)
                _neckBone.localRotation *= Quaternion.Euler(dir * leanFactor * leanNeckMax, 0f, 0f);
        }

        // --- Restore arm world transforms ---
        // Forearm and hand are children of the upper arm, so they follow automatically.
        if (rightIKActive && _rightUpperArmBone != null)
            _rightUpperArmBone.SetPositionAndRotation(savedRightArmPos, savedRightArmRot);

        if (leftIKActive && _leftUpperArmBone != null)
            _leftUpperArmBone.SetPositionAndRotation(savedLeftArmPos, savedLeftArmRot);

        // Swing the upper arm bones up/down when the owner's camera arm has something equipped.
        // Skip while IK is active — the world-transform restore above already locked the arm.
        bool rightArmHolding = netLayer1Weight.Value > 0.01f || netLayer2Weight.Value > 0.01f;
        bool leftArmHolding  = netLayer4Weight.Value > 0.01f || netLayer2Weight.Value > 0.01f;

        float armPitch = Mathf.Clamp(netPitch.Value, -armPitchClampUp, armPitchClampDown);

        if (rightArmHolding && _rightUpperArmBone != null && !netRightArmRigActive.Value && rightArmRig.weight < 0.01f)
        {
            _rightUpperArmBone.rotation =
                Quaternion.AngleAxis(armPitch * armHoldPitchWeight, transform.right)
                * _rightUpperArmBone.rotation;
        }

        if (leftArmHolding && _leftUpperArmBone != null && !netLeftArmRigActive.Value && leftArmRig.weight < 0.01f)
        {
            _leftUpperArmBone.rotation =
                Quaternion.AngleAxis(armPitch * armHoldPitchWeight, transform.right)
                * _leftUpperArmBone.rotation;
        }

        // Snap the held world object to the body arm target now that all bone manipulation
        // is complete. This is the authoritative sync — LateUpdate in PlayerPickupController
        // also calls this as a fallback, but execution order means it may run before bones
        // are pitched. Calling it here guarantees the correct final position.
        _playerPickupController?.SyncWorldObjectToBody();
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
        if (SuppressLocalBodyLean) return;

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
        targetLayer2Weight = 0f;
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

        // Apply locally and immediately so the owner sees no round-trip delay.
        bodyAnimator.SetBool(animString, value);
        armsAnimator.SetBool(animString, value);

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
        DOTween.Kill(rightArmRig);
        DOTween.To(() => rightArmRig.weight, x => rightArmRig.weight = x, smoothWeight, smoothTime).SetTarget(rightArmRig);

        if (IsOwner)
        {
            netRightArmRigActive.Value = smoothWeight > 0f;

            // Camera arm rig is only relevant for the local/owner player — skip it on proxy clients
            // so its evaluation does not override the body arm rig's bone results on observers.
            if (camRightArmRig != null)
            {
                DOTween.Kill(camRightArmRig);
                DOTween.To(() => camRightArmRig.weight, x => camRightArmRig.weight = x, smoothWeight, smoothTime).SetTarget(camRightArmRig);
            }
        }
    }
    
    public void SetLeftArmRigWeightSmooth(float smoothWeight, float smoothTime)
    {
        DOTween.Kill(leftArmRig);
        DOTween.To(() => leftArmRig.weight, x => leftArmRig.weight = x, smoothWeight, smoothTime).SetTarget(leftArmRig);

        if (IsOwner)
        {
            netLeftArmRigActive.Value = smoothWeight > 0f;

            // Camera arm rig is only relevant for the local/owner player — skip it on proxy clients.
            if (camLeftArmRig != null)
            {
                DOTween.Kill(camLeftArmRig);
                DOTween.To(() => camLeftArmRig.weight, x => camLeftArmRig.weight = x, smoothWeight, smoothTime).SetTarget(camLeftArmRig);
            }
        }
    }

    public void SetAnimFloat(string animString, float value)
    {
        bodyAnimator.SetFloat(animString, value);
        armsAnimator.SetFloat(animString, value);
    }

    public void SetAimRigWeightSmooth(float smoothWeight, float smoothTime)
    {
        DOTween.Kill(shoulderRig);
        DOTween.To(() => shoulderRig.weight, x => shoulderRig.weight = x, smoothWeight, smoothTime).SetTarget(shoulderRig);
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
        // Kill any in-flight DOTween targeting rightArmRig (e.g. from SetRightArmRigWeightSmooth)
        // so it cannot fight the coroutine's manual lerp below.
        DOTween.Kill(rightArmRig);

        if (IsOwner) netRightArmRigActive.Value = true;

        float elapsed = 0;
        // Phase 1: Lerp Up to 1
        while (elapsed < smoothOnDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / smoothOnDuration;
            RightArmRig.weight = Mathf.Lerp(0, 1, t);
            // Camera arm rig is only meaningful on the local owner — proxy clients must not
            // evaluate it during the stamp so it doesn't override the body arm rig's bones.
            if (IsOwner) CamRightArmRig.weight = Mathf.Lerp(0, 1, t);
            yield return null;
        }

        RightArmRig.weight = 1;
        yield return new WaitForSeconds(onDuration);

        // Phase 2: Lerp Down to 0 (Faster)
        elapsed = 0f;
        while (elapsed < smoothOnDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / smoothOnDuration;
            RightArmRig.weight = Mathf.Lerp(1, 0, t);
            if (IsOwner) CamRightArmRig.weight = Mathf.Lerp(1, 0, t);
            yield return null;
        }
        
        RightArmRig.weight = 0;
        if (IsOwner) netRightArmRigActive.Value = false;
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
        // Kill any in-flight DOTween targeting leftArmRig so it cannot fight the coroutine.
        DOTween.Kill(leftArmRig);

        if (IsOwner) netLeftArmRigActive.Value = true;

        float elapsed = 0;
        // Phase 1: Lerp Up to 1
        while (elapsed < smoothOnDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / smoothOnDuration;
            LeftArmRig.weight = Mathf.Lerp(0, 1, t);
            // Camera arm rig is only meaningful on the local owner — proxy clients must not
            // evaluate it during interactions so it doesn't override the body arm rig's bones.
            if (IsOwner) CamLeftArmRig.weight = Mathf.Lerp(0, 1, t);
            yield return null;
        }

        LeftArmRig.weight = 1;
        yield return new WaitForSeconds(onDuration);

        // Phase 2: Lerp Down to 0 (Faster)
        elapsed = 0f;
        while (elapsed < smoothOnDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / smoothOnDuration;
            LeftArmRig.weight = Mathf.Lerp(1, 0, t);
            if (IsOwner) CamLeftArmRig.weight = Mathf.Lerp(1, 0, t);
            yield return null;
        }
        
        LeftArmRig.weight = 0;
        if (IsOwner) netLeftArmRigActive.Value = false;
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