using Unity.Netcode;
using UnityEngine;

public class Drawer : Interactable
{
    [SerializeField] private Animator animator;
    public NetworkVariable<bool> isOpen = new NetworkVariable<bool>();
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip drawerOpenSound;
    [SerializeField] AudioClip drawerCloseSound;

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        ToggleDrawer();
    }

    void ToggleDrawer()
    {
        isOpen.Value = !isOpen.Value;
        animator.SetBool("Open", isOpen.Value);
        
        audioSource.PlayOneShot(isOpen.Value ? drawerOpenSound : drawerCloseSound);
    }
}
