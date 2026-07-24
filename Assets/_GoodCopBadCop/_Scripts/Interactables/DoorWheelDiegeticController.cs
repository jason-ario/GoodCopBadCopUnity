using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Diegetic view controller for the bunker door wheel knob.
/// The player opens this view by clicking the wheel. While the view is active and the
/// mouse button is held, rotating the mouse around the wheel's screen-centre spins the
/// wheel visually. Enough counter-clockwise rotation unlocks and opens the door. A
/// configurable clockwise limit prevents the wheel from spinning past the "fully tight"
/// stop. Releasing the mouse button exits the view.
/// </summary>
public class DoorWheelDiegeticController : DiegeticViewController
{
    [Header("Wheel")]
    [Tooltip("The Transform that visually spins (the wheel mesh root).")]
    [SerializeField] private Transform _wheelTransform;

    [Tooltip("The BunkerDoorController to call Open() on when the door is unlocked.")]
    [SerializeField] private BunkerDoorController _bunkerDoor;

    [Header("Rotation Limits")]
    [Tooltip("Number of full CCW rotations required to unlock the door.")]
    [SerializeField] private float _rotationsToUnlock = 3f;

    [Tooltip("Maximum clockwise degrees from the starting angle (the 'fully tight' stop).")]
    [SerializeField] private float _maxClockwiseDegrees = 90f;

    [Header("Feel")]
    [Tooltip("Sensitivity multiplier applied to the raw mouse-angle delta. "
           + "Increase for a faster-responding wheel; decrease for a heavier feel.")]
    [SerializeField] private float _sensitivity = 1f;

    [Tooltip("Degrees per second the wheel spins at full right-stick deflection (controller only). "
           + "Stick left = CCW (unlocks door); stick right = CW.")]
    [SerializeField] private float _controllerRotateSpeed = 120f;

    [Tooltip("Tracks occupancy of this door so it can be released when the view closes.")]
    [SerializeField] private DiegeticOccupancy _occupancy;

    [Header("Audio")]
    [Tooltip("Audio source that plays the looping spin sound while the wheel is being turned.")]
    [SerializeField] private AudioSource _spinAudioSource;

    [Tooltip("Looping sound played for as long as the player is dragging the wheel.")]
    [SerializeField] private AudioClip _spinLoopSound;

    [Tooltip("Minimum angular velocity (degrees/second) the wheel must be spinning at for the loop sound to play.")]
    [SerializeField] private float _spinAudioVelocityThreshold = 0.1f;

    [Tooltip("Cinemachine impulse fired when the wheel is fully spun and the door unlocks.")]
    [SerializeField] private CinemachineImpulseSource _openImpulseSource;

    // ─── Runtime state ────────────────────────────────────────────────────────

    /// <summary>True while the player is holding LMB and dragging the wheel.</summary>
    private bool _isDragging;

    /// <summary>Screen-space angle (degrees) of the mouse relative to the wheel centre on the previous frame.</summary>
    private float _lastMouseAngle;

    /// <summary>
    /// Total accumulated rotation from the starting angle (positive = CCW, negative = CW).
    /// Clamped on the CW side by <see cref="_maxClockwiseDegrees"/>.
    /// </summary>
    private float _accumulatedRotation;

    /// <summary>Monotonically increasing sum of all CCW rotation this session. Used for unlock detection.</summary>
    private float _totalCCWDegrees;

    // ─── DiegeticViewController overrides ────────────────────────────────────

    /// <summary>No back button — the view exits on mouse release.</summary>
    protected override bool ShowBackButton => false;

    /// <summary>Suppress the camera pan while the player is actively dragging the wheel.</summary>
    protected override bool SuppressCameraMovement => _isDragging;

    private bool LmbDown => Input.GetMouseButtonDown(0) || (Gamepad.current?.rightTrigger.wasPressedThisFrame  ?? false);
    private bool LmbHeld => Input.GetMouseButton(0)     || (Gamepad.current?.rightTrigger.isPressed             ?? false);
    private bool LmbUp   => Input.GetMouseButtonUp(0)   || (Gamepad.current?.rightTrigger.wasReleasedThisFrame  ?? false);

    protected override void OnOpened()
    {
        _accumulatedRotation = 0f;
        _totalCCWDegrees     = 0f;

        // The view was opened via a mouse-down, so we begin dragging immediately.
        SetDragging(true);
        Camera cam = RaycastCamera;
        if (cam != null && _wheelTransform != null)
            _lastMouseAngle = GetMouseAngleAroundWheel(cam);
    }

    protected override void OnClosed()
    {
        SetDragging(false);
        _occupancy?.Release();
    }

    protected override void OnUpdate()
    {
        // If the door was opened by another means while this view is active, exit cleanly.
        if (_bunkerDoor != null && _bunkerDoor.IsOpen)
        {
            Close();
            return;
        }

        Camera cam = RaycastCamera;
        if (cam == null || _wheelTransform == null) return;

        // In case the button was released before the first OnUpdate (edge case).
        if (_isDragging && !LmbHeld && !LmbUp)
        {
            SetDragging(false);
            Close();
            return;
        }

        // Mouse / RT down while view is active and not yet dragging (e.g. player re-pressed).
        if (!_isDragging && LmbDown)
        {
            SetDragging(true);
            _lastMouseAngle = GetMouseAngleAroundWheel(cam);
            return;
        }

        // Release → exit the view.
        if (LmbUp)
        {
            SetDragging(false);
            Close();
            return;
        }

        if (!_isDragging)
        {
            UpdateSpinAudio(0f);
            return;
        }

        // ── Compute angular delta ─────────────────────────────────────────────

        float delta;
        Vector2 stick = Gamepad.current?.rightStick.ReadValue() ?? Vector2.zero;

        if (stick.sqrMagnitude > 0.01f)
        {
            // Controller: right stick X drives CW/CCW.
            // Stick left (negative X) = CCW = positive delta.
            delta = -stick.x * _controllerRotateSpeed * Time.deltaTime * _sensitivity;
            // Keep _lastMouseAngle current so there's no jump if the player switches to mouse mid-drag.
            _lastMouseAngle = GetMouseAngleAroundWheel(cam);
        }
        else
        {
            float currentMouseAngle = GetMouseAngleAroundWheel(cam);
            // DeltaAngle handles wrap-around: positive = CCW, negative = CW.
            delta = Mathf.DeltaAngle(_lastMouseAngle, currentMouseAngle) * _sensitivity;
            _lastMouseAngle = currentMouseAngle;
        }

        // ── Apply CW clamp ────────────────────────────────────────────────────

        float newAccumulation = _accumulatedRotation + delta;
        if (newAccumulation < -_maxClockwiseDegrees)
        {
            // Clamp: only allow the remaining CW travel, then stop.
            delta = -_maxClockwiseDegrees - _accumulatedRotation;
            newAccumulation = -_maxClockwiseDegrees;
        }

        _accumulatedRotation = newAccumulation;

        // ── Drive the spin loop sound from actual angular velocity ───────────

        float angularVelocity = Mathf.Abs(delta) / Mathf.Max(Time.deltaTime, 0.0001f);
        UpdateSpinAudio(angularVelocity);

        // ── Spin the wheel ────────────────────────────────────────────────────

        // Rotate on the wheel's local Z axis. Per convention: negative Z = CCW.
        // Mouse CCW (positive delta) → negative Z change = CCW visual.
        Vector3 euler = _wheelTransform.localEulerAngles;
        euler.z -= delta;
        _wheelTransform.localEulerAngles = euler;

        // ── Accumulate CCW for unlock ─────────────────────────────────────────

        if (delta > 0f)
            _totalCCWDegrees += delta;

        if (_totalCCWDegrees >= _rotationsToUnlock * 360f)
            TriggerDoorOpen();
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Sets the dragging state. Stops the spin loop sound as a safety net whenever
    /// dragging ends; playback while dragging is driven per-frame by <see cref="UpdateSpinAudio"/>
    /// based on actual angular velocity, not merely whether the wheel is held.
    /// </summary>
    private void SetDragging(bool dragging)
    {
        _isDragging = dragging;

        if (!dragging)
            UpdateSpinAudio(0f);
    }

    /// <summary>
    /// Starts or stops the looping spin sound depending on whether the wheel's current
    /// angular velocity (degrees/second) exceeds <see cref="_spinAudioVelocityThreshold"/>.
    /// </summary>
    private void UpdateSpinAudio(float angularVelocityDegPerSec)
    {
        if (_spinAudioSource == null || _spinLoopSound == null) return;

        bool shouldPlay = angularVelocityDegPerSec > _spinAudioVelocityThreshold;

        if (shouldPlay)
        {
            if (_spinAudioSource.isPlaying && _spinAudioSource.clip == _spinLoopSound) return;
            _spinAudioSource.clip = _spinLoopSound;
            _spinAudioSource.loop = true;
            _spinAudioSource.Play();
        }
        else if (_spinAudioSource.isPlaying)
        {
            _spinAudioSource.Stop();
        }
    }

    /// <summary>
    /// Returns the screen-space angle (degrees) of the mouse cursor relative to the
    /// wheel's projected screen position. Uses <c>Atan2</c> so the range is [-180, 180].
    /// </summary>
    private float GetMouseAngleAroundWheel(Camera cam)
    {
        Vector3 screenPos   = cam.WorldToScreenPoint(_wheelTransform.position);
        Vector2 fromCentre  = (Vector2)Input.mousePosition - (Vector2)screenPos;
        return Mathf.Atan2(fromCentre.y, fromCentre.x) * Mathf.Rad2Deg;
    }

    private void TriggerDoorOpen()
    {
        _openImpulseSource?.GenerateImpulse();

        // Exit the view first so the camera transition feels responsive.
        Close();
        _bunkerDoor?.Open();
    }
}
