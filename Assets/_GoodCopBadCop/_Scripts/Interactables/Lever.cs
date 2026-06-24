using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

public class Lever : Interactable
{
    [SerializeField] private AudioSource leverAudio;
    [SerializeField] private AudioClip leverOnSound;
    [SerializeField] private AudioClip leverOffSound;
    [SerializeField] private ShutterController shutter;

    [Header("Camera & IK")]
    [Tooltip("Child Transform the player camera DOTweens to during the interaction.")]
    [SerializeField] private Transform _camPos;

    [Tooltip("World Transform the right-arm IK anchors to while the player holds the lever.")]
    [SerializeField] private Transform _ikTarget;

    [Tooltip("World Transform the player's head look-at is pinned to. Leave empty to fall back to the lever's own transform.")]
    [SerializeField] private Transform _lookTarget;

    [Tooltip("Seconds the camera takes to reach _camPos.")]
    [SerializeField] private float _cameraMoveDuration = 0.5f;

    [Tooltip("Seconds the camera takes to return to the normal position after releasing.")]
    [SerializeField] private float _cameraReturnDuration = 0.25f;

    [Header("Lever Arm")]
    [Tooltip("The child Transform that visually represents the lever arm.")]
    [SerializeField] private Transform _leverArm;

    [Tooltip("Local euler angles of the lever arm when fully UP (shutter open).")]
    [SerializeField] private Vector3 _topRot = new Vector3(60f, -180f, 0f);

    [Tooltip("Local euler angles of the lever arm when fully DOWN (shutter closed).")]
    [SerializeField] private Vector3 _bottomRot = new Vector3(-60f, -180f, 0f);

    [Tooltip("Multiplier applied to raw Mouse Y axis input. Higher = faster lever travel per mouse movement.")]
    [SerializeField] private float _dragSensitivity = 0.05f;

    [Tooltip("Duration of the snap tween when the lever commits to an end position on mouse release.")]
    [SerializeField] private float _snapDuration = 0.1f;

    private const string GrabLeverBool = "GrabLever";

    private NetworkVariable<bool> _isUp = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsUp => _isUp.Value;

    /// <summary>Normalised lever position: 0 = bottom/down, 1 = top/up.</summary>
    private float _dragT = 0f;

    /// <summary>
    /// Tracks whether the local client has already triggered an open or close during the
    /// current drag. Seeded from <see cref="_isUp"/> on interaction start.
    /// </summary>
    private bool _localShutterOpen;

    private bool _inControl = false;
    private PlayerInteractionController _currentPlayer;

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

    private void Update()
    {
        if (!_inControl) return;
        if (_currentPlayer == null || !_currentPlayer.IsLocalPlayer) return;

        // Drag while LMB is held — accumulate into _dragT.
        // Input.GetAxis("Mouse Y") is already a per-frame delta; no Time.deltaTime needed.
        if (Input.GetMouseButton(0))
        {
            _dragT = Mathf.Clamp01(_dragT + Input.GetAxis("Mouse Y") * _dragSensitivity);
            ApplyDragRotation();
            CheckShutterThreshold();
        }

        // Release → commit position and exit.
        if (Input.GetMouseButtonUp(0))
        {
            CommitLever();
            StartCoroutine(ExitLeverView());
        }
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        if (_inControl) return;

        // Seed drag position and local shutter state from current network state and go live
        // immediately, so releasing before the camera finishes still triggers a proper exit.
        _dragT = _isUp.Value ? 1f : 0f;
        _localShutterOpen = _isUp.Value;
        _currentPlayer = player;
        _inControl = true;

        StartCoroutine(EnterLeverSequence(player));
    }

    private IEnumerator EnterLeverSequence(PlayerInteractionController player)
    {
        PlayerMovementController movement = player.playerMovementController;
        PlayerAnimationController anim = player.playerAnimationController;

        movement.SetCanControl(false);
        movement.LookAtTarget(transform);

        Transform lookPoint = _lookTarget != null ? _lookTarget : transform;
        anim.OverrideHeadLookAt(lookPoint.position);

        if (_ikTarget != null)
            anim.RightArmIKTarget = _ikTarget;

        if (_camPos != null)
        {
            movement.CameraTransform.DOMove(_camPos.position, _cameraMoveDuration);
            movement.CameraTransform.DORotate(_camPos.rotation.eulerAngles, _cameraMoveDuration)
                .OnUpdate(movement.SyncPitch);
        }

        // Start the grab reach shortly after camera begins moving.
        yield return new WaitForSeconds(0.1f);
        if (!_inControl) yield break; // Player already released — ExitLeverView handles cleanup.

        anim.EnableRightArmMask();
        anim.SetAnimBool(GrabLeverBool, true);

        // Ramp IK weight up and hold it — ExitLeverView ramps it back down.
        anim.SetRightArmRigWeightSmooth(1f, 0.2f);
    }

    private IEnumerator ExitLeverView()
    {
        if (!_inControl) yield break;

        _inControl = false;

        PlayerInteractionController player = _currentPlayer;
        _currentPlayer = null;

        if (player == null) yield break;

        PlayerMovementController movement = player.playerMovementController;
        PlayerAnimationController anim = player.playerAnimationController;

        // Kill any in-progress camera tweens before starting the return.
        movement.CameraTransform.DOKill();

        anim.SetAnimBool(GrabLeverBool, false);
        anim.SetRightArmRigWeightSmooth(0f, 0.2f);

        yield return new WaitForSeconds(0.3f);

        anim.OverrideHeadLookAt(null);
        anim.SetBodyLeanDirect(0f);
        movement.ResetCameraPos(false, _cameraReturnDuration);

        yield return new WaitForSeconds(_cameraReturnDuration);

        anim.DisableRightArmMask();
        movement.SetCanControl(true);
    }

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
        _leverArm.localRotation = Quaternion.Euler(isUp ? _topRot : _bottomRot);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetLeverServerRpc(bool isUp, ulong senderClientId)
    {
        _isUp.Value = isUp;
        BroadcastLeverStateClientRpc(isUp, senderClientId);
    }

    /// <summary>
    /// Applies the committed lever state on all clients except the one that already
    /// predicted it locally.
    /// </summary>
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
        SnapLeverArmToState(newValue);

        if (newValue)
            shutter.OpenShutter();
        else
            shutter.CloseShutter();
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
