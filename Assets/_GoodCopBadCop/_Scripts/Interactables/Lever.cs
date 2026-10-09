using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Shutter lever. Pressing Interact opens its diegetic view (<see cref="LeverDiegeticController"/>),
/// where the player clicks and drags the lever up/down. Shutter open/close fires at the 90 % / 10 %
/// drag thresholds; releasing commits the lever to the nearest end and syncs it over the network.
/// </summary>
public class Lever : Interactable
{
    [SerializeField] private AudioSource leverAudio;
    [SerializeField] private AudioClip leverOnSound;
    [SerializeField] private AudioClip leverOffSound;
    [SerializeField] private ShutterController shutter;

    [Header("Diegetic View")]
    [Tooltip("The close-up view opened when the player presses Interact on the lever.")]
    [SerializeField] private LeverDiegeticController _diegeticView;

    [Tooltip("Optional. Prevents a second player entering the lever view while someone is using it.")]
    [SerializeField] private DiegeticOccupancy _occupancy;

    [Header("Hand IK")]
    [Tooltip("Child Transform of the lever arm the right hand grips while dragging.")]
    [SerializeField] private Transform _rightIkTarget;

    [Tooltip("Child Transform of the lever arm the left hand grips while dragging (used when the right hand holds an item).")]
    [SerializeField] private Transform _leftIkTarget;

    [Tooltip("World Transform the player's head looks at while gripping. Falls back to the lever itself.")]
    [SerializeField] private Transform _lookTarget;

    [Tooltip("Seconds the arm IK takes to blend onto / off the lever.")]
    [SerializeField] private float _handReachDuration = 0.2f;

    private const string RightGripBool = "RightGrip";
    private const string LeftGripBool  = "LeftGrip";

    [Header("Lever Arm")]
    [Tooltip("The child Transform that visually represents the lever arm.")]
    [SerializeField] private Transform _leverArm;

    [Tooltip("Local euler angles of the lever arm when fully UP (shutter open).")]
    [SerializeField] private Vector3 _topRot = new Vector3(60f, -180f, 0f);

    [Tooltip("Local euler angles of the lever arm when fully DOWN (shutter closed).")]
    [SerializeField] private Vector3 _bottomRot = new Vector3(-60f, -180f, 0f);

    [Tooltip("Multiplier applied to raw Mouse Y axis input. Higher = faster lever travel per mouse movement.")]
    [SerializeField] private float _dragSensitivity = 0.05f;

    [Tooltip("Units per second the lever travels at full right-stick deflection (controller only).")]
    [SerializeField] private float _controllerDragSpeed = 1.5f;

    [Tooltip("Duration of the snap tween when the lever commits to an end position on release.")]
    [SerializeField] private float _snapDuration = 0.1f;

    private NetworkVariable<bool> _isUp = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsUp => _isUp.Value;

    /// <summary>Whether the lever currently accepts player input (see <see cref="SetInteractable"/>).</summary>
    public bool IsUsable => _isInteractable;

    /// <summary>Normalised lever position: 0 = bottom/down, 1 = top/up.</summary>
    private float _dragT = 0f;

    /// <summary>
    /// Tracks whether the local client has already triggered an open or close during the
    /// current drag. Seeded from <see cref="_isUp"/> when a drag begins.
    /// </summary>
    private bool _localShutterOpen;

    private bool _isDragging;
    private bool _isInteractable = true;

    protected override string DefaultInteractVerb => "Pull";

    public override void OnNetworkSpawn()
    {
        _isUp.OnValueChanged += OnLeverStateChanged;
        SnapLeverArmToState(_isUp.Value);

        if (_isUp.Value)
            shutter.OpenShutter();
        else
            shutter.CloseShutter();
    }

    public override void OnNetworkDespawn()
    {
        _isUp.OnValueChanged -= OnLeverStateChanged;
    }

    protected override void Awake()
    {
        base.Awake();

        // Rotation is driven manually — disable the Animator so it does not fight the script.
        Animator animator = GetComponent<Animator>();
        if (animator != null)
            animator.enabled = false;
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        if (!_isInteractable) return;
        if (_diegeticView == null)
        {
            Debug.LogWarning($"[Lever] '{name}' has no LeverDiegeticController assigned — Interact does nothing.", this);
            return;
        }
        if (DiegeticViewController.IsAnyViewActive) return;
        if (_occupancy != null && !_occupancy.TryClaim(player)) return;

        _diegeticView.Open(player);
    }

    // ── Drag API (driven by LeverDiegeticController) ─────────────────────────

    /// <summary>Starts a drag from the lever's current committed state.</summary>
    public void BeginDrag()
    {
        if (_isDragging) return;
        _isDragging = true;

        _leverArm?.DOKill();
        _dragT = _isUp.Value ? 1f : 0f;
        _localShutterOpen = _isUp.Value;
    }

    /// <summary>Moves the lever by a raw per-frame Mouse Y delta (positive = up).</summary>
    public void DragByMouse(float mouseYDelta) => DragBy(mouseYDelta * _dragSensitivity);

    /// <summary>Moves the lever by a continuous stick value in [-1, 1] (positive = up), scaled by deltaTime.</summary>
    public void DragByStick(float stickY) => DragBy(stickY * _controllerDragSpeed * Time.deltaTime);

    /// <summary>Ends the drag and commits the lever to the nearest end.</summary>
    public void EndDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        CommitLever();
    }

    // ── Hand grab (driven by LeverDiegeticController) ────────────────────────

    private PlayerAnimationController _grabAnim;
    private bool _grabRightArm;

    /// <summary>
    /// Reaches the player's free hand onto the lever arm (grip pose + arm IK). Uses the right
    /// arm unless it is busy (IK-active or holding an item), in which case the left arm is used.
    /// </summary>
    public void BeginHandGrab(PlayerInteractionController player)
    {
        if (_grabAnim != null || player == null) return;

        PlayerAnimationController anim   = player.playerAnimationController;
        PlayerPickupController    pickup = player.GetComponent<PlayerPickupController>();
        if (anim == null) return;

        bool rightArmBusy = anim.RightArmRig.weight > 0.5f || (pickup != null && pickup.HeldObject != null);
        bool leftArmBusy  = anim.LeftArmRig.weight  > 0.5f;
        if (rightArmBusy && leftArmBusy) return;

        _grabRightArm = !rightArmBusy;
        Transform ikTarget = _grabRightArm ? _rightIkTarget : _leftIkTarget;
        if (ikTarget == null) return;

        _grabAnim = anim;

        Transform lookPoint = _lookTarget != null ? _lookTarget : transform;
        anim.OverrideHeadLookAt(lookPoint.position);

        anim.SetAnimBool(_grabRightArm ? RightGripBool : LeftGripBool, true);

        if (_grabRightArm)
        {
            anim.RightArmIKTarget       = ikTarget;
            anim.CamRightArmRigIKTarget = ikTarget;
            anim.EnableRightArmMask();
            anim.SetRightArmRigWeightSmooth(1f, _handReachDuration);
        }
        else
        {
            anim.LeftArmIKTarget       = ikTarget;
            anim.CamLeftArmRigIKTarget = ikTarget;
            anim.EnableLeftArmMask();
            anim.SetLeftArmRigWeightSmooth(1f, _handReachDuration);
        }
    }

    /// <summary>Releases the hand grab started by <see cref="BeginHandGrab"/>.</summary>
    public void EndHandGrab()
    {
        PlayerAnimationController anim = _grabAnim;
        if (anim == null) return;
        _grabAnim = null;

        anim.SetAnimBool(_grabRightArm ? RightGripBool : LeftGripBool, false);

        if (_grabRightArm)
        {
            anim.RightArmIKTarget       = null;
            anim.CamRightArmRigIKTarget = null;
            anim.SetRightArmRigWeightSmooth(0f, _handReachDuration);
            anim.DisableRightArmMask();
        }
        else
        {
            anim.LeftArmIKTarget       = null;
            anim.CamLeftArmRigIKTarget = null;
            anim.SetLeftArmRigWeightSmooth(0f, _handReachDuration);
            anim.DisableLeftArmMask();
        }

        anim.OverrideHeadLookAt(null);
    }

    private void DragBy(float deltaT)
    {
        if (!_isDragging) return;
        _dragT = Mathf.Clamp01(_dragT + deltaT);
        ApplyDragRotation();
        CheckShutterThreshold();
    }

    // ── Lever state ───────────────────────────────────────────────────────────

    /// <summary>
    /// Snaps _dragT to the nearest end, tweens the arm there, and syncs the shutter
    /// and network state. Shutter is driven against _localShutterOpen so it never
    /// fires twice for a state already applied during the drag.
    /// </summary>
    private void CommitLever()
    {
        bool newIsUp = _dragT >= 0.5f;
        _dragT = newIsUp ? 1f : 0f;

        if (_leverArm != null)
        {
            _leverArm.DOLocalRotate(newIsUp ? _topRot : _bottomRot, _snapDuration)
                .SetEase(Ease.OutBack);
        }

        // If the committed state doesn't match what the local client already applied via
        // threshold (e.g. released at 0.6 — above 0.5 but never crossed 0.9), sync now.
        if (newIsUp != _localShutterOpen)
        {
            _localShutterOpen = newIsUp;
            if (newIsUp) shutter.OpenShutter(); else shutter.CloseShutter();
            leverAudio.PlayOneShot(newIsUp ? leverOnSound : leverOffSound);
        }

        // Only send RPC if the network variable needs to change.
        if (newIsUp != _isUp.Value)
            SetLeverServerRpc(newIsUp, NetworkManager.Singleton.LocalClientId);
    }

    /// <summary>
    /// Fires shutter open/close at the 90 % / 10 % drag thresholds during a live drag.
    /// Uses hysteresis so crossing back into the dead zone does not re-trigger.
    /// </summary>
    private void CheckShutterThreshold()
    {
        if (_dragT >= 0.9f && !_localShutterOpen)
        {
            _localShutterOpen = true;
            shutter.OpenShutter();
            leverAudio.PlayOneShot(leverOnSound);
        }
        else if (_dragT <= 0.1f && _localShutterOpen)
        {
            _localShutterOpen = false;
            shutter.CloseShutter();
            leverAudio.PlayOneShot(leverOffSound);
        }
    }

    private void ApplyDragRotation()
    {
        if (_leverArm == null) return;
        _leverArm.localRotation = Quaternion.Lerp(
            Quaternion.Euler(_bottomRot),
            Quaternion.Euler(_topRot),
            _dragT
        );
    }

    private void SnapLeverArmToState(bool isUp)
    {
        if (_leverArm == null) return;
        if (_isDragging) return; // local player is driving — don't fight the input
        _leverArm.DOKill();
        _leverArm.localRotation = Quaternion.Euler(isUp ? _topRot : _bottomRot);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetLeverServerRpc(bool isUp, ulong senderClientId)
    {
        _isUp.Value = isUp;
        BroadcastLeverStateClientRpc(isUp, senderClientId);
    }

    [ClientRpc]
    private void BroadcastLeverStateClientRpc(bool isUp, ulong excludeClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == excludeClientId) return;

        SnapLeverArmToState(isUp);
        leverAudio.PlayOneShot(isUp ? leverOnSound : leverOffSound);

        if (isUp)
            shutter.OpenShutter();
        else
            shutter.CloseShutter();
    }

    private void OnLeverStateChanged(bool oldValue, bool newValue)
    {
        // Catch-up for late-joining clients that missed the ClientRpc.
        // Don't cut short a commit/animate tween that is already easing the arm into place.
        if (_leverArm == null || !DOTween.IsTweening(_leverArm))
            SnapLeverArmToState(newValue);

        if (newValue)
            shutter.OpenShutter();
        else
            shutter.CloseShutter();
    }

    /// <summary>
    /// Enables or disables player interaction with the lever on all clients.
    /// Non-interactable levers suppress highlighting and silently reject Interact calls.
    /// Server-only.
    /// </summary>
    public void SetInteractable(bool interactable)
    {
        if (!IsServer) return;
        _isInteractable = interactable;
        Highlight(false);
        SetInteractableClientRpc(interactable);
    }

    /// <summary>
    /// Animates the lever arm to the open (up) position over <paramref name="duration"/> seconds
    /// and syncs the network state to open on all clients.
    /// The NetworkVariable change propagates to all clients via OnLeverStateChanged, which
    /// opens the shutter — so calling ShutterController.OpenShutter() separately is not required.
    /// Server-only.
    /// </summary>
    public void AnimateOpenServerSide(float duration = 1f)
    {
        if (!IsServer) return;
        _isUp.Value = true;
        AnimateLeverArmClientRpc(true, duration);
    }

    [ClientRpc]
    private void SetInteractableClientRpc(bool interactable)
    {
        _isInteractable = interactable;
        Highlight(false);
    }

    [ClientRpc]
    private void AnimateLeverArmClientRpc(bool isUp, float duration)
    {
        if (_leverArm == null) return;
        _leverArm.DOKill();
        _leverArm.DOLocalRotate(isUp ? _topRot : _bottomRot, duration).SetEase(Ease.InOutSine);
    }

    /// <summary>
    /// Raises the lever on the server and broadcasts visuals to all clients,
    /// opening the shutter. Must be called on the server.
    /// </summary>
    public void OpenServerSide()
    {
        if (!IsServer) return;
        _isUp.Value = true;
        BroadcastLeverStateClientRpc(true, ulong.MaxValue);
    }

    /// <summary>
    /// Resets the lever to the down state on the server and broadcasts to all clients.
    /// No-ops if the lever is already down to avoid spurious audio.
    /// </summary>
    public void Reset()
    {
        if (!IsServer) return;
        if (!_isUp.Value) return;
        _isUp.Value = false;
        BroadcastLeverStateClientRpc(false, ulong.MaxValue);
    }
}
