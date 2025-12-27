using UnityEngine;
using UnityEngine.EventSystems;

public class PanelButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Animator _animator;
    [SerializeField] private AudioClip sfxOnSelect;
    [SerializeField] AudioSource audioSource;
    
    public void OnPointerEnter(PointerEventData eventData)
    {
        audioSource.Play();

        _animator.SetBool("Selected", true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _animator.SetBool("Selected", false);
    }
}
