using UnityEngine;

public class ShutterController : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip openClip;
    [SerializeField] private AudioClip closeClip;
    
    public void OpenShutter()
    {
        animator.SetBool("Open", true);
        audioSource.PlayOneShot(openClip);
    }
    
    public void CloseShutter()
    {
        animator.SetBool("Open", false);
        audioSource.PlayOneShot(closeClip);
    }

    public void ResetShutter()
    {
        animator.SetBool("Open", false);
        animator.SetTrigger("Reset");
    }
}
