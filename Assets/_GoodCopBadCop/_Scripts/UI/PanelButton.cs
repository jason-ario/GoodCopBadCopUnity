using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Hover / focus animation and sounds for panel-style buttons. Gamepad and keyboard focus
/// (EventSystem select/deselect) drive the same "Selected" animator state as the mouse.
/// </summary>
public class PanelButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] private Animator _animator;
    [SerializeField] private AudioClip sfxOnSelect;
    [SerializeField] private AudioClip sfxOnClick;

    public void OnPointerEnter(PointerEventData eventData)
    {
        SFXController.Instance?.Play(sfxOnSelect);
        SetSelected(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // Keep the highlight while this button still holds gamepad/keyboard focus.
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject) return;
        SetSelected(false);
    }

    /// <summary>Plays the click sound on pointer down, before any OnClick listener can deactivate the GameObject.</summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        SFXController.Instance?.Play(sfxOnClick);
    }

    /// <summary>Mirrors OnPointerEnter for gamepad/keyboard EventSystem navigation.</summary>
    public void OnSelect(BaseEventData eventData)
    {
        SFXController.Instance?.Play(sfxOnSelect);
        SetSelected(true);
    }

    /// <summary>Mirrors OnPointerExit for gamepad/keyboard EventSystem navigation.</summary>
    public void OnDeselect(BaseEventData eventData) => SetSelected(false);

    // A screen closed while this button was highlighted must not reopen with it stuck highlighted.
    private void OnDisable() => SetSelected(false);

    private void SetSelected(bool selected)
    {
        if (_animator != null && _animator.isActiveAndEnabled && _animator.runtimeAnimatorController != null)
            _animator.SetBool("Selected", selected);
    }
}
