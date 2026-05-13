using UnityEngine;

public class ShutterController : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip openClip;
    [SerializeField] private AudioClip closeClip;

    /// <summary>True while the booth window is open.</summary>
    public bool IsOpen { get; private set; }

    public void OpenShutter()
    {
        IsOpen = true;
        animator.SetBool("Open", true);
        audioSource.PlayOneShot(openClip);
    }
    
    public void CloseShutter()
    {
        IsOpen = false;
        animator.SetBool("Open", false);
        audioSource.PlayOneShot(closeClip);
    }

    public void ResetShutter()
    {
        IsOpen = false;
        animator.SetBool("Open", false);
        animator.SetTrigger("Reset");
    }
}
