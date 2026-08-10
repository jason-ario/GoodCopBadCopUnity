using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class TextButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] private Animator anim;
    [SerializeField] private AudioClip sfxOnSelect;
    [SerializeField] private AudioClip sfxOnClick;
    [SerializeField] private bool useHover;
    Button button;
    public bool disableAnimation;
    private bool isActiveTab;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (button != null && !button.interactable) return;
        if(anim != null)
        {
            anim.SetBool(useHover ? "Hovering" : "Selected", true);
        }
        SFXController.Instance?.Play(sfxOnSelect);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (button != null && !button.interactable) return;
        if (isActiveTab) return;
        if (anim != null)
        {
            anim.SetBool(useHover ? "Hovering" : "Selected", false);
        }
    }

    /// <summary>Plays the click sound on pointer down, before any OnClick listener can deactivate the GameObject.</summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        if (button != null && !button.interactable) return;
        SFXController.Instance?.Play(sfxOnClick);
        if (anim != null)
        {
            anim.SetBool("Selected", true);
        }
    }

    /// <summary>Mirrors OnPointerEnter for gamepad/keyboard EventSystem navigation.</summary>
    public void OnSelect(BaseEventData eventData)
    {
        if (button != null && !button.interactable) return;
        if (anim != null)
        {
            anim.SetBool(useHover ? "Hovering" : "Selected", true);
        }

        SFXController.Instance?.Play(sfxOnSelect);
    }

    /// <summary>Mirrors OnPointerExit for gamepad/keyboard EventSystem navigation.</summary>
    public void OnDeselect(BaseEventData eventData)
    {
        if (button != null && !button.interactable) return;
        if (isActiveTab) return;
        if (anim != null)
        {
            anim.SetBool(useHover ? "Hovering" : "Selected", false);
        }
    }

    /// <summary>Marks this button as the active tab, keeping the selected animation state persistent.</summary>
    public void SetActiveTab(bool active)
    {
        isActiveTab = active;
        anim.SetBool("Selected", active);
        if (anim != null)
        {
            if (!active) anim.SetBool("Hovering", false);
        }
    }

    public void Reset()
    {
        isActiveTab = false;
        if (anim != null)
        {
            anim.SetBool("Selected", false);
            anim.SetBool("Hovering", false);
        }
    }
}
