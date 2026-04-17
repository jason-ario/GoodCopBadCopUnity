using UnityEngine;

public class ShutterController : MonoBehaviour
{
    [SerializeField] Animator animator;
    
    public void OpenShutter()
    {
        animator.SetBool("Open", true);
    }
    
    public void CloseShutter()
    {
        animator.SetBool("Open", false);
    }

    public void ResetShutter()
    {
        animator.SetBool("Open", false);
        animator.SetTrigger("Reset");
    }
}
