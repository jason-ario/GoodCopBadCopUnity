using System.Collections.Generic;
using UnityEngine;
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
public class KeyBackButtonActivator : MonoBehaviour
{
    [SerializeField] private Key _key = Key.Escape;

    private Button _button;
    private GamepadBackButtonActivator _partner;

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

    private void Awake()
    {
        _button = GetComponent<Button>();
        _partner = GetComponentInChildren<GamepadBackButtonActivator>(true);
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
        if (!(Keyboard.current?[_key].wasPressedThisFrame ?? false)) return;

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

    /// <summary>Invokes this activator's Button.onClick if it is currently clickable.</summary>
    public void InvokeButton()
    {
        if (_button == null || !_button.isActiveAndEnabled || !_button.interactable) return;
        _button.onClick.Invoke();
    }
}
