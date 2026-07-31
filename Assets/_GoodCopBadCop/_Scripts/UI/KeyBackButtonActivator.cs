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

    private void Awake()
    {
        _button = GetComponent<Button>();
        _partner = GetComponentInChildren<GamepadBackButtonActivator>(true);
    }

    private void Update()
    {
        if (!(Keyboard.current?[_key].wasPressedThisFrame ?? false)) return;

        InvokeButton();
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
