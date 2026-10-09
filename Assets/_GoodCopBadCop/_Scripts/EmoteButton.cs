using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Sits on one pre-placed slot of the <see cref="EmoteWheelUI"/> wheel. The actual click → emote
/// wiring is done via the Button's OnClick() persistent call (targeting
/// <see cref="EmoteWheelUI.PlayEmote(string)"/> with this slot's emote name as the argument), so
/// this script only owns the hit area and the "Selected" overlay's hover/press animation:
///  - Hovering (not pressed) shows the overlay semi-transparent (<see cref="hoverAlpha"/>).
///  - Pressing down makes it fully opaque.
///  - Releasing while still hovering returns it to semi-transparent; moving off (or losing focus)
///    hides it entirely.
///
/// Hit testing is angular: at runtime an invisible <see cref="RaycastOnlyGraphic"/> covering the
/// whole wheel is added as a child, and <see cref="IsRaycastLocationValid"/> (an
/// <see cref="ICanvasRaycastFilter"/>, which also filters every child graphic such as the Icon)
/// only accepts the pointer when it lies in the ring between <see cref="innerDeadZone"/> and
/// <see cref="outerRadius"/> AND this slot's direction is the closest of all sibling slots. The
/// whole wedge is therefore hoverable, not just the icon in its center.
///
/// A per-frame watchdog in <see cref="Update"/> force-releases the press if the mouse/gamepad
/// submit button is no longer physically held, even if no OnPointerUp event ever arrives (e.g.
/// the button was released outside the game view).
///
/// Gamepad: <see cref="EmoteWheelUI"/> aims with a stick and calls <see cref="SetGamepadHighlight"/>
/// / <see cref="GamepadPress"/>; the gamepad highlight shares the hover overlay.
/// </summary>
[RequireComponent(typeof(Button))]
public class EmoteButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ICanvasRaycastFilter
{
    [Tooltip("Must match the Name field of an EmoteDefinition entry in EmoteWheelUI. Also used as " +
             "the argument for this button's OnClick() -> EmoteWheelUI.PlayEmote(string) binding.")]
    [SerializeField] private string emoteName;

    [Tooltip("Overlay object shown while hovering/pressing this button (e.g. the 'Selected' child).")]
    [SerializeField] private GameObject selectedOverlay;

    [Tooltip("Alpha of the selected overlay while hovering but not pressed.")]
    [SerializeField, Range(0f, 1f)] private float hoverAlpha = 0.5f;

    [Header("Hit Area")]
    [Tooltip("The wheel's RectTransform; its rect center is the wheel center and half its smaller " +
             "side is the wheel radius. Defaults to this button's parent.")]
    [SerializeField] private RectTransform wheelRect;

    [Tooltip("Rect whose center marks this slot's direction from the wheel center. Defaults to the " +
             "selected overlay.")]
    [SerializeField] private RectTransform directionReference;

    [Tooltip("Inner dead zone as a fraction of the wheel radius. Pointer closer to the center than " +
             "this selects nothing.")]
    [SerializeField, Range(0f, 1f)] private float innerDeadZone = 0.2f;

    [Tooltip("Outer hit limit as a fraction of the wheel radius (>1 gives some forgiveness past the edge).")]
    [SerializeField, Min(0f)] private float outerRadius = 1.15f;

    private Image          _overlayImage;
    private RectTransform  _hitArea;
    private EmoteButton[]  _slots;
    private Button         _button;
    private bool           _isHovering;
    private bool           _isPressed;
    private bool           _isGamepadHighlighted;

    /// <summary>True while the pointer hovers this slot or the gamepad stick has it highlighted.</summary>
    private bool IsHighlighted => _isHovering || _isGamepadHighlighted;

    private void Awake()
    {
        _button = GetComponent<Button>();

        if (selectedOverlay != null)
        {
            _overlayImage = selectedOverlay.GetComponent<Image>();
            if (directionReference == null)
                directionReference = selectedOverlay.transform as RectTransform;
        }

        if (wheelRect == null)
            wheelRect = transform.parent as RectTransform;

        _slots = transform.parent != null
            ? transform.parent.GetComponentsInChildren<EmoteButton>(includeInactive: true)
            : new[] { this };

        CreateHitArea();
    }

    private void OnEnable()
    {
        FitHitArea();
    }

    private void Update()
    {
        if (!_isPressed) return;

        Gamepad gp = Gamepad.current;
        bool stillHeld = Input.GetMouseButton(0)
                         || (gp != null && (gp.buttonSouth.isPressed || gp.rightTrigger.isPressed));
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
            SetOverlay(IsHighlighted, hoverAlpha);
    }

    // ─── Gamepad ────────────────────────────────────────────────────────────

    /// <summary>Stick-driven highlight, set by <see cref="EmoteWheelUI"/> while a gamepad aims at this slot.</summary>
    public void SetGamepadHighlight(bool highlighted)
    {
        if (_isGamepadHighlighted == highlighted) return;
        _isGamepadHighlighted = highlighted;
        if (!_isPressed)
            SetOverlay(IsHighlighted, hoverAlpha);
    }

    /// <summary>
    /// Gamepad confirm on this slot: shows the pressed overlay (released by the Update watchdog once
    /// the confirm button is let go) and invokes the Button's OnClick, the same path as a mouse click.
    /// </summary>
    public void GamepadPress()
    {
        if (_button == null || !_button.isActiveAndEnabled || !_button.IsInteractable()) return;

        _isPressed = true;
        SetOverlay(true, 1f);
        _button.onClick.Invoke();
    }

    /// <summary>Screen-space direction from the wheel center to this slot.</summary>
    public bool TryGetScreenDirection(out Vector2 direction)
    {
        direction = Vector2.zero;
        if (wheelRect == null || directionReference == null) return false;

        Canvas canvas = GetComponentInParent<Canvas>();
        Canvas root = canvas != null ? canvas.rootCanvas : null;
        Camera cam = root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;

        Vector2 center = RectTransformUtility.WorldToScreenPoint(cam, wheelRect.TransformPoint(wheelRect.rect.center));
        Vector2 slot = RectTransformUtility.WorldToScreenPoint(cam, directionReference.TransformPoint(directionReference.rect.center));
        direction = slot - center;
        return direction.sqrMagnitude > 0.0001f;
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
        _isGamepadHighlighted = false;
        SetOverlay(false, hoverAlpha);
    }

    // ─── Hit testing ────────────────────────────────────────────────────────

    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        if (wheelRect == null || !TryGetDirectionAngle(out float myAngle))
            return true;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(wheelRect, screenPoint, eventCamera, out Vector2 local))
            return false;

        Rect rect = wheelRect.rect;
        Vector2 offset = local - rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
        float distance = offset.magnitude;
        if (distance < radius * innerDeadZone || distance > radius * outerRadius)
            return false;

        float pointerAngle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
        float myDelta = Mathf.Abs(Mathf.DeltaAngle(pointerAngle, myAngle));

        foreach (EmoteButton slot in _slots)
        {
            if (slot == null || slot == this || !slot.isActiveAndEnabled) continue;
            if (slot.TryGetDirectionAngle(out float otherAngle) &&
                Mathf.Abs(Mathf.DeltaAngle(pointerAngle, otherAngle)) < myDelta)
                return false;
        }

        return true;
    }

    /// <summary>Angle (degrees, wheel-local space) from the wheel center to this slot's direction reference.</summary>
    private bool TryGetDirectionAngle(out float angle)
    {
        angle = 0f;
        if (wheelRect == null || directionReference == null) return false;

        Vector3 worldCenter = directionReference.TransformPoint(directionReference.rect.center);
        Vector2 offset = (Vector2)wheelRect.InverseTransformPoint(worldCenter) - wheelRect.rect.center;
        if (offset.sqrMagnitude < 0.0001f) return false;

        angle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
        return true;
    }

    private void CreateHitArea()
    {
        if (wheelRect == null) return;

        var go = new GameObject("Hit Area", typeof(RectTransform), typeof(CanvasRenderer), typeof(RaycastOnlyGraphic))
        {
            layer = gameObject.layer
        };
        _hitArea = (RectTransform)go.transform;
        _hitArea.SetParent(transform, false);
        _hitArea.SetAsFirstSibling();
        _hitArea.anchorMin = _hitArea.anchorMax = _hitArea.pivot = new Vector2(0.5f, 0.5f);
    }

    /// <summary>Sizes the invisible hit graphic to a square that covers the full (outer-radius) wheel.</summary>
    private void FitHitArea()
    {
        if (_hitArea == null || wheelRect == null) return;

        Rect rect = wheelRect.rect;
        _hitArea.position = wheelRect.TransformPoint(rect.center);
        _hitArea.localRotation = Quaternion.identity;
        _hitArea.localScale = Vector3.one;

        float wheelToLocal = wheelRect.lossyScale.x / Mathf.Max(0.0001f, transform.lossyScale.x);
        float diameter = Mathf.Min(rect.width, rect.height) * Mathf.Max(1f, outerRadius) * wheelToLocal;
        _hitArea.sizeDelta = new Vector2(diameter, diameter);
    }

    private void EndPress()
    {
        _isPressed = false;
        SetOverlay(IsHighlighted, hoverAlpha);
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
