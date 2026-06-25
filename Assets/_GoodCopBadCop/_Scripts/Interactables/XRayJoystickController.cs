using UnityEngine;

/// <summary>
/// Interactable joystick that controls the X-ray monitor position on its rail.
///
/// State machine:
///   Off        — monitor lerps to its idle (X, Y) park position.
///   TurningOn  — player has interacted; camera activates immediately, monitor lerps to the
///                centre of the working window at 2× transition speed.
///   Active     — player drives the monitor with the joystick.
///   TurningOff — player exited; camera/UI torn down immediately, monitor lerps back to idle.
/// </summary>
public class XRayJoystickController : Interactable, IHeldItemPassthrough
{
    [Header("X-Ray Camera")]
    [Tooltip("The X Ray Camera GameObject to activate while the player is in control.")]
    [SerializeField] private GameObject _xRayCameraGO;

    [Header("Monitor Movement")]
    [Tooltip("The Transform of the monitor (swivel) that slides along the rail.")]
    [SerializeField] private Transform _monitorTransform;

    [Tooltip("Units per second the monitor moves in response to joystick input.")]
    [SerializeField] private float _monitorMoveSpeed = 1f;

    [Tooltip("Maximum horizontal offset (X) the monitor can travel from its resting position.")]
    [SerializeField] private float _maxMoveX = 0.5f;

    [Tooltip("Maximum vertical offset (Y) the monitor can travel from its resting position.")]
    [SerializeField] private float _maxMoveY = 0.5f;

    [Tooltip("Shifts the centre of the Y movement window up (positive) or down (negative) from the monitor's resting position.")]
    [SerializeField] private float _yOffset = 0f;

    [Tooltip("Input axis used for horizontal monitor movement.")]
    [SerializeField] private string _horizontalAxis = "Horizontal";

    [Tooltip("Input axis used for vertical monitor movement.")]
    [SerializeField] private string _verticalAxis = "Vertical";

    [Header("Idle / Off Position")]
    [Tooltip("X offset from the monitor's resting position it parks at when not in use.")]
    [SerializeField] private float _idleXOffset = 0f;

    [Tooltip("How far above the top of the Y window the monitor parks when not in use.")]
    [SerializeField] private float _offYAboveMax = 0.5f;

    [Tooltip("Local units per second the monitor moves during on/off transitions. The TurningOn lerp runs at 2x this value.")]
    [SerializeField] private float _transitionSpeed = 1f;

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

    private void Update()
    {
        switch (_monitorState)
        {
            case MonitorState.Off:
                MoveMonitorToward(IdleLocalX, IdleLocalY, _transitionSpeed);
                break;

            case MonitorState.TurningOn:
                // Run at 2x speed; switch to Active once the monitor reaches centre.
                if (MoveMonitorToward(OnLocalX, OnLocalY, _transitionSpeed * 2f))
                    _monitorState = MonitorState.Active;
                break;

            case MonitorState.Active:
                if (_currentPlayer != null && _currentPlayer.IsLocalPlayer)
                    HandleMonitorMovement();
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

        _currentPlayer = player;
        _monitorState  = MonitorState.TurningOn;

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

    /// <summary>
    /// Translates the monitor within its X/Y window based on joystick/keyboard input.
    /// </summary>
    private void HandleMonitorMovement()
    {
        if (_monitorTransform == null) return;

        float h = Input.GetAxis(_horizontalAxis);
        float v = Input.GetAxis(_verticalAxis);

        Vector3 localPos = _monitorTransform.localPosition;

        localPos.x = Mathf.Clamp(
            localPos.x + h * _monitorMoveSpeed * Time.deltaTime,
            _monitorStartLocalPos.x - _maxMoveX,
            _monitorStartLocalPos.x + _maxMoveX);

        localPos.y = Mathf.Clamp(
            localPos.y + v * _monitorMoveSpeed * Time.deltaTime,
            _monitorStartLocalPos.y + _yOffset - _maxMoveY,
            _monitorStartLocalPos.y + _yOffset + _maxMoveY);

        _monitorTransform.localPosition = localPos;
    }
}
