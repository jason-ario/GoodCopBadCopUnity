using System.Collections;
using GoodCopBadCop.Input;
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
///  2. The animator trigger for the selected emote is fired (networked). The
///     emote state exits back to Default on exit time in the animator.
///  3. After <see cref="EmoteDefinition.Duration"/> seconds the trigger is reset
///     (in case it went unconsumed) and layer 3 is restored to 0 (or left at 1
///     if it was already there). Interrupting an emote resets its trigger too.
///
/// While the wheel is open, movement, look rotation, and interaction are locked
/// (mirrors the pause menu / guidebook pattern), and restored when it closes.
///
/// Opening is only allowed while the player can otherwise interact with the world
/// (<see cref="PlayerInteractionController.CanInteract"/>, and not mid-dialogue via
/// <see cref="DialogueChoiceSystem.IsInDialogueMode"/> / <see cref="ScriptedDialogueRunner.IsScriptedModeActive"/>).
/// If any of these become true while the wheel is already open, it force-closes immediately.
/// </summary>
public class EmoteInputController : MonoBehaviour
{
    private PlayerAnimationController    _animController;
    private PlayerInstance               _playerInstance;
    private PlayerMovementController     _movementController;
    private PlayerInteractionController  _interactionController;

    private bool      _wheelOpen       = false;
    private bool      _isEmoting       = false;
    private Coroutine _emoteCoroutine;
    private string    _activeTrigger;
    private bool      _layer3WasActiveBeforeEmote;

    // ─── Unity lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        _animController        = GetComponent<PlayerAnimationController>();
        _playerInstance        = GetComponent<PlayerInstance>();
        _movementController    = GetComponent<PlayerMovementController>();
        _interactionController = GetComponent<PlayerInteractionController>();
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

        // The emote wheel is only allowed while the player can otherwise interact with the
        // world. This covers dialogue (interview mode drives CanInteract false via
        // SetSuspectCamMode), scripted cutscenes, and any other system that disables
        // PlayerInteractionController via SetCanInteract(false).
        bool interactAllowed = (_interactionController == null || _interactionController.CanInteract)
                                && !DialogueChoiceSystem.IsInDialogueMode
                                && !ScriptedDialogueRunner.IsScriptedModeActive;

        // Opening requires a fresh press edge, so re-clicking an emote while the open input is
        // still physically held won't immediately reopen a wheel that was just closed.
        bool openPressed = RebindableInput.GetKeyDown(GameAction.OpenEmotes) || RebindableInput.GetGamepadDown(GameAction.OpenEmotes);

        // Closing is level-based (checked every frame against the *current* held state) rather
        // than edge-based. Open/close is 100% driven by whether the key is currently held — no
        // other action (clicking an emote, moving the mouse, etc.) ever closes the wheel. This is
        // also self-correcting: if a release edge is ever missed or a device (e.g. a
        // virtual/phantom gamepad) misbehaves, the wheel can't get permanently stuck open just
        // because a "key up"/"released" event never fired.
        bool openHeld = RebindableInput.GetKeyHeld(GameAction.OpenEmotes) || RebindableInput.GetGamepadHeld(GameAction.OpenEmotes);

        if (!_isEmoting && interactAllowed && openPressed)
            OpenWheel();

        // If interaction becomes disabled while the wheel is already open (e.g. a dialogue
        // starts mid-hold), force it closed immediately rather than waiting for key release.
        if (_wheelOpen && (!openHeld || !interactAllowed))
            CloseWheel();
    }

    // ─── Wheel open / close ─────────────────────────────────────────────────

    private void OpenWheel()
    {
        if (_wheelOpen || _isEmoting) return;

        // Emoting puts the guidebook away. Close it before the wheel takes the control lock:
        // closing afterwards would hand movement/look back while the wheel is still up, and
        // leaving it open would let CloseWheel restore control with the book still in hand.
        GuidebookController.CloseLocalGuidebook();

        _wheelOpen = true;

        UIController.Instance?.ShowCursor();

        // Lock movement, look, and interaction while the wheel is open — mirrors the
        // pause menu / guidebook pattern. SetCanControl(false) alone stops
        // PlayerMovementController's Update (movement + look), but SetCanMove/SetCanLook
        // are also set so dependent systems (reticle, footsteps, camera shake) see
        // consistent state while the wheel is up.
        if (_movementController != null)
        {
            _movementController.SetCanMove(false);
            _movementController.SetCanControl(false);
            _movementController.SetCanLook(false);
        }

        EmoteWheelUI.Instance?.Show();
    }

    private void CloseWheel()
    {
        if (!_wheelOpen) return;
        _wheelOpen = false;

        EmoteWheelUI.Instance?.Hide();

        // Restore look first so SetCanControl finds CanLook == true and re-enables the reticle.
        if (_movementController != null)
        {
            _movementController.SetCanLook(true);
            _movementController.SetCanControl(true);
            _movementController.SetCanMove(true);
        }

        UIController.Instance?.HideCursor();
    }

    // ─── Selection & emote sequence ─────────────────────────────────────────

    private void HandleEmoteSelected(int index)
    {
        if (EmoteWheelUI.Instance == null) return;
        EmoteDefinition[] emotes = EmoteWheelUI.Instance.Emotes;
        if (index < 0 || index >= emotes.Length) return;

        // Safety net for any path that plays an emote without opening the wheel first. While the
        // wheel is open it owns the control lock, so don't hand control back here.
        GuidebookController.CloseLocalGuidebook(restorePlayerControl: !_wheelOpen);

        // Selecting an emote does not close the wheel — closing is 100% driven by releasing
        // the open key/button, so the player can fire off several emotes in a row while holding it.
        bool interrupting = _emoteCoroutine != null;
        if (interrupting)
        {
            StopCoroutine(_emoteCoroutine);

            // Clear the interrupted emote's trigger so it can't sit pending and replay
            // once the upper-body layer returns to Default.
            if (!string.IsNullOrEmpty(_activeTrigger))
                _animController.ResetAnimTrigger(_activeTrigger);
        }
        else
        {
            // Only sample on a fresh emote — mid-emote the target weight is already 1.
            _layer3WasActiveBeforeEmote = _animController.GetLayer3TargetWeight() >= 0.99f;
        }

        _emoteCoroutine = StartCoroutine(PlayEmoteSequence(emotes[index]));
    }

    private IEnumerator PlayEmoteSequence(EmoteDefinition emote)
    {
        _isEmoting = true;
        _activeTrigger = emote.AnimTriggerName;

        _animController.SetLayer3Weight(1f);
        _animController.SetAnimTrigger(emote.AnimTriggerName);

        // The animator leaves the emote state on exit time; Duration just keeps layer 3
        // at full weight for the clip and blocks reopening the wheel meanwhile.
        yield return new WaitForSeconds(emote.Duration);

        // Clear the trigger in case it was never consumed (e.g. layer was mid-transition).
        _animController.ResetAnimTrigger(emote.AnimTriggerName);
        _activeTrigger = null;

        // Only lower layer 3 back to 0 if it wasn't already active before.
        if (!_layer3WasActiveBeforeEmote)
            _animController.SetLayer3Weight(0f);

        yield return new WaitForSeconds(0.15f);

        _isEmoting = false;
        _emoteCoroutine = null;
    }
}
