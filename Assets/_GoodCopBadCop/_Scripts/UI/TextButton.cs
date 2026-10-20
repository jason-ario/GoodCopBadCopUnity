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

    private static readonly int DisabledParam = Animator.StringToHash("Disabled");
    private bool hasDisabledParam;
    private bool? lastInteractable;

    /// <summary>True if this button plays its own click sound on pointer down.</summary>
    public bool HasClickSfx => sfxOnClick != null;

    private void Awake()
    {
        button = GetComponent<Button>();

        // Menu feedback must keep animating while a single-player pause has Time.timeScale at 0.
        if (anim != null)
        {
            anim.updateMode = AnimatorUpdateMode.UnscaledTime;

            // Only controllers with a "Disabled" bool (e.g. Text Button.controller) get the greyed-out state.
            foreach (AnimatorControllerParameter p in anim.parameters)
            {
                if (p.nameHash == DisabledParam && p.type == AnimatorControllerParameterType.Bool)
                {
                    hasDisabledParam = true;
                    break;
                }
            }
        }
    }

    private void OnEnable()
    {
        // Animator parameters reset when the object is re-enabled, so force a resync.
        lastInteractable = null;
        SyncDisabledState();
    }

    private void Update() => SyncDisabledState();

    /// <summary>
    /// Mirrors <see cref="Selectable.interactable"/> into the animator's "Disabled" bool so
    /// non-interactable buttons (e.g. Buy when the player can't afford it) show the greyed-out state.
    /// </summary>
    private void SyncDisabledState()
    {
        if (!hasDisabledParam || button == null || !anim.isActiveAndEnabled) return;

        bool interactable = button.interactable;
        if (lastInteractable == interactable) return;
        lastInteractable = interactable;

        anim.SetBool(DisabledParam, !interactable);
        if (!interactable)
        {
            // Hover/select handlers early-out while disabled, so clear any stale highlight now.
            anim.SetBool("Selected", false);
            anim.SetBool("Hovering", false);
        }
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
