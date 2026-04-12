using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class SelectableTab : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
{
    private bool isSelected = false;
    public bool IsSelected => isSelected;
    [SerializeField] private Animator _animator;
    [SerializeField] UnityEvent onSelected;
    
    public void Select()
    {
        onSelected?.Invoke();
    }

    public void SetHovering(bool hovering)
    {
        if (isSelected)
        {
            return;
        }

        _animator.SetBool("Hovering", hovering);
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        _animator.SetBool("Selected", selected);
        SetHovering(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        SetHovering(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetHovering(false);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        Select();
    }
}
