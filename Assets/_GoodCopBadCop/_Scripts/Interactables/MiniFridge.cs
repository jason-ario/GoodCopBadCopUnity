using UnityEngine;

public class MiniFridge : Interactable
{
    [SerializeField] AudioSource audioSource;  
    [SerializeField] private AudioClip fridgeCloseSound;
    [SerializeField] private AudioClip fridgeOpenSound;
    [SerializeField] Animator animator;
    bool fridgeOpen = false;
    
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (fridgeOpen)
        {
            audioSource.PlayOneShot(fridgeCloseSound);
            fridgeOpen = false;
            animator.SetBool("Open", false);
        }
        else
        {
            audioSource.PlayOneShot(fridgeOpenSound);
            fridgeOpen = true;
            animator.SetBool("Open", true);
        }
    }
}
