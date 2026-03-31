using UnityEngine;
using UnityEngine.Events;

public class ClickablePCElement : MonoBehaviour
{
    [SerializeField] Animator animator;
    public UnityEvent onClickEvent;
    
    public virtual void OnHoverEnter()
    {
        Debug.Log(name + " hover enter");
        animator.SetBool("Hovering", true);
    }

    public virtual void OnHoverExit()
    {
        Debug.Log(name + " hover exit");
        animator.SetBool("Hovering", false);
    }

    public virtual void OnClick()
    {
        Debug.Log(name + " clicked");
        onClickEvent.Invoke();
    }
}