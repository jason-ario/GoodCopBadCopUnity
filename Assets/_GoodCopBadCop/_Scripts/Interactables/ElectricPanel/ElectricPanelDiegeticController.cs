using UnityEngine;

/// <summary>
/// Diegetic view controller for the electrical panel puzzle.
///
/// While this view is active:
///   • Raycasts each frame to detect <see cref="CircuitSwitch"/> hover and clicks.
///   • Detects a click-and-drag on the knob collider and feeds screen-space
///     angle deltas to <see cref="TurningNobController"/>.
///   • Checks every frame whether all switches are On and the knob has reached
///     its On position; if so, triggers <see cref="ElectricPanelController.RestorePower"/>
///     and exits the view.
///
/// Cursor panning is suppressed while the player is dragging the knob (same
/// pattern as <see cref="DoorWheelDiegeticController"/>).
/// </summary>
public class ElectricPanelDiegeticController : DiegeticViewController
{
    [Header("References")]
    [Tooltip("The ElectricPanelController that owns this view.")]
    [SerializeField] private ElectricPanelController _panelController;

    [Tooltip("All circuit switches on the panel — must ALL be On to solve the puzzle.")]
    [SerializeField] private CircuitSwitch[] _switches;

    [Tooltip("The turning knob controller.")]
    [SerializeField] private TurningNobController _nob;

    [Tooltip("The BoxCollider on the Turning_nob mesh. Used for knob drag detection.")]
    [SerializeField] private Collider _nobCollider;

    [Tooltip("The outer collider on the panel root. Disabled while the view is open so it doesn't block raycasts to the switches.")]
    [SerializeField] private Collider _panelCollider;

    // ─── Runtime state ────────────────────────────────────────────────────────

    private bool _isDraggingNob;
    private float _lastMouseAngle;

    // ─── DiegeticViewController overrides ────────────────────────────────────

    /// <summary>Suppress camera panning while the player is dragging the knob.</summary>
    protected override bool SuppressCameraMovement => _isDraggingNob;

    protected override void OnOpened()
    {
        if (_panelCollider != null) _panelCollider.enabled = false;
    }

    protected override void OnClosed()
    {
        // Release knob drag and spring it back if not at On.
        if (_isDraggingNob)
        {
            _isDraggingNob = false;
            _nob?.OnRelease();
        }

        if (_panelCollider != null) _panelCollider.enabled = true;

        // Notify the panel controller so it can close the door.
        _panelController?.OnViewClosed();
    }

    protected override void OnUpdate()
    {
        Camera cam = RaycastCamera;
        if (cam == null) return;

        if (_isDraggingNob)
            HandleNobDrag(cam);
        else
            HandleHoverAndClick(cam);
    }

    // ─── Hover and click ─────────────────────────────────────────────────────

    private void HandleHoverAndClick(Camera cam)
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        bool didHit = Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Collide);

        if (!Input.GetMouseButtonDown(0) || !didHit) return;

        bool hitNob = _nobCollider != null && hit.collider == _nobCollider;

        if (hitNob)
        {
            BeginNobDrag(cam);
        }
        else
        {
            hit.collider.GetComponentInParent<CircuitSwitch>()?.OnClick();
            CheckPuzzleSolved();
        }
    }

    // ─── Knob drag ────────────────────────────────────────────────────────────

    private void BeginNobDrag(Camera cam)
    {
        _isDraggingNob = true;
        _lastMouseAngle = ScreenAngleAroundNob(cam);
    }

    private void HandleNobDrag(Camera cam)
    {
        if (Input.GetMouseButtonUp(0))
        {
            _isDraggingNob = false;
            _nob?.OnRelease();
            CheckPuzzleSolved();
            return;
        }

        if (_nob == null) return;

        float current = ScreenAngleAroundNob(cam);
        float delta   = Mathf.DeltaAngle(_lastMouseAngle, current);
        _lastMouseAngle = current;

        _nob.AddDragDelta(delta);
    }

    // ─── Puzzle check ─────────────────────────────────────────────────────────

    private void CheckPuzzleSolved()
    {
        if (_nob == null || !_nob.IsAtOnPosition) return;

        if (_switches != null)
        {
            foreach (CircuitSwitch sw in _switches)
                if (sw != null && !sw.IsOn) return;
        }

        // All switches are On and the knob has reached its On position — restore power!
        _panelController?.RestorePower();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private float ScreenAngleAroundNob(Camera cam)
    {
        if (_nobCollider == null) return 0f;
        Vector2 screen = cam.WorldToScreenPoint(_nobCollider.transform.position);
        Vector2 dir    = (Vector2)Input.mousePosition - screen;
        return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
    }
}
