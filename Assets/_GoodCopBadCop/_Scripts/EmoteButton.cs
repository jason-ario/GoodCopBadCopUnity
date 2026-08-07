using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Sits on one pre-placed slot of the <see cref="EmoteWheelUI"/> wheel. The actual click → emote
/// wiring is done via the Button's OnClick() persistent call (targeting
/// <see cref="EmoteWheelUI.PlayEmote(string)"/> with this slot's emote name as the argument), so
/// this script only owns the "Selected" overlay's hover/press animation:
///  - Hovering (not pressed) shows the overlay semi-transparent (<see cref="hoverAlpha"/>).
///  - Pressing down makes it fully opaque.
///  - Releasing while still hovering returns it to semi-transparent; moving off (or losing focus)
///    hides it entirely.
///
/// A per-frame watchdog in <see cref="Update"/> force-releases the press if the mouse/gamepad
/// submit button is no longer physically held, even if no OnPointerUp event ever arrives (e.g.
/// the button was released outside the game view).
/// </summary>
[RequireComponent(typeof(Button))]
public class EmoteButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("Must match the Name field of an EmoteDefinition entry in EmoteWheelUI. Also used as " +
             "the argument for this button's OnClick() -> EmoteWheelUI.PlayEmote(string) binding.")]
    [SerializeField] private string emoteName;

    [Tooltip("Overlay object shown while hovering/pressing this button (e.g. the 'Selected' child).")]
    [SerializeField] private GameObject selectedOverlay;

    [Tooltip("Alpha of the selected overlay while hovering but not pressed.")]
    [SerializeField, Range(0f, 1f)] private float hoverAlpha = 0.5f;

    private Image _overlayImage;
    private bool  _isHovering;
    private bool  _isPressed;

    private void Awake()
    {
        if (selectedOverlay != null)
            _overlayImage = selectedOverlay.GetComponent<Image>();
    }

    private void Update()
    {
        if (!_isPressed) return;

        bool stillHeld = Input.GetMouseButton(0) || (Gamepad.current?.buttonSouth.isPressed ?? false);
        if (!stillHeld)
            EndPress();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _isHovering = true;
        if (!_isPressed)
            SetOverlay(true, hoverAlpha);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isHovering = false;
        if (_isPressed)
            EndPress();
        else
            SetOverlay(false, hoverAlpha);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPressed = true;
        SetOverlay(true, 1f);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_isPressed) return;
        EndPress();
    }

    /// <summary>Forces the highlight off. Called by <see cref="EmoteWheelUI"/> when the wheel closes.</summary>
    public void Deselect()
    {
        _isPressed = false;
        _isHovering = false;
        SetOverlay(false, hoverAlpha);
    }

    private void EndPress()
    {
        _isPressed = false;
        SetOverlay(_isHovering, hoverAlpha);
    }

    private void SetOverlay(bool active, float alpha)
    {
        if (selectedOverlay == null) return;

        selectedOverlay.SetActive(active);
        if (_overlayImage != null)
        {
            Color c = _overlayImage.color;
            c.a = alpha;
            _overlayImage.color = c;
        }
    }
}
