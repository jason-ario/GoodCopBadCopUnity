using Unity.Netcode;
using UnityEngine;

public class SwitchCover : Interactable
{
    [SerializeField] Animator anim;
    public NetworkVariable<bool> switchCoverOpen;
    [SerializeField] private BoxCollider switchBoxCollider;
    [SerializeField] AudioSource audioSource;

    public override void Interact(PlayerInteractionController player)
    {
        if (switchCoverOpen.Value)
        {
            player.playerAnimationController.SetAnimTrigger("CloseSwitchCover");
            anim.SetBool("SwitchOpen", false);
            switchBoxCollider.enabled = false;
            switchCoverOpen.Value = false;
            audioSource.Play();
        }
        else
        {
            player.playerAnimationController.SetAnimTrigger("OpenSwitchCover");
            anim.SetBool("SwitchOpen", true);
            switchBoxCollider.enabled = true;
            switchCoverOpen.Value = true;
            audioSource.Play();
        }
    }
}
