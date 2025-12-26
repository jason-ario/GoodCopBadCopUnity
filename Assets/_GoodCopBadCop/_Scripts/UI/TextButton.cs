using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class TextButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Animator anim;
    [SerializeField] private AudioClip sfxOnSelect;
    [SerializeField] private AudioClip sfxOnClick;
    [SerializeField] AudioSource audioSource;

    public void OnPointerEnter(PointerEventData eventData)
    {
        anim.SetBool("Selected", true);
        audioSource.Play();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        anim.SetBool("Selected", false);
    }
}