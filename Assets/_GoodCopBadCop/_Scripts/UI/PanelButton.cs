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
}
