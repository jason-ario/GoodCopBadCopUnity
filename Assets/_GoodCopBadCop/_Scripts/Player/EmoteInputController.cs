using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles the emote wheel flow for the local player.
///
/// Opening and closing the wheel is 100% driven by holding T (or D-pad Up on a gamepad):
/// the wheel is shown for as long as the key is held and hides the instant it's released,
/// regardless of whether an emote was clicked. Clicking an emote plays it without closing
/// the wheel, so the player can keep the wheel open and fire off multiple emotes in a row.
///
/// When an emote is selected:
///  1. Upper-body layer (layer 3) is ramped to weight 1 so the emote
///     animation overrides the body. If it was already 1 before the
///     emote started it is left at 1 when done.
///  2. The animator bool for the selected emote is set for
///     <see cref="EmoteDefinition.Duration"/> seconds, then cleared.
///  3. Layer 3 is restored to 0 (or left at 1 if it was already there).
///
/// Movement and look rotation are never locked.
/// </summary>
public class EmoteInputController : MonoBehaviour
{
    private PlayerAnimationController   _animController;
    private PlayerInstance              _playerInstance;

    private bool      _wheelOpen       = false;
    private bool      _isEmoting       = false;
    private Coroutine _emoteCoroutine;

    // ─── Unity lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        _animController  = GetComponent<PlayerAnimationController>();
        _playerInstance  = GetComponent<PlayerInstance>();
    }

    private void Start()
    {
        if (EmoteWheelUI.Instance != null)
            EmoteWheelUI.Instance.OnEmoteSelected += HandleEmoteSelected;
        else
            Debug.LogWarning("[EmoteInputController] EmoteWheelUI.Instance not found.", this);
    }

    private void OnDestroy()
    {
        if (EmoteWheelUI.Instance != null)
            EmoteWheelUI.Instance.OnEmoteSelected -= HandleEmoteSelected;

        if (_wheelOpen) CloseWheel();
        if (_emoteCoroutine != null) StopCoroutine(_emoteCoroutine);
    }

    private void Update()
    {
        if (PlayerInstance.Instance != _playerInstance) return;
        if (UIController.Instance != null && UIController.Instance.IsPaused) return;

        // Opening requires a fresh press edge, so re-clicking an emote while the open input is
        // still physically held won't immediately reopen a wheel that was just closed.
        bool openPressed = Input.GetKeyDown(KeyCode.T) || (Gamepad.current?.dpad.up.wasPressedThisFrame ?? false);

        // Closing is level-based (checked every frame against the *current* held state) rather
        // than edge-based. Open/close is 100% driven by whether the key is currently held — no
        // other action (clicking an emote, moving the mouse, etc.) ever closes the wheel. This is
        // also self-correcting: if a release edge is ever missed or a device (e.g. a
        // virtual/phantom gamepad) misbehaves, the wheel can't get permanently stuck open just
        // because a "key up"/"released" event never fired.
        bool openHeld = Input.GetKey(KeyCode.T) || (Gamepad.current?.dpad.up.isPressed ?? false);

        if (!_isEmoting && openPressed)
            OpenWheel();

        if (_wheelOpen && !openHeld)
            CloseWheel();
    }

    // ─── Wheel open / close ─────────────────────────────────────────────────

    private void OpenWheel()
    {
        if (_wheelOpen || _isEmoting) return;
        _wheelOpen = true;

        UIController.Instance?.ShowCursor();
        EmoteWheelUI.Instance?.Show();
    }

    private void CloseWheel()
    {
        if (!_wheelOpen) return;
        _wheelOpen = false;

        EmoteWheelUI.Instance?.Hide();
        UIController.Instance?.HideCursor();
    }

    // ─── Selection & emote sequence ─────────────────────────────────────────

    private void HandleEmoteSelected(int index)
    {
        if (EmoteWheelUI.Instance == null) return;
        EmoteDefinition[] emotes = EmoteWheelUI.Instance.Emotes;
        if (index < 0 || index >= emotes.Length) return;

        // Selecting an emote does not close the wheel — closing is 100% driven by releasing
        // the open key/button, so the player can fire off several emotes in a row while holding it.
        if (_emoteCoroutine != null)
            StopCoroutine(_emoteCoroutine);

        _emoteCoroutine = StartCoroutine(PlayEmoteSequence(emotes[index]));
    }

    private IEnumerator PlayEmoteSequence(EmoteDefinition emote)
    {
        _isEmoting = true;

        // Remember whether layer 3 was already active before this emote.
        bool layer3WasActive = _animController.GetLayer3TargetWeight() >= 0.99f;

        _animController.SetLayer3Weight(1f);
        _animController.SetAnimBool(emote.AnimBoolName, true);

        yield return new WaitForSeconds(emote.Duration);

        _animController.SetAnimBool(emote.AnimBoolName, false);

        // Only lower layer 3 back to 0 if it wasn't already active before.
        if (!layer3WasActive)
            _animController.SetLayer3Weight(0f);

        yield return new WaitForSeconds(0.15f);

        _isEmoting = false;
        _emoteCoroutine = null;
    }
}
