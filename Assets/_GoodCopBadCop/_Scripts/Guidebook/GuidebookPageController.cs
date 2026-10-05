using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Physical page-stack mechanic for the Guidebook. Content-agnostic: <see cref="GuidebookBuilder"/>
/// supplies the ordered list of <see cref="GuidebookSheet"/>s via <see cref="SetSheets"/>.
///
/// Sheets begin on the right (unread) stack. "Next" flips the top right sheet 180° about Z onto
/// the left (read) stack; "Previous" flips it back. The visible face swaps at the midpoint of the
/// flip, when the sheet is edge-on to the viewer.
///
/// Page-turn presses are queued: every press turns one more page, and the next flip starts a
/// moment after the previous one (<see cref="_queuedTurnInterval"/>) instead of waiting for it to
/// land, so several sheets can be in the air at once. <see cref="LeftCount"/> is the logical
/// count and already includes sheets that are still flipping onto the left stack.
///
/// Only the two faces of the open spread (plus whatever a flip is revealing) keep their canvases
/// active — buried sheets render paper only.
///
/// Input is ignored unless <see cref="InputEnabled"/> is true. <see cref="GuidebookController"/>
/// enables it only on the local player's first-person guidebook, so the body-rig and cutscene
/// copies never react to the local player's keys.
/// </summary>
public class GuidebookPageController : MonoBehaviour
{
    private const float StickThreshold = 0.5f;

    [Header("Stack Origins — local space")]
    [SerializeField] private Vector3 _rightOrigin = new Vector3( 0.10f, 0f, 0f);
    [SerializeField] private Vector3 _leftOrigin  = new Vector3(-0.10f, 0f, 0f);

    [Tooltip("Y offset added per sheet in a stack, simulating physical thickness.")]
    [SerializeField] private float _pageThickness = 0.002f;

    [Tooltip("Z offset added per sheet in a stack, preventing depth-plane overlap.")]
    [SerializeField] private float _pageDepth = 0.001f;

    [Header("Animation")]
    [SerializeField] private float          _turnDuration     = 0.35f;
    [Tooltip("Per-sheet flip duration used when a tab jump flips across multiple sheets.")]
    [SerializeField] private float          _snapFlipDuration = 0.08f;
    [Tooltip("Delay between the starts of queued page turns when the player presses repeatedly. " +
             "Never shorter than the face-swap margin of a flip, so sheets always land in order.")]
    [SerializeField] private float          _queuedTurnInterval = 0.09f;
    [SerializeField] private float          _arcHeight        = 0.02f;
    [SerializeField] private AnimationCurve _turnCurve        = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Fraction of a flip (0-0.5) to wait before revealing the face underneath the lifting sheet, " +
             "and before landing to hide the face it covers. Prevents content clipping through the paper.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float          _faceSwapMargin   = 0.15f;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip   _pageFlipClip;

    /// <summary>Fired when a flip completes. Argument is the new left-stack count.</summary>
    public event Action<int> OnPageChanged;

    /// <summary>When false, keyboard/gamepad page-turn input is ignored.</summary>
    public bool InputEnabled { get; set; }

    public int  LeftCount       => _leftCount;
    public int  SheetCount      => _sheets.Count;
    public bool HasNextPage     => _leftCount < _sheets.Count;
    public bool HasPreviousPage => _leftCount > 0;
    public bool IsBusy          => _turnsInFlight > 0 || _isSnapping;

    /// <summary>
    /// The left-stack count the book is heading to once queued presses and tab jumps finish.
    /// This is what gets networked, so observers aim for the final page instead of chasing each step.
    /// </summary>
    public int TargetLeftCount => _isSnapping ? _snapTarget : _leftCount + _pendingTurns;

    [Tooltip("Remote page changes larger than this many sheets play as a fast tab jump instead of normal turns.")]
    [SerializeField] private int _maxMirroredTurns = 3;

    private readonly List<GuidebookSheet> _sheets = new List<GuidebookSheet>();

    private int       _leftCount;
    private int       _turnsInFlight;
    private int       _flightDirection;   // +1 next, -1 previous, 0 idle
    private int       _pendingTurns;      // queued presses: >0 next, <0 previous
    private float     _lastTurnStartTime = float.NegativeInfinity;
    private bool      _isSnapping;
    private int       _snapTarget;
    private float     _prevStickX;
    private Coroutine _snapSequence;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnDisable()
    {
        _pendingTurns = 0;

        // A flip interrupted by closing the book would leave a sheet mid-air; settle it.
        if (IsBusy)
        {
            StopAllCoroutines();
            ResetFlightState();
            SnapTo(_leftCount);
        }
    }

    private void Update()
    {
        Gamepad gp = Gamepad.current;
        float stickX = gp != null ? gp.leftStick.x.ReadValue() : 0f;
        bool stickNext = stickX >  StickThreshold && _prevStickX <=  StickThreshold;
        bool stickPrev = stickX < -StickThreshold && _prevStickX >= -StickThreshold;
        _prevStickX = stickX;

        if (_isSnapping) return;

        if (InputEnabled)
        {
            if (stickNext || NextPressed(gp))      QueueTurn(+1);
            else if (stickPrev || PrevPressed(gp)) QueueTurn(-1);
        }

        // Also runs without input so mirrored copies (see TurnTo) play their queued turns.
        PumpQueuedTurns();
    }

    // ── Input (Input System device polling, matching project convention) ─────

    private static bool NextPressed(Gamepad gp)
    {
        Keyboard kb = Keyboard.current;
        return (kb != null && (kb.eKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame
                               || kb.rightArrowKey.wasPressedThisFrame))
            || (gp != null && (gp.rightShoulder.wasPressedThisFrame || gp.dpad.right.wasPressedThisFrame));
    }

    private static bool PrevPressed(Gamepad gp)
    {
        Keyboard kb = Keyboard.current;
        return (kb != null && (kb.qKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame
                               || kb.leftArrowKey.wasPressedThisFrame))
            || (gp != null && (gp.leftShoulder.wasPressedThisFrame || gp.dpad.left.wasPressedThisFrame));
    }

    // ── Queued turns ──────────────────────────────────────────────────────────

    /// <summary>
    /// Adds one page turn to the queue. Presses never ask for more pages than remain in that
    /// direction, and a press in the opposite direction first cancels a queued (not yet started) turn.
    /// </summary>
    private void QueueTurn(int direction)
    {
        int target = Mathf.Clamp(_leftCount + _pendingTurns + direction, 0, _sheets.Count);
        _pendingTurns = target - _leftCount;
    }

    private void PumpQueuedTurns()
    {
        if (_pendingTurns == 0) return;

        int direction = _pendingTurns > 0 ? 1 : -1;

        // Reversing mid-flight would grab a sheet that is still in the air: wait for it to land.
        if (_turnsInFlight > 0 && direction != _flightDirection) return;

        float interval = Mathf.Max(_queuedTurnInterval, _faceSwapMargin * _turnDuration + 0.01f);
        if (_turnsInFlight > 0 && Time.time - _lastTurnStartTime < interval) return;

        bool started = direction > 0 ? TurnNext(_turnDuration) : TurnPrevious(_turnDuration);
        if (started) _pendingTurns -= direction;
        else         _pendingTurns = 0;
    }

    private void ResetFlightState()
    {
        _snapSequence = null;
        _isSnapping = false;
        _turnsInFlight = 0;
        _flightDirection = 0;
        _pendingTurns = 0;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the sheet list (reading order) and instantly lays the sheets out with
    /// <paramref name="leftCount"/> sheets already flipped.
    /// </summary>
    public void SetSheets(IReadOnlyList<GuidebookSheet> sheets, int leftCount)
    {
        StopAllCoroutines();
        ResetFlightState();

        _sheets.Clear();
        if (sheets != null)
            foreach (GuidebookSheet s in sheets)
                if (s != null) _sheets.Add(s);

        SnapTo(leftCount);
    }

    /// <summary>
    /// Flips the top right-stack sheet onto the left stack. May start while earlier forward flips
    /// are still in the air. Returns false if there is no page or a backward flip is in flight.
    /// </summary>
    public bool TurnNext(float duration)
    {
        if (!HasNextPage || (_turnsInFlight > 0 && _flightDirection < 0)) return false;

        int idx        = _leftCount;
        int rightCount = _sheets.Count - _leftCount;
        Vector3 from   = RightPos(rightCount - 1);
        Vector3 to     = LeftPos(_leftCount);

        // Reveal the right sheet underneath once this one has lifted clear; hide the left page
        // it lands on just before touchdown.
        GuidebookSheet under   = idx + 1 < _sheets.Count ? _sheets[idx + 1] : null;
        GuidebookSheet covered = idx - 1 >= 0 ? _sheets[idx - 1] : null;

        _leftCount++;
        BeginFlight(+1);
        PlayFlipSound();
        StartCoroutine(AnimateTurn(
            _sheets[idx], from, to,
            0f, -180f,
            duration,
            showBackAtMidpoint: true,
            onLifted:  under   != null ? () => under.SetFace(showBack: false) : null,
            onLanding: covered != null ? () => covered.SetFace(showBack: true, visible: false) : null));
        return true;
    }

    /// <summary>
    /// Flips the top left-stack sheet back onto the right stack. May start while earlier backward
    /// flips are still in the air. Returns false if there is no page or a forward flip is in flight.
    /// </summary>
    public bool TurnPrevious(float duration)
    {
        if (!HasPreviousPage || (_turnsInFlight > 0 && _flightDirection > 0)) return false;

        int idx        = _leftCount - 1;
        int rightCount = _sheets.Count - _leftCount;
        Vector3 from   = LeftPos(_leftCount - 1);
        Vector3 to     = RightPos(rightCount);

        GuidebookSheet under   = idx - 1 >= 0 ? _sheets[idx - 1] : null;
        GuidebookSheet covered = idx + 1 < _sheets.Count ? _sheets[idx + 1] : null;

        _leftCount--;
        BeginFlight(-1);
        PlayFlipSound();
        StartCoroutine(AnimateTurn(
            _sheets[idx], from, to,
            180f, 360f,
            duration,
            showBackAtMidpoint: false,
            onLifted:  under   != null ? () => under.SetFace(showBack: true) : null,
            onLanding: covered != null ? () => covered.SetFace(showBack: false, visible: false) : null));
        return true;
    }

    /// <summary>Instantly lays every sheet out for the given left-stack count. No animation.</summary>
    public void SnapTo(int leftCount)
    {
        _leftCount = Mathf.Clamp(leftCount, 0, _sheets.Count);

        int n = _sheets.Count;
        for (int i = 0; i < n; i++)
        {
            Transform t = _sheets[i].transform;
            if (i < _leftCount)
            {
                t.localPosition = LeftPos(i);
                t.localRotation = Quaternion.Euler(0f, 0f, 180f);
            }
            else
            {
                t.localPosition = RightPos(n - 1 - i);
                t.localRotation = Quaternion.identity;
            }
        }

        RefreshFaces();
    }

    /// <summary>
    /// Animates sheet flips, one after another, until <paramref name="targetLeftCount"/> sheets
    /// are on the left stack. Used by section tabs. Cancels any in-progress jump and queued presses.
    /// </summary>
    public void FlipTo(int targetLeftCount)
    {
        targetLeftCount = Mathf.Clamp(targetLeftCount, 0, _sheets.Count);
        _pendingTurns = 0;
        if (targetLeftCount == _leftCount && !_isSnapping) return;

        _snapTarget = targetLeftCount;
        if (_snapSequence != null) StopCoroutine(_snapSequence);
        _snapSequence = StartCoroutine(AnimatedFlipTo(targetLeftCount));
    }

    /// <summary>
    /// Mirrors a page position received from another player: small changes play as normal queued
    /// turns, large ones (tab jumps) as a fast <see cref="FlipTo"/>. Snaps instantly while inactive.
    /// </summary>
    public void TurnTo(int targetLeftCount)
    {
        targetLeftCount = Mathf.Clamp(targetLeftCount, 0, _sheets.Count);

        if (!isActiveAndEnabled)
        {
            SnapTo(targetLeftCount);
            return;
        }

        if (targetLeftCount == TargetLeftCount) return;

        if (_isSnapping || Mathf.Abs(targetLeftCount - _leftCount) > _maxMirroredTurns)
            FlipTo(targetLeftCount);
        else
            _pendingTurns = targetLeftCount - _leftCount;
    }

    private IEnumerator AnimatedFlipTo(int target)
    {
        _isSnapping = true;

        while (_turnsInFlight > 0) yield return null;

        while (_leftCount != target)
        {
            if (_leftCount < target) TurnNext(_snapFlipDuration);
            else                     TurnPrevious(_snapFlipDuration);

            yield return null;
            while (_turnsInFlight > 0) yield return null;
        }

        _isSnapping   = false;
        _snapSequence = null;
    }

    // ── Faces ─────────────────────────────────────────────────────────────────

    /// <summary>Only the open spread renders content: left top shows its back, right top its front.</summary>
    private void RefreshFaces()
    {
        for (int i = 0; i < _sheets.Count; i++)
        {
            bool leftTop  = i == _leftCount - 1;
            bool rightTop = i == _leftCount;
            _sheets[i].SetFace(showBack: leftTop, visible: leftTop || rightTop);
        }
    }

    // ── Position helpers ──────────────────────────────────────────────────────

    private Vector3 RightPos(int stackIndex) =>
        _rightOrigin + new Vector3(0f, stackIndex * _pageThickness, stackIndex * _pageDepth);

    private Vector3 LeftPos(int stackIndex) =>
        _leftOrigin + new Vector3(0f, stackIndex * _pageThickness, stackIndex * _pageDepth);

    // ── Animation ─────────────────────────────────────────────────────────────

    private void BeginFlight(int direction)
    {
        _turnsInFlight++;
        _flightDirection = direction;
        _lastTurnStartTime = Time.time;
    }

    private void PlayFlipSound()
    {
        if (_audioSource != null && _pageFlipClip != null)
            _audioSource.PlayOneShot(_pageFlipClip);
    }

    private IEnumerator AnimateTurn(
        GuidebookSheet sheet,
        Vector3 fromPos, Vector3 toPos,
        float fromZ, float toZ,
        float duration,
        bool showBackAtMidpoint,
        Action onLifted,
        Action onLanding)
    {
        Transform page = sheet.transform;
        bool faceSwitched = false;
        bool lifted       = false;
        bool landing      = false;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float t = _turnCurve.Evaluate(progress);

            if (!lifted && progress >= _faceSwapMargin)
            {
                lifted = true;
                onLifted?.Invoke();
            }

            if (!faceSwitched && t >= 0.5f)
            {
                faceSwitched = true;
                sheet.SetFace(showBackAtMidpoint);
            }

            if (!landing && progress >= 1f - _faceSwapMargin)
            {
                landing = true;
                onLanding?.Invoke();
            }

            float arcY = Mathf.Sin(t * Mathf.PI) * _arcHeight;
            page.localPosition = Vector3.Lerp(fromPos, toPos, t) + new Vector3(0f, arcY, 0f);
            page.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(fromZ, toZ, t));

            yield return null;
        }

        page.localPosition = toPos;
        page.localRotation = Quaternion.Euler(0f, 0f, Mathf.Repeat(toZ, 360f));

        // Faces settle once the last sheet in the air has landed; until then the lift/landing
        // callbacks of each flight keep the right faces visible.
        _turnsInFlight = Mathf.Max(0, _turnsInFlight - 1);
        if (_turnsInFlight == 0)
        {
            _flightDirection = 0;
            RefreshFaces();
        }
        OnPageChanged?.Invoke(_leftCount);
    }
}
