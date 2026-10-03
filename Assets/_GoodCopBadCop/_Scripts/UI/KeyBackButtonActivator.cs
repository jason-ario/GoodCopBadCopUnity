using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Lets a UI <see cref="Button"/> also respond to a keyboard key press
/// (Escape by default), in addition to mouse/touch clicks.
///
/// EventSystem's Submit/Cancel actions only reach the currently *selected*
/// UI element, so a plain Button does not react to a Cancel key unless it
/// is selected. This component makes the button respond directly,
/// regardless of selection, mirroring the pattern used by
/// <see cref="GamepadBackButtonActivator"/> for the gamepad East button.
///
/// The "Back button" prefab keeps its key-hint Button on the parent object
/// and its gamepad/click Button on a child object, so pressing the key also
/// invokes the paired <see cref="GamepadBackButtonActivator"/>'s Button (and
/// vice versa) — this keeps Q, gamepad B/East, and mouse clicks behaving
/// identically no matter which Button ended up wired with the real action.
///
/// Attach to any Button that represents a "Back" or "Cancel" action.
/// </summary>
[RequireComponent(typeof(Button))]
public class KeyBackButtonActivator : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Key _key = Key.Escape;

    [Tooltip("Played through SFXController whenever this Back button is pressed (mouse, key, or gamepad).")]
    [SerializeField] private AudioClip _clickSfx;

    private Button _button;
    private GamepadBackButtonActivator _partner;
    private TextButton _textButton;

    // One press can invoke both the parent and child Buttons (and several activators on Escape),
    // so the click sound is limited to once per frame.
    private static int _lastClickSfxFrame = -1;

    private static readonly HashSet<KeyBackButtonActivator> _instances = new HashSet<KeyBackButtonActivator>();

    /// <summary>
    /// True if any enabled <see cref="KeyBackButtonActivator"/> bound to <see cref="Key.Escape"/>
    /// was active-and-interactable as of the end of the previous frame. Other systems (e.g.
    /// <c>UIController</c>'s Pause toggle) read this to let a visible Back button "own" the
    /// Escape key instead of also pausing the game. Refreshed every frame in <see cref="LateUpdate"/>
    /// so it reflects state from before the current frame's input is processed, avoiding a race
    /// with this same frame's Back button click.
    /// </summary>
    public static bool AnyEscapeBackButtonInteractable { get; private set; }

    /// <summary>True only for the frame in which an Escape-bound Back button handled Escape.</summary>
    public static bool EscapeBackButtonPressedThisFrame { get; private set; }

    public static void ClearEscapeBackButtonPressedThisFrame()
    {
        EscapeBackButtonPressedThisFrame = false;
    }

    private void Awake()
    {
        _button = GetComponent<Button>();
        _partner = GetComponentInChildren<GamepadBackButtonActivator>(true);
        _textButton = GetComponent<TextButton>();
    }

    /// <summary>True if this activator's own Button can currently be clicked.</summary>
    public bool IsClickable => _button != null && _button.isActiveAndEnabled && _button.interactable;

    /// <summary>
    /// Plays the Back click sound at most once per frame. Pointer presses are skipped when a
    /// <see cref="TextButton"/> on this object already plays its own click sound on pointer down.
    /// </summary>
    public void PlayClickSfx(bool fromPointer)
    {
        if (_clickSfx == null || _lastClickSfxFrame == Time.frameCount) return;
        if (fromPointer && _textButton != null && _textButton.HasClickSfx) return;

        _lastClickSfxFrame = Time.frameCount;
        SFXController.Instance?.Play(_clickSfx);
    }

    private void OnEnable()
    {
        _instances.Add(this);
    }

    private void OnDisable()
    {
        _instances.Remove(this);
        RefreshAnyEscapeBackButtonInteractable();
    }

    private void Update()
    {
        // While a text field (e.g. the PC search bar) owns the keyboard, Q/Escape belong to it.
        if (GoodCopBadCop.Input.TextInputFocus.IsCapturingKeyboard) return;
        if (!(Keyboard.current?[_key].wasPressedThisFrame ?? false)) return;

        if (_key == Key.Escape && _button != null && _button.isActiveAndEnabled && _button.interactable)
            EscapeBackButtonPressedThisFrame = true;

        // Checked before invoking, since the Back action usually deactivates this screen.
        if (IsClickable || (_partner != null && _partner.IsClickable))
            PlayClickSfx(false);

        InvokeButton();
        if (_partner != null)
            _partner.InvokeButton();
    }

    private void LateUpdate()
    {
        RefreshAnyEscapeBackButtonInteractable();
    }

    private static void RefreshAnyEscapeBackButtonInteractable()
    {
        foreach (var activator in _instances)
        {
            if (activator._key == Key.Escape
                && activator._button != null
                && activator._button.isActiveAndEnabled
                && activator._button.interactable)
            {
                AnyEscapeBackButtonInteractable = true;
                return;
            }
        }

        AnyEscapeBackButtonInteractable = false;
    }

    /// <summary>
    /// Mouse clicks land on this (parent) object's raycast Image, so Button only fires this
    /// object's own onClick. Mirror the key path by also invoking the paired child Button,
    /// which is where many screens (e.g. Start Campaign Screen) wire the real Back action.
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left
            || _button == null
            || !_button.isActiveAndEnabled
            || !_button.interactable)
            return;

        PlayClickSfx(true);

        if (_partner != null)
            _partner.InvokeButton();
    }

    /// <summary>Invokes this activator's Button.onClick if it is currently clickable.</summary>
    public void InvokeButton()
    {
        if (_button == null || !_button.isActiveAndEnabled || !_button.interactable) return;
        _button.onClick.Invoke();
    }
}
