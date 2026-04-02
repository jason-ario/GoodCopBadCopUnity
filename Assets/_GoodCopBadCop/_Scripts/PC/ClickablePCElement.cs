using UnityEngine;
using UnityEngine.Events;

public class ClickablePCElement : MonoBehaviour
{
    [SerializeField] Animator animator;
    public UnityEvent onClickEvent;
    
    public virtual void OnHoverEnter()
    {
        Debug.Log(name + " hover enter");
        if (animator != null)
        {
            animator.SetBool("Hovering", true);
        }
    }

    public virtual void OnHoverExit()
    {
        Debug.Log(name + " hover exit");
        if (animator != null)
        {
            animator.SetBool("Hovering", false);
        }    
    }

    public virtual void OnClick()
    {
        Debug.Log(name + " clicked");
        onClickEvent.Invoke();
    }
}