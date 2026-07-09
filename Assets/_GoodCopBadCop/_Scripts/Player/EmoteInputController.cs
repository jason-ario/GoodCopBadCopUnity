using System.Collections;
using UnityEngine;

/// <summary>
/// Handles the emote wheel flow for the local player.
///
/// Pressing T opens the emote wheel (cursor shown, look unlocked).
/// Moving the mouse highlights the nearest slot. Releasing T without
/// clicking closes the wheel with no selection.
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

        if (_wheelOpen) CloseWheel(selectEmote: false);
        if (_emoteCoroutine != null) StopCoroutine(_emoteCoroutine);
    }

    private void Update()
    {
        if (PlayerInstance.Instance != _playerInstance) return;
        if (_isEmoting) return;
        if (UIController.Instance != null && UIController.Instance.IsPaused) return;

        if (Input.GetKeyDown(KeyCode.T))
            OpenWheel();

        if (Input.GetKeyUp(KeyCode.T) && _wheelOpen)
            CloseWheel(selectEmote: false);
    }

    // ─── Wheel open / close ─────────────────────────────────────────────────

    private void OpenWheel()
    {
        if (_wheelOpen || _isEmoting) return;
        _wheelOpen = true;

        UIController.Instance?.ShowCursor();
        EmoteWheelUI.Instance?.Show();
    }

    private void CloseWheel(bool selectEmote)
    {
        if (!_wheelOpen) return;
        _wheelOpen = false;

        EmoteWheelUI.Instance?.Hide();

        if (!selectEmote)
            UIController.Instance?.HideCursor();
    }

    // ─── Selection & emote sequence ─────────────────────────────────────────

    private void HandleEmoteSelected(int index)
    {
        if (EmoteWheelUI.Instance == null) return;
        EmoteDefinition[] emotes = EmoteWheelUI.Instance.Emotes;
        if (index < 0 || index >= emotes.Length) return;

        CloseWheel(selectEmote: true);
        UIController.Instance?.HideCursor();

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
