using DG.Tweening;
using GoodCopBadCop.Input;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Diegetic view for the shutter <see cref="Lever"/>. Opened by pressing Interact on the lever.
/// Inside the view, clicking on the lever (LMB / RT) and dragging the mouse up/down (or right stick)
/// moves it; releasing commits it to the nearest end. Releasing does NOT exit the view — the player
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

    [Header("First-Person Arms")]
    [Tooltip("Seconds the player camera (which carries the first-person arms) takes to move into the view, " +
             "so the hand-grab IK can reach the lever. Match roughly to the Cinemachine blend time.")]
    [SerializeField] private float _armsCameraMoveDuration = 0.5f;

    private bool _isDragging;
    private Transform _playerCam;
    private Quaternion _playerCamRestLocalRot;

    protected override bool SuppressCameraMovement => _isDragging;

    protected override string ActionPromptText => _dragPromptText;

    // Arms stay visible so the hand can be seen gripping the lever.
    protected override bool HidePlayerArms => false;

    private bool GrabDown => Input.GetMouseButtonDown(0) || RebindableInput.GetGamepadDown(GameAction.UseItem);
    private bool GrabHeld => Input.GetMouseButton(0)     || RebindableInput.GetGamepadHeld(GameAction.UseItem);

    protected override void OnOpened()
    {
        _isDragging = false;

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
