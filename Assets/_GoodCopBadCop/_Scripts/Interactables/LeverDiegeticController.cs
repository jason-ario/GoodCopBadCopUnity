using DG.Tweening;
using GoodCopBadCop.Input;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Diegetic view for the shutter <see cref="Lever"/>. Opened by pressing Interact on the lever.
/// Inside the view, clicking on the lever (LMB / RT) and dragging the mouse up/down (or right stick)
/// moves it; releasing commits it to the nearest end. With a gamepad, pushing up/down on the vertical
/// axis (left stick, D-pad or right stick) grabs and moves the lever directly, and letting go commits it
/// to the end it was pushed toward. Releasing does NOT exit the view — the player
/// leaves with the exit key / Back button, like the bunker door wheel.
/// The lever highlights while the cursor hovers it (and for the whole drag), and a
/// "[LMB] to drag" prompt shows while the view is open.
/// While dragging, the player's free hand grips the lever (grip pose + arm IK); the first-person
/// arms stay visible and the player camera that carries them is moved into the view pose.
/// </summary>
public class LeverDiegeticController : DiegeticViewController
{
    [Header("Lever")]
    [Tooltip("The lever this view drives.")]
    [SerializeField] private Lever _lever;

    [Tooltip("The lever arm's collider — clicking and dragging on it (while the view is open) moves the lever.")]
    [SerializeField] private Collider _leverCollider;

    [Tooltip("Tracks occupancy of the lever so it's released when the view closes.")]
    [SerializeField] private DiegeticOccupancy _occupancy;

    [Header("Prompt")]
    [Tooltip("Text shown next to the LMB / RT icon while the view is open.")]
    [SerializeField] private string _dragPromptText = "to drag";

    [Tooltip("Text shown next to the gamepad stick icon while a gamepad is active (the stick moves the lever directly).")]
    [SerializeField] private string _gamepadPromptText = "to pull";

    [Tooltip("Gamepad icon shown instead of RT while a gamepad is active (left stick up/down).")]
    [SerializeField] private Sprite _gamepadPromptSprite;

    [Header("First-Person Arms")]
    [Tooltip("Seconds the player camera (which carries the first-person arms) takes to move into the view, " +
             "so the hand-grab IK can reach the lever. Match roughly to the Cinemachine blend time.")]
    [SerializeField] private float _armsCameraMoveDuration = 0.5f;

    private bool _isDragging;
    private Transform _playerCam;
    private Quaternion _playerCamRestLocalRot;

    // Gamepad: pushing the vertical axis (left stick, D-pad or right stick) grabs and moves the lever
    // directly — there is no cursor to aim at the lever with a controller.
    private const float GamepadAxisDeadzone = 0.3f;
    private bool _gamepadDrag;
    private bool _gamepadLastPushUp;

    protected override bool SuppressCameraMovement => _isDragging;

    protected override string ActionPromptText => _dragPromptText;
    protected override string GamepadActionPromptText => _gamepadPromptText;
    protected override Sprite GamepadActionPromptSprite => _gamepadPromptSprite;

    // Arms stay visible so the hand can be seen gripping the lever.
    protected override bool HidePlayerArms => false;

    private bool GrabDown => Input.GetMouseButtonDown(0) || RebindableInput.GetGamepadDown(GameAction.UseItem);
    private bool GrabHeld => Input.GetMouseButton(0)     || RebindableInput.GetGamepadHeld(GameAction.UseItem);

    protected override void OnOpened()
    {
        _isDragging = false;
        _gamepadDrag = false;

        // The first-person arms are parented to the player camera, so move that camera into the
        // view pose (as the old Cam Pos tween did) — the arm IK can then reach the lever handle.
        _playerCam = Player != null ? Player.playerMovementController.CameraTransform : null;
        if (_playerCam != null && ViewCamera != null)
        {
            _playerCamRestLocalRot = _playerCam.localRotation;
            Transform view = ViewCamera.transform;
            _playerCam.DOKill();
            _playerCam.DOMove(view.position, _armsCameraMoveDuration);
            _playerCam.DORotateQuaternion(view.rotation, _armsCameraMoveDuration);
        }
    }

    protected override void OnClosed()
    {
        _gamepadDrag = false;
        if (_isDragging)
        {
            _isDragging = false;
            _lever?.EndDrag();
        }
        _lever?.EndHandGrab();

        // Snap the player camera back to rest; the Cinemachine blend out of the view camera hides the jump.
        if (_playerCam != null && Player != null)
        {
            _playerCam.DOKill();
            Player.playerMovementController.ResetCameraPos(true);
            _playerCam.localRotation = _playerCamRestLocalRot;
        }
        _playerCam = null;

        _lever?.Highlight(false);
        _occupancy?.Release();
    }

    protected override void OnUpdate()
    {
        if (_lever == null) return;

        // Lever was disabled (e.g. by a scripted sequence) while the view was open.
        if (!_lever.IsUsable)
        {
            Close();
            return;
        }

        SyncPlayerCamToView();

        if (HandleGamepadAxis()) return;

        Camera cam = RaycastCamera;
        if (cam == null) return;

        bool overLever = IsPointerOverLever(cam);
        _lever.Highlight(_isDragging || overLever);

        if (!_isDragging)
        {
            if (GrabDown && overLever)
            {
                _isDragging = true;
                _lever.BeginDrag();
                _lever.BeginHandGrab(Player);
            }
            return;
        }

        // Released: commit to the nearest end and let go, but stay in the view.
        if (!GrabHeld)
        {
            _isDragging = false;
            _lever.EndDrag();
            _lever.EndHandGrab();
            return;
        }

        // Mouse Y is already a per-frame delta; the stick is continuous, so the lever scales it by deltaTime.
        float mouseY = Input.GetAxis("Mouse Y");
        float stickY = Gamepad.current?.rightStick.ReadValue().y ?? 0f;
        if (Mathf.Abs(mouseY) > 0.001f) _lever.DragByMouse(mouseY);
        else                            _lever.DragByStick(stickY);
    }

    /// <summary>
    /// Gamepad vertical axis (strongest of left stick, D-pad, right stick). Positive = up.
    /// </summary>
    private static float ReadGamepadVertical()
    {
        Gamepad gp = Gamepad.current;
        if (gp == null) return 0f;

        float y = gp.leftStick.ReadValue().y;
        float dpad = gp.dpad.ReadValue().y;
        float right = gp.rightStick.ReadValue().y;
        if (Mathf.Abs(dpad) > Mathf.Abs(y)) y = dpad;
        if (Mathf.Abs(right) > Mathf.Abs(y)) y = right;
        return y;
    }

    /// <summary>
    /// Pushing up/down on the gamepad grabs the lever and moves it (no cursor aim needed). Letting go
    /// commits the lever to the end it was last pushed toward, so a quick tap flips it. Returns true
    /// while the gamepad owns the lever this frame (the mouse/RT path is skipped).
    /// </summary>
    private bool HandleGamepadAxis()
    {
        // A mouse / RT drag in progress keeps ownership until it's released.
        if (_isDragging && !_gamepadDrag) return false;

        float y = ReadGamepadVertical();
        bool pushed = Mathf.Abs(y) >= GamepadAxisDeadzone;

        if (!_gamepadDrag)
        {
            if (!pushed) return false;

            _gamepadDrag = true;
            _isDragging = true;
            _lever.BeginDrag();
            _lever.BeginHandGrab(Player);
        }

        _lever.Highlight(true);

        if (pushed)
        {
            _gamepadLastPushUp = y > 0f;
            _lever.DragByStick(y);
            return true;
        }

        // Released: commit toward the last pushed direction, but stay in the view.
        _gamepadDrag = false;
        _isDragging = false;
        _lever.EndDragToward(_gamepadLastPushUp);
        _lever.EndHandGrab();
        return true;
    }

    /// <summary>Keeps the player camera (and its arms) aligned with the view camera as it pans.</summary>
    private void SyncPlayerCamToView()
    {
        if (_playerCam == null || ViewCamera == null || DOTween.IsTweening(_playerCam)) return;
        Transform view = ViewCamera.transform;
        _playerCam.SetPositionAndRotation(view.position, view.rotation);
    }

    private bool IsPointerOverLever(Camera cam)
    {
        if (_leverCollider == null || !_leverCollider.enabled) return false;
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        return _leverCollider.Raycast(ray, out _, 100f);
    }
}
