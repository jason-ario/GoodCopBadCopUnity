using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class TextButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
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
        anim.SetBool(useHover ? "Hovering" : "Selected", true);
        SFXController.Instance?.Play(sfxOnSelect);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (button != null && !button.interactable) return;
        if (isActiveTab) return;
        anim.SetBool(useHover ? "Hovering" : "Selected", false);
    }

    /// <summary>Plays the click sound on pointer down, before any OnClick listener can deactivate the GameObject.</summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        if (button != null && !button.interactable) return;
        SFXController.Instance?.Play(sfxOnClick);
        anim.SetBool("Selected", true);
    }

    /// <summary>Marks this button as the active tab, keeping the selected animation state persistent.</summary>
    public void SetActiveTab(bool active)
    {
        isActiveTab = active;
        anim.SetBool("Selected", active);
        if (!active) anim.SetBool("Hovering", false);
    }

    public void Reset()
    {
        isActiveTab = false;
        anim.SetBool("Selected", false);
        anim.SetBool("Hovering", false);
    }
}
