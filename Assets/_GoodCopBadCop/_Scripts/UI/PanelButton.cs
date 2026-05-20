using UnityEngine;
using UnityEngine.EventSystems;

public class PanelButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
{
    [SerializeField] private Animator _animator;
    [SerializeField] private AudioClip sfxOnSelect;
    [SerializeField] private AudioClip sfxOnClick;

    public void OnPointerEnter(PointerEventData eventData)
    {
        SFXController.Instance?.Play(sfxOnSelect);

        if (_animator != null)
        {
            _animator.SetBool("Selected", true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_animator != null)
        {
            _animator.SetBool("Selected", false);
        }
    }

    /// <summary>Plays the click sound on pointer down, before any OnClick listener can deactivate the GameObject.</summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        SFXController.Instance?.Play(sfxOnClick);
    }
}
