using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Diegetic view controller for a bunker door wheel knob (one instance per side of the door).
/// The view is opened externally (see <see cref="BunkerDoorInteractable"/>) by clicking
/// anywhere on the door. Once open, clicking specifically on the wheel and dragging spins
/// it — rotating the mouse around the wheel's screen-centre while held. Releasing the mouse
/// button stops the spin but does NOT exit the view; the view only closes when the wheel is
/// fully spun (opening the door) or the player presses the exit key.
/// A configurable clockwise limit prevents the wheel from spinning past the "fully tight" stop.
/// </summary>
public class DoorWheelDiegeticController : DiegeticViewController
{
    [Header("Wheel")]
    [Tooltip("The Transform that visually spins (the wheel mesh root).")]
    [SerializeField] private Transform _wheelTransform;

    [Tooltip("The wheel's own collider — clicking and dragging on this (while the view is open) spins the wheel.")]
    [SerializeField] private Collider _wheelCollider;

    [Tooltip("The BunkerDoorController to call Open() on when the door is unlocked.")]
    [SerializeField] private BunkerDoorController _bunkerDoor;

    [Tooltip("Replicates this wheel's spin across the network so other players see it turn. " +
             "Optional — if unassigned, rotation stays purely local to this client.")]
    [SerializeField] private DoorWheelNetworkSync _networkSync;

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

    [Tooltip("Multiplier applied only to the wheel's visual spin rate (not the unlock progress or clamp logic). "
           + "1 = matches input 1:1; lower values (e.g. 0.5) make the wheel feel heavier/more tactile.")]
    [SerializeField] private float _visualRotationSpeedMultiplier = 0.5f;

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

    [Tooltip("Delay (seconds) after the wheel finishes spinning before the view closes and " +
             "player look control is restored. Lets the player's mouse motion settle so the " +
             "camera doesn't jolt from residual spin input when control hands back.")]
    [SerializeField] private float _closeDelayAfterUnlock = 1f;

    // ─── Runtime state ────────────────────────────────────────────────────────

    /// <summary>True while the player is holding LMB and dragging the wheel.</summary>
    private bool _isDragging;

    /// <summary>True once the wheel has fully unlocked and the view is waiting to close.</summary>
    private bool _closing;

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

    /// <summary>Suppress the camera pan while the player is actively dragging the wheel,
    /// and while waiting for the post-unlock close delay to elapse.</summary>
    protected override bool SuppressCameraMovement => _isDragging || _closing;

    private bool LmbDown => Input.GetMouseButtonDown(0) || (Gamepad.current?.rightTrigger.wasPressedThisFrame  ?? false);
    private bool LmbHeld => Input.GetMouseButton(0)     || (Gamepad.current?.rightTrigger.isPressed             ?? false);
    private bool LmbUp   => Input.GetMouseButtonUp(0)   || (Gamepad.current?.rightTrigger.wasReleasedThisFrame  ?? false);

    // ─── MonoBehaviour ────────────────────────────────────────────────────────

    /// <summary>
    /// The wheel's own collider sits directly in front of the door's interaction collider,
    /// so it must stay disabled outside this view — otherwise it blocks raycasts/clicks
    /// meant for <see cref="BunkerDoorInteractable"/>. It's only re-enabled while this
    /// diegetic view is open (see <see cref="OnOpened"/>/<see cref="OnClosed"/>), when it's
    /// needed for <see cref="IsPointerOverWheel"/>.
    /// </summary>
    private void Awake()
    {
        if (_wheelCollider != null)
            _wheelCollider.enabled = false;
    }

    protected override void OnOpened()
    {
        _accumulatedRotation = 0f;
        _totalCCWDegrees     = 0f;
        _isDragging          = false;
        _closing             = false;

        if (_wheelCollider != null)
            _wheelCollider.enabled = true;
    }

    protected override void OnClosed()
    {
        SetDragging(false);
        _occupancy?.Release();
        _closing = false;
        StopAllCoroutines();

        if (_wheelCollider != null)
            _wheelCollider.enabled = false;
    }

    protected override void OnUpdate()
    {
        // Waiting out the post-unlock delay: ignore all further input until we close.
        if (_closing) return;

        // If the door was opened by another means while this view is active, exit cleanly.
        if (_bunkerDoor != null && _bunkerDoor.IsOpen)
        {
            Close();
            return;
        }

        Camera cam = RaycastCamera;
        if (cam == null || _wheelTransform == null) return;

        // Not yet dragging: only a click that actually lands on the wheel starts a drag.
        // Letting go of the mouse elsewhere, or missing the wheel, has no effect — the
        // view itself stays open until the wheel is fully spun or the exit key is pressed.
        if (!_isDragging)
        {
            if (LmbDown && IsPointerOverWheel(cam))
            {
                SetDragging(true);
                _lastMouseAngle = GetMouseAngleAroundWheel(cam);
            }
            else
            {
                UpdateSpinAudio(0f);
            }
            return;
        }

        // Dragging and released: stop spinning, but do NOT close the view.
        if (!LmbHeld && !LmbUp)
        {
            // In case the button was released before the first OnUpdate (edge case).
            SetDragging(false);
            return;
        }

        if (LmbUp)
        {
            SetDragging(false);
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
        euler.z -= delta * _visualRotationSpeedMultiplier;
        _wheelTransform.localEulerAngles = euler;

        // Broadcast the new angle so other clients see the wheel spin too.
        _networkSync?.PublishWheelZRotation(euler.z);

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

        if (_networkSync != null)
            _networkSync.IsLocalAuthority = dragging;

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
    /// Returns true if the mouse cursor is currently over this wheel's collider.
    /// Used to gate starting a drag so clicks elsewhere in the view don't spin the wheel.
    /// </summary>
    private bool IsPointerOverWheel(Camera cam)
    {
        if (_wheelCollider == null) return false;
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        return _wheelCollider.Raycast(ray, out _, 100f);
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
        if (_closing) return;

        _openImpulseSource?.GenerateImpulse();

        // Stop responding to further drag input immediately, but keep the view open
        // (and camera pan frozen) for a moment so the player's mouse motion from
        // spinning the wheel settles before look control hands back — avoids a
        // camera jolt from residual spin input on the frame control is restored.
        SetDragging(false);
        _closing = true;
        _bunkerDoor?.Open();
        StartCoroutine(CloseAfterDelay());
    }

    private IEnumerator CloseAfterDelay()
    {
        yield return new WaitForSeconds(_closeDelayAfterUnlock);
        Close();
    }
}
