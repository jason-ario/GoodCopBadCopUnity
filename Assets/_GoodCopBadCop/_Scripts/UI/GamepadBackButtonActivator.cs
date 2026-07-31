using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Lets a UI <see cref="Button"/> also respond to the gamepad East button
/// (B on Xbox, Circle on PlayStation — the right-hand face button used as
/// "back/cancel" on most controllers), in addition to mouse/touch clicks.
///
/// EventSystem's Submit/Cancel actions only reach the currently *selected*
/// UI element, so a plain Button does not react to the Cancel action unless
/// it is selected. This component makes the button respond directly,
/// regardless of selection, mirroring the "B = cancel" polling pattern
/// already used in <see cref="MainMenuController"/>.
///
/// The "Back button" prefab keeps its gamepad/click Button on a child object
/// and its key-hint Button on the parent object, so pressing gamepad East
/// also invokes the paired <see cref="KeyBackButtonActivator"/>'s Button (and
/// vice versa) — this keeps Q, gamepad B/East, and mouse clicks behaving
/// identically no matter which Button ended up wired with the real action.
///
/// Attach to any Button that represents a "Back" or "Cancel" action.
/// </summary>
[RequireComponent(typeof(Button))]
public class GamepadBackButtonActivator : MonoBehaviour
{
    private Button _button;
    private KeyBackButtonActivator _partner;

    private void Awake()
    {
        _button = GetComponent<Button>();
        _partner = GetComponentInParent<KeyBackButtonActivator>(true);
    }

    private void Update()
    {
        if (!(Gamepad.current?.buttonEast.wasPressedThisFrame ?? false)) return;

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
