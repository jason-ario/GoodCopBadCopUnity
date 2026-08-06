using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Sits on one pre-placed slot of the <see cref="EmoteWheelUI"/> wheel. The actual click → emote
/// wiring is done via the Button's OnClick() persistent call (targeting
/// <see cref="EmoteWheelUI.PlayEmote(string)"/> with this slot's emote name as the argument), so
/// this script only owns the "Selected" highlight: it shows while the button is actively being
/// pressed and hides on release, on pointer exit, or when <see cref="Deselect"/> is called
/// (e.g. by <see cref="EmoteWheelUI.Hide"/> when the wheel closes).
/// </summary>
[RequireComponent(typeof(Button))]
public class EmoteButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Tooltip("Must match the Name field of an EmoteDefinition entry in EmoteWheelUI. Also used as " +
             "the argument for this button's OnClick() -> EmoteWheelUI.PlayEmote(string) binding.")]
    [SerializeField] private string emoteName;

    [Tooltip("Overlay object shown only while this button is actively being pressed (e.g. the 'Selected' child).")]
    [SerializeField] private GameObject selectedOverlay;

    private bool _isPressed;

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPressed = true;
        SetHighlighted(true);
        EmoteWheelUI.Instance?.NotifyButtonPressState(true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_isPressed) return;
        EndPress();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_isPressed) return;
        EndPress();
    }

    /// <summary>Forces the highlight off. Called by <see cref="EmoteWheelUI"/> when the wheel closes.</summary>
    public void Deselect()
    {
        if (_isPressed) EndPress();
    }

    private void EndPress()
    {
        _isPressed = false;
        SetHighlighted(false);
        EmoteWheelUI.Instance?.NotifyButtonPressState(false);
    }

    private void SetHighlighted(bool highlighted)
    {
        if (selectedOverlay != null)
            selectedOverlay.SetActive(highlighted);
    }
}
