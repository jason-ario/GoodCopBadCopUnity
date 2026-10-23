using DG.Tweening;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A lever-style power switch located at the power station.
///
/// Interaction rules (same as the booth shutter <see cref="Lever"/>):
///   – Pressing Interact opens the switch's diegetic view (<see cref="LeverDiegeticController"/>).
///   – Inside the view, click (LMB / RT) on the switch and drag up/down — or push the gamepad
///     vertical axis — to move the handle; the player's free hand grips it via arm IK.
///   – On release the handle snaps to the nearest end (up or down). The view stays open until
///     the player exits with the exit key / Back button.
///   – When committed to the DOWN position with <see cref="FuseBoxPuzzleController.IsReady"/>
///     and power off, the server plays a loud activation sound on all clients (once)
///     and calls <see cref="ElectricityController.PowerOn"/> to restore electricity.
///   – A <see cref="Reset"/> method (server-only) snaps the switch back to the UP
///     position — call this when a new power outage begins.
///
/// Setup notes:
///   - Requires a <see cref="NetworkObject"/> on the same prefab root.
///   - Assign <see cref="_fuseBoxController"/> and <see cref="_electricityController"/>.
///   - Assign the handle child to <see cref="_handle"/> (rotates on drag).
///   - Assign a <see cref="LeverDiegeticController"/> (with its own CinemachineCamera) to <see cref="_diegeticView"/>.
/// </summary>
public class PowerSwitch : Interactable, IDiegeticLever
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private FuseBoxPuzzleController _fuseBoxController;
    [SerializeField] private ElectricityController   _electricityController;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [Tooltip("Plays once on all clients when the switch actually restores power (fuses in, power off).")]
    [SerializeField] private AudioClip _activateSound;
    [SerializeField] private AudioClip _switchOnSound;
    [SerializeField] private AudioClip _switchOffSound;
    [Tooltip("Disembodied creepy laugh played on all clients shortly after the switch restores power " +
             "(same clip as Ocho's booth-outage laugh). Leave empty to skip.")]
    [SerializeField] private AudioClip _creepyLaughClip;
    [Tooltip("Seconds after power is restored before the creepy laugh plays.")]
    [SerializeField] private float _creepyLaughDelay = 1.5f;
    [SerializeField] private float _creepyLaughVolume = 1f;
    [SerializeField] private float _creepyLaughMaxDistance = 30f;

    [Header("Diegetic View")]
    [Tooltip("The close-up view opened when the player presses Interact on the switch.")]
    [SerializeField] private LeverDiegeticController _diegeticView;
    [Tooltip("Optional. Prevents a second player entering the switch view while someone is using it.")]
    [SerializeField] private DiegeticOccupancy _occupancy;

    [Header("Hand IK")]
    [Tooltip("Right-arm IK target while the player grips the switch.")]
    [SerializeField] private Transform _rightIkTarget;
    [Tooltip("Left-arm IK target while the player grips the switch (used when the right hand holds an item).")]
    [SerializeField] private Transform _leftIkTarget;
    [Tooltip("Head look-at pin while gripping. Falls back to this transform if unset.")]
    [SerializeField] private Transform _lookTarget;
    [Tooltip("Seconds the arm IK takes to blend onto / off the switch.")]
    [SerializeField] private float _handReachDuration = 0.2f;

    [Header("Handle")]
    [Tooltip("The child Transform that visually represents the switch handle.")]
    [SerializeField] private Transform _handle;
    [Tooltip("Local euler angles when the switch is fully UP (resting position).")]
    [SerializeField] private Vector3 _upRot    = new Vector3( 60f, -180f, 0f);
    [Tooltip("Local euler angles when the switch is fully DOWN (activated position).")]
    [SerializeField] private Vector3 _downRot  = new Vector3(-60f, -180f, 0f);
    [Tooltip("Multiplier applied to raw Mouse Y while dragging. Higher = faster handle travel.")]
    [SerializeField] private float _dragSensitivity = 0.05f;
    [Tooltip("Units per second the handle travels at full stick deflection (controller only).")]
    [SerializeField] private float _controllerDragSpeed = 1.5f;
    [Tooltip("Duration of the snap tween when committing to an end position.")]
    [SerializeField] private float _snapDuration = 0.1f;

    // ── Network state ─────────────────────────────────────────────────────────

    /// <summary>True while the switch is in the activated (down) position.</summary>
    private NetworkVariable<bool> _isDown = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ── Local interaction state ───────────────────────────────────────────────

    /// <summary>Normalised handle position: 0 = up, 1 = down.</summary>
    private float _dragT = 0f;
    private bool _localIsDown;
    private bool _isDragging;
    private bool _isInteractable = true;

    private PlayerAnimationController _grabAnim;
    private bool _grabRightArm;

    private const string RightGripBool = "RightGrip";
    private const string LeftGripBool  = "LeftGrip";

    /// <summary>Whether the switch currently accepts player input (see <see cref="SetInteractable"/>).</summary>
    public bool IsUsable => _isInteractable;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        // Fall back to the conventional layout: a child "Diegetic View" and occupancy on this root.
        if (_diegeticView == null) _diegeticView = GetComponentInChildren<LeverDiegeticController>(true);
        if (_occupancy == null)    _occupancy    = GetComponent<DiegeticOccupancy>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isDown.OnValueChanged += OnSwitchStateChanged;
        SnapHandleToState(_isDown.Value);

        if (_fuseBoxController != null)
        {
            _fuseBoxController.OnFuseCountChanged += OnFuseCountChanged;
            // Late-join / already-ready catch-up.
            SetForceHighlight(_fuseBoxController.IsReady && !_isDown.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        _isDown.OnValueChanged -= OnSwitchStateChanged;

        if (_fuseBoxController != null)
            _fuseBoxController.OnFuseCountChanged -= OnFuseCountChanged;
    }

    /// <summary>
    /// Highlights the switch as soon as every fuse slot is filled, prompting the player to
    /// flip it. Clears again if a fuse is removed before the switch is committed.
    /// </summary>
    private void OnFuseCountChanged(int filled, int total)
    {
        if (_isDown.Value) return; // Already flipped — nothing to prompt.
        SetForceHighlight(_fuseBoxController != null && _fuseBoxController.IsReady);
    }

    // ── Interact ──────────────────────────────────────────────────────────────

    protected override string DefaultInteractVerb => Loc.N("Flip");

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        if (!_isInteractable) return;
        if (_diegeticView == null)
        {
            Debug.LogWarning($"[PowerSwitch] '{name}' has no LeverDiegeticController assigned — Interact does nothing.", this);
            return;
        }
        if (DiegeticViewController.IsAnyViewActive) return;
        if (_occupancy != null && !_occupancy.TryClaim(player)) return;

        _diegeticView.Open(player);
    }

    // ── Drag API (driven by LeverDiegeticController) ─────────────────────────

    public void BeginDrag()
    {
        if (_isDragging) return;
        _isDragging = true;

        _handle?.DOKill();
        _dragT       = _isDown.Value ? 1f : 0f;
        _localIsDown = _isDown.Value;
    }

    // Input is "positive = up"; _dragT runs 0 (up) → 1 (down), hence the sign flip.
    public void DragByMouse(float mouseYDelta) => DragBy(-mouseYDelta * _dragSensitivity);

    public void DragByStick(float stickY) => DragBy(-stickY * _controllerDragSpeed * Time.deltaTime);

    public void EndDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        CommitSwitch();
    }

    public void EndDragToward(bool up)
    {
        if (!_isDragging) return;
        _isDragging = false;
        _dragT = up ? 0f : 1f;
        CommitSwitch();
    }

    private void DragBy(float deltaT)
    {
        if (!_isDragging) return;
        _dragT = Mathf.Clamp01(_dragT + deltaT);
        ApplyDragRotation();
        CheckAudioThreshold();
    }

    // ── Hand grab (driven by LeverDiegeticController) ────────────────────────

    /// <summary>
    /// Reaches the player's free hand onto the handle (grip pose + arm IK). Uses the right
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

    // ── Drag helpers ──────────────────────────────────────────────────────────

    private void ApplyDragRotation()
    {
        if (_handle == null) return;
        _handle.localRotation = Quaternion.Lerp(
            Quaternion.Euler(_upRot),
            Quaternion.Euler(_downRot),
            _dragT
        );
    }

    /// <summary>Plays audio feedback when crossing the drag thresholds.</summary>
    private void CheckAudioThreshold()
    {
        if (_dragT >= 0.9f && !_localIsDown)
        {
            _localIsDown = true;
            if (_audioSource != null && _switchOffSound != null)
                _audioSource.PlayOneShot(_switchOffSound);
        }
        else if (_dragT <= 0.1f && _localIsDown)
        {
            _localIsDown = false;
            if (_audioSource != null && _switchOnSound != null)
                _audioSource.PlayOneShot(_switchOnSound);
        }
    }

    /// <summary>
    /// Snaps _dragT to the nearest end and sends an RPC if the state changed.
    /// Activating the DOWN position triggers the power-restore attempt on the server.
    /// </summary>
    private void CommitSwitch()
    {
        bool newIsDown = _dragT >= 0.5f;
        _dragT = newIsDown ? 1f : 0f;

        if (newIsDown)
            SetForceHighlight(false);

        if (_handle != null)
        {
            _handle.DOLocalRotate(newIsDown ? _downRot : _upRot, _snapDuration)
                .SetEase(Ease.OutBack);
        }

        // Sync audio for any threshold not yet crossed during the drag.
        if (newIsDown != _localIsDown)
        {
            _localIsDown = newIsDown;
            if (_audioSource != null)
            {
                AudioClip clip = newIsDown ? _switchOffSound : _switchOnSound;
                if (clip != null) _audioSource.PlayOneShot(clip);
            }
        }

        // Always send the RPC — server guards against redundant state sets.
        SetSwitchServerRpc(newIsDown, NetworkManager.Singleton.LocalClientId);
    }

    private void SnapHandleToState(bool isDown)
    {
        if (_handle == null) return;
        if (_isDragging) return; // local player is driving — don't fight the input
        _handle.DOKill();
        _handle.localRotation = Quaternion.Euler(isDown ? _downRot : _upRot);
    }

    // ── Network ───────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void SetSwitchServerRpc(bool isDown, ulong senderClientId)
    {
        _isDown.Value = isDown;

        // Broadcast visuals to all other clients.
        BroadcastSwitchStateClientRpc(isDown, senderClientId);

        // Only the DOWN commit triggers the power-restore attempt.
        if (!isDown) return;

        // The big activation sound plays on all clients only when this pull actually
        // restores power (fuses in, power currently off) — once per activation.
        if (_fuseBoxController != null && _fuseBoxController.IsReady
            && _electricityController != null && !_electricityController.IsPowerOn)
        {
            PlayActivateSoundClientRpc();
            _electricityController.PowerOn();
            Debug.Log("[PowerSwitch] Fuse box ready — power restored.");
        }
        else
        {
            Debug.Log("[PowerSwitch] Committed DOWN — fuse box not ready or power already on.");
        }
    }

    [ClientRpc]
    private void BroadcastSwitchStateClientRpc(bool isDown, ulong excludeClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == excludeClientId) return;

        SnapHandleToState(isDown);

        if (_audioSource != null)
        {
            AudioClip clip = isDown ? _switchOffSound : _switchOnSound;
            if (clip != null) _audioSource.PlayOneShot(clip);
        }
    }

    [ClientRpc]
    private void PlayActivateSoundClientRpc()
    {
        if (_audioSource != null && _activateSound != null)
            _audioSource.PlayOneShot(_activateSound);

        if (_creepyLaughClip != null)
            StartCoroutine(PlayCreepyLaughAfterDelay());
    }

    private IEnumerator PlayCreepyLaughAfterDelay()
    {
        yield return new WaitForSeconds(_creepyLaughDelay);

        // Standalone spatial emitter, matching how OchoBoothEncounter plays its laugh.
        if (SFXController.Instance != null)
            SFXController.Instance.PlayAtPosition(_creepyLaughClip, transform.position,
                _creepyLaughVolume, 1f, _creepyLaughMaxDistance);
        else if (_audioSource != null)
            _audioSource.PlayOneShot(_creepyLaughClip, _creepyLaughVolume);
    }

    private void OnSwitchStateChanged(bool oldValue, bool newValue)
    {
        // Late-join catch-up. Don't cut short a commit tween already easing the handle into place.
        if (_handle == null || !DOTween.IsTweening(_handle))
            SnapHandleToState(newValue);
        if (newValue) SetForceHighlight(false);
    }

    // ── Server utilities ──────────────────────────────────────────────────────

    /// <summary>
    /// Resets the switch to the UP position on all clients.
    /// Call this server-side at the start of a new power outage.
    /// </summary>
    public void Reset()
    {
        if (!IsServer) return;
        if (!_isDown.Value) return;
        _isDown.Value = false;
        BroadcastSwitchStateClientRpc(false, ulong.MaxValue);
        SetForceHighlight(_fuseBoxController != null && _fuseBoxController.IsReady);
    }

    /// <summary>Enables or disables player interaction. Server-only.</summary>
    public void SetInteractable(bool interactable)
    {
        if (!IsServer) return;
        _isInteractable = interactable;
        Highlight(false);
        SetInteractableClientRpc(interactable);
    }

    [ClientRpc]
    private void SetInteractableClientRpc(bool interactable)
    {
        _isInteractable = interactable;
        Highlight(false);
    }
}
