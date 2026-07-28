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
/// Attach to any Button that represents a "Back" or "Cancel" action.
/// </summary>
[RequireComponent(typeof(Button))]
public class GamepadBackButtonActivator : MonoBehaviour
{
    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
    }

    private void Update()
    {
        if (!(Gamepad.current?.buttonEast.wasPressedThisFrame ?? false)) return;
        if (_button == null || !_button.isActiveAndEnabled || !_button.interactable) return;

        _button.onClick.Invoke();
    }
}
