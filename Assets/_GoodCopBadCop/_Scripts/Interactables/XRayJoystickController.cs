using UnityEngine;

/// <summary>
/// Interactable joystick that controls the X-ray monitor position on its rail.
/// On interaction: locks the player, hides the first-person arms and body mesh,
/// activates the X-ray Cinemachine camera, and shows the Back UI.
/// Horizontal/Vertical axes move the monitor within its configured X/Y window.
/// Q or the Back button exits the view and restores everything.
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

    private bool _inControl = false;
    private PlayerInteractionController _currentPlayer;
    private Vector3 _monitorStartLocalPos;

    // Cached so they can be restored on exit.
    private GameObject _playerArms;
    private GameObject _playerBody;

    protected override void Awake()
    {
        base.Awake();

        if (_monitorTransform != null)
            _monitorStartLocalPos = _monitorTransform.localPosition;
    }

    private void Update()
    {
        if (!_inControl) return;
        if (_currentPlayer == null || !_currentPlayer.IsLocalPlayer) return;

        HandleMonitorMovement();
    }

    /// <summary>
    /// Entry point called by <see cref="PlayerInteractionController"/> when the player clicks
    /// the joystick. Locks player controls, hides arms and body, activates the X-ray camera,
    /// and registers the Back UI exit callback.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        if (_inControl) return;

        _currentPlayer = player;
        _inControl = true;

        player.playerMovementController.SetCanControl(false);

        // Hide first-person arms — same paths used by DiegeticViewController.
        _playerArms = player.transform.Find("CinemachineCamera/Arms_Socket/Player_Arms")?.gameObject;
        if (_playerArms != null)
            _playerArms.SetActive(false);

        // Hide the body mesh so it doesn't clip into the X-ray camera view.
        _playerBody = player.transform.Find("Art")?.gameObject;
        if (_playerBody != null)
            _playerBody.SetActive(false);

        // Switch to the X-ray Cinemachine camera — Cinemachine blends automatically.
        if (_xRayCameraGO != null)
            _xRayCameraGO.SetActive(true);

        // Show the Back UI. UIController forwards Q/Back key presses to this callback
        // automatically whenever DiegeticViewController.IsAnyViewActive is false.
        UIController.Instance.ShowBackButton(ExitJoystickView);
    }

    /// <summary>
    /// Translates the monitor transform within its X/Y window based on joystick/keyboard input.
    /// Called each frame while <see cref="_inControl"/> is true.
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

    /// <summary>
    /// Callback registered with the Back UI. Tears down the view and restores player state.
    /// </summary>
    private void ExitJoystickView()
    {
        if (!_inControl) return;

        _inControl = false;

        PlayerInteractionController player = _currentPlayer;
        _currentPlayer = null;

        if (player == null) return;

        // Deactivate the X-ray camera — Cinemachine blends back to the player camera.
        if (_xRayCameraGO != null)
            _xRayCameraGO.SetActive(false);

        // Restore the first-person arms.
        if (_playerArms != null)
        {
            _playerArms.SetActive(true);
            _playerArms = null;
        }

        // Restore the body mesh.
        if (_playerBody != null)
        {
            _playerBody.SetActive(true);
            _playerBody = null;
        }

        UIController.Instance.HideBackButton();

        player.playerMovementController.SetCanControl(true);
    }
}
