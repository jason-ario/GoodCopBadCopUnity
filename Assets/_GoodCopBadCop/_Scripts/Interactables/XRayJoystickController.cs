using GoodCopBadCop.Input;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Interactable joystick that controls the X-ray monitor position on its rail.
/// While active, the mouse (or gamepad right stick) moves the monitor and LMB / RT cycles
/// the X-ray feed's scan mode (Skeletal, Deep Tissue, Organs, ...).
///
/// Networking (server-authoritative NetworkVariables, requests via server RPCs):
///   - Scan mode: every client's monitor shows the same mode; late joiners get it on spawn.
///   - Operator: one player at a time; others can't interact while it's in use. Cleared if the
///     operator disconnects.
///   - Monitor position: the operator drives it locally and publishes it (throttled); every other
///     client smoothly follows it while the machine is operated, and parks it when it's free.
///
/// State machine:
///   Off        — monitor lerps to its idle (X, Y) park position.
///   TurningOn  — player has interacted; camera activates immediately, monitor lerps to the
///                centre of the working window at 2× transition speed.
///   Active     — player drives the monitor with the joystick.
///   TurningOff — player exited; camera/UI torn down immediately, monitor lerps back to idle.
/// </summary>
public class XRayJoystickController : Interactable
{
    [Header("X-Ray Camera")]
    [Tooltip("The X Ray Camera GameObject to activate while the player is in control.")]
    [SerializeField] private GameObject _xRayCameraGO;

    [Header("Monitor Movement")]
    [Tooltip("The Transform of the monitor (swivel) that slides along the rail.")]
    [SerializeField] private Transform _monitorTransform;

    [Tooltip("Units per second the monitor moves at full right-stick deflection.")]
    [SerializeField] private float _monitorMoveSpeed = 1f;

    [Tooltip("Local units the monitor moves per unit of mouse axis delta (Mouse X / Mouse Y).")]
    [SerializeField] private float _mouseSensitivity = 0.004f;

    [Tooltip("Right-stick deadzone.")]
    [SerializeField] private float _stickDeadzone = 0.15f;

    [Tooltip("Maximum horizontal offset (X) the monitor can travel from its resting position.")]
    [SerializeField] private float _maxMoveX = 0.5f;

    [Tooltip("Maximum vertical offset (Y) the monitor can travel from its resting position.")]
    [SerializeField] private float _maxMoveY = 0.5f;

    [Tooltip("Shifts the centre of the Y movement window up (positive) or down (negative) from the monitor's resting position.")]
    [SerializeField] private float _yOffset = 0f;

    [Header("Scan Modes")]
    [Tooltip("The X-ray feed camera. LMB / UseItem (RT) cycles its scan mode while the player is in control.")]
    [SerializeField] private XRayCamera _xRayFeed;

    [Header("Idle / Off Position")]
    [Tooltip("X offset from the monitor's resting position it parks at when not in use.")]
    [SerializeField] private float _idleXOffset = 0f;

    [Tooltip("How far above the top of the Y window the monitor parks when not in use.")]
    [SerializeField] private float _offYAboveMax = 0.5f;

    [Tooltip("Local units per second the monitor moves during on/off transitions. The TurningOn lerp runs at 2x this value.")]
    [SerializeField] private float _transitionSpeed = 1f;

    [Header("Networking")]
    [Tooltip("Seconds between monitor position updates sent by the operator.")]
    [SerializeField] private float _positionSendInterval = 0.05f;

    [Tooltip("How quickly remote clients' monitors catch up with the synced position (higher = snappier).")]
    [SerializeField] private float _remoteFollowSharpness = 12f;

    // ─── Network state ───────────────────────────────────────────────────────

    private const ulong NoOperator = ulong.MaxValue;

    private readonly NetworkVariable<int> _netMode = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<ulong> _netOperator = new NetworkVariable<ulong>(
        NoOperator, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<Vector2> _netMonitorPos = new NetworkVariable<Vector2>(
        Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float _nextPositionSendTime;
    private Vector2 _lastSentPosition = new Vector2(float.NaN, float.NaN);

    /// <summary>Someone else (on another client) is operating the machine.</summary>
    private bool IsOperatedByOther =>
        IsSpawned && _currentPlayer == null &&
        _netOperator.Value != NoOperator && _netOperator.Value != NetworkManager.LocalClientId;

    public override bool IsInteractable => base.IsInteractable && !IsOperatedByOther;

    // ─── State ───────────────────────────────────────────────────────────────

    private enum MonitorState { Off, TurningOn, Active, TurningOff }
    private MonitorState _monitorState = MonitorState.Off;

    private PlayerInteractionController _currentPlayer;
    private Vector3 _monitorStartLocalPos;

    // Cached so they can be restored on exit.
    private GameObject _playerArms;
    private GameObject _playerBody;

    // ─── Computed positions ───────────────────────────────────────────────────

    /// <summary>Centre of the X working window — the target X when turning on.</summary>
    private float OnLocalX   => _monitorStartLocalPos.x;

    /// <summary>Centre of the Y working window — the target Y when turning on.</summary>
    private float OnLocalY   => _monitorStartLocalPos.y + _yOffset;

    /// <summary>X position the monitor returns to when idle.</summary>
    private float IdleLocalX => _monitorStartLocalPos.x + _idleXOffset;

    /// <summary>Y position the monitor parks at when off — above the top of the working window.</summary>
    private float IdleLocalY => _monitorStartLocalPos.y + _yOffset + _maxMoveY + _offYAboveMax;

    // ─── Unity ───────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        if (_monitorTransform != null)
        {
            _monitorStartLocalPos = _monitorTransform.localPosition;

            // Snap to idle position immediately so the monitor is never seen in the wrong spot on load.
            Vector3 idle = _monitorStartLocalPos;
            idle.x = _monitorStartLocalPos.x + _idleXOffset;
            idle.y = _monitorStartLocalPos.y + _yOffset + _maxMoveY + _offYAboveMax;
            _monitorTransform.localPosition = idle;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _netMode.OnValueChanged += OnNetModeChanged;
        if (_xRayFeed != null) _xRayFeed.SetMode(_netMode.Value);

        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        _netMode.OnValueChanged -= OnNetModeChanged;
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        switch (_monitorState)
        {
            case MonitorState.Off:
                if (IsOperatedByOther) FollowRemoteMonitor();
                else MoveMonitorToward(IdleLocalX, IdleLocalY, _transitionSpeed);
                break;

            case MonitorState.TurningOn:
                if (LostOperatorClaim()) { ExitJoystickView(); break; }
                // Run at 2x speed; switch to Active once the monitor reaches centre.
                if (MoveMonitorToward(OnLocalX, OnLocalY, _transitionSpeed * 2f))
                    _monitorState = MonitorState.Active;
                PublishMonitorPosition(false);
                break;

            case MonitorState.Active:
                if (LostOperatorClaim()) { ExitJoystickView(); break; }
                if (_currentPlayer != null && _currentPlayer.IsLocalPlayer && !IsInputBlocked)
                {
                    // Mouse drives the monitor, so keep the cursor captured (Escape / B still exit).
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;

                    HandleMonitorMovement();
                    HandleModeCycle();
                }
                PublishMonitorPosition(false);
                break;

            case MonitorState.TurningOff:
                if (MoveMonitorToward(IdleLocalX, IdleLocalY, _transitionSpeed))
                    _monitorState = MonitorState.Off;
                break;
        }
    }

    // ─── Interaction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the player clicks the joystick. Immediately locks the player, hides their
    /// mesh, activates the X-ray camera, and starts the monitor sliding to centre.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        if (_monitorState == MonitorState.Active || _monitorState == MonitorState.TurningOn) return;
        if (IsOperatedByOther) return;

        _currentPlayer = player;
        _monitorState  = MonitorState.TurningOn;

        // Claim the machine so other players can't use it at the same time.
        if (IsSpawned) SetOperatingServerRpc(true);
        PublishMonitorPosition(true);

        player.playerMovementController.SetCanControl(false);

        // Hide first-person arms — same paths used by DiegeticViewController.
        _playerArms = player.transform.Find("CinemachineCamera/Arms_Socket/Player_Arms")?.gameObject;
        if (_playerArms != null)
            _playerArms.SetActive(false);

        // Hide the body mesh so it doesn't clip into the X-ray camera view.
        _playerBody = player.transform.Find("Art")?.gameObject;
        if (_playerBody != null)
            _playerBody.SetActive(false);

        // Activate camera immediately — player sees the X-ray view while the monitor slides in.
        if (_xRayCameraGO != null)
            _xRayCameraGO.SetActive(true);

        UIController.Instance.ShowBackButton(ExitJoystickView);
    }

    /// <summary>
    /// Callback registered with the Back UI. Immediately tears down the view and starts
    /// the monitor sliding back to its idle position.
    /// </summary>
    private void ExitJoystickView()
    {
        if (_monitorState != MonitorState.Active && _monitorState != MonitorState.TurningOn) return;

        _monitorState = MonitorState.TurningOff;

        // Release the machine; other clients park their monitor once the operator clears.
        if (IsSpawned) SetOperatingServerRpc(false);

        if (_xRayCameraGO != null)
            _xRayCameraGO.SetActive(false);

        if (_playerArms != null)
        {
            _playerArms.SetActive(true);
            _playerArms = null;
        }

        if (_playerBody != null)
        {
            _playerBody.SetActive(true);
            _playerBody = null;
        }

        UIController.Instance.HideBackButton();

        if (_currentPlayer != null)
        {
            _currentPlayer.playerMovementController.SetCanControl(true);
            _currentPlayer = null;
        }
    }

    // ─── Monitor helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Moves the monitor's local position toward (<paramref name="targetX"/>, <paramref name="targetY"/>)
    /// at <paramref name="speed"/> units per second. Returns true when both axes have arrived.
    /// </summary>
    private bool MoveMonitorToward(float targetX, float targetY, float speed)
    {
        if (_monitorTransform == null) return true;

        Vector3 localPos = _monitorTransform.localPosition;
        localPos.x = Mathf.MoveTowards(localPos.x, targetX, speed * Time.deltaTime);
        localPos.y = Mathf.MoveTowards(localPos.y, targetY, speed * Time.deltaTime);
        _monitorTransform.localPosition = localPos;

        return Mathf.Approximately(localPos.x, targetX) && Mathf.Approximately(localPos.y, targetY);
    }

    /// <summary>Paused (pause menu owns the cursor) — skip monitor input.</summary>
    private static bool IsInputBlocked => UIController.Instance != null && UIController.Instance.IsPaused;

    /// <summary>LMB / UseItem (RT) advances the X-ray feed to its next scan mode (synced to all clients).</summary>
    private void HandleModeCycle()
    {
        if (_xRayFeed == null || _xRayFeed.Modes.Count == 0) return;
        if (!Input.GetMouseButtonDown(0) && !RebindableInput.GetGamepadDown(GameAction.UseItem)) return;

        int next = (_xRayFeed.ModeIndex + 1) % _xRayFeed.Modes.Count;
        _xRayFeed.SetMode(next); // predict locally for instant feedback; the server echo is identical
        if (IsSpawned) SetModeServerRpc(next);
    }

    // ─── Networking ──────────────────────────────────────────────────────────

    /// <summary>
    /// True when the server has given the machine to a different client (e.g. two players
    /// interacted at the same moment and the other one won). The local view should close.
    /// </summary>
    private bool LostOperatorClaim() =>
        IsSpawned && _netOperator.Value != NoOperator && _netOperator.Value != NetworkManager.LocalClientId;

    /// <summary>Sends the local monitor position to the server (throttled unless <paramref name="force"/>).</summary>
    private void PublishMonitorPosition(bool force)
    {
        if (!IsSpawned || _monitorTransform == null || _currentPlayer == null) return;
        if (!force && Time.unscaledTime < _nextPositionSendTime) return;

        Vector2 pos = _monitorTransform.localPosition;
        if (!force && (pos - _lastSentPosition).sqrMagnitude < 1e-8f) return;

        _nextPositionSendTime = Time.unscaledTime + _positionSendInterval;
        _lastSentPosition = pos;
        SetMonitorPositionServerRpc(pos);
    }

    /// <summary>Remote clients: smoothly follow the operator's synced monitor position.</summary>
    private void FollowRemoteMonitor()
    {
        if (_monitorTransform == null) return;

        Vector2 target = _netMonitorPos.Value;
        Vector3 localPos = _monitorTransform.localPosition;
        float t = 1f - Mathf.Exp(-_remoteFollowSharpness * Time.deltaTime);
        localPos.x = Mathf.Lerp(localPos.x, target.x, t);
        localPos.y = Mathf.Lerp(localPos.y, target.y, t);
        _monitorTransform.localPosition = localPos;
    }

    private void OnNetModeChanged(int previous, int current)
    {
        if (_xRayFeed != null) _xRayFeed.SetMode(current);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (IsServer && _netOperator.Value == clientId)
            _netOperator.Value = NoOperator;
    }

    [Rpc(SendTo.Server)]
    private void SetOperatingServerRpc(bool operating, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (operating)
        {
            if (_netOperator.Value == NoOperator) _netOperator.Value = sender;
        }
        else if (_netOperator.Value == sender)
        {
            _netOperator.Value = NoOperator;
        }
    }

    [Rpc(SendTo.Server)]
    private void SetModeServerRpc(int mode) => _netMode.Value = Mathf.Max(0, mode);

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    private void SetMonitorPositionServerRpc(Vector2 position, RpcParams rpcParams = default)
    {
        // Only the current operator may move the monitor.
        ulong sender = rpcParams.Receive.SenderClientId;
        if (_netOperator.Value == sender || _netOperator.Value == NoOperator)
            _netMonitorPos.Value = position;
    }

    /// <summary>
    /// Translates the monitor within its X/Y window. Mouse delta moves it directly;
    /// the gamepad right stick moves it at <see cref="_monitorMoveSpeed"/>.
    /// </summary>
    private void HandleMonitorMovement()
    {
        if (_monitorTransform == null) return;

        // Mouse axes are already per-frame deltas.
        float dx = Input.GetAxis("Mouse X") * _mouseSensitivity;
        float dy = Input.GetAxis("Mouse Y") * _mouseSensitivity;

        // Right stick is continuous, so scale by deltaTime.
        Vector2 stick = Gamepad.current?.rightStick.ReadValue() ?? Vector2.zero;
        if (stick.magnitude < _stickDeadzone) stick = Vector2.zero;
        dx += stick.x * _monitorMoveSpeed * Time.deltaTime;
        dy += stick.y * _monitorMoveSpeed * Time.deltaTime;

        Vector3 localPos = _monitorTransform.localPosition;

        localPos.x = Mathf.Clamp(
            localPos.x + dx,
            _monitorStartLocalPos.x - _maxMoveX,
            _monitorStartLocalPos.x + _maxMoveX);

        localPos.y = Mathf.Clamp(
            localPos.y + dy,
            _monitorStartLocalPos.y + _yOffset - _maxMoveY,
            _monitorStartLocalPos.y + _yOffset + _maxMoveY);

        _monitorTransform.localPosition = localPos;
    }
}
