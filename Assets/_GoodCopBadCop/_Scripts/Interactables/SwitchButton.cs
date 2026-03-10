using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class SwitchButton : Interactable
{
    [SerializeField] private AudioSource buttonPressSound;
    [SerializeField] private Animator anim;
    [SerializeField] Transform ikTarget;
    
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        GameManager.Instance.TryStartLevel();
        StartCoroutine(EnableAndDisableMask(player));
        player.playerAnimationController.SetAnimTrigger("PressButton");
        player.playerAnimationController.TurnRightArmRigOnAndOff(.2f,.5f);
        player.playerAnimationController.RightArmRigIKTarget = ikTarget;
        PlayButtonSoundClientRpc();
    }

    IEnumerator EnableAndDisableMask(PlayerInteractionController player)
    {
        player.playerMovementController.SetCanControl(false);
        player.playerAnimationController.EnableHoldObjectMask();
        yield return new WaitForSeconds(1);
        player.playerAnimationController.DisableHoldObjectMask();
        player.playerMovementController.SetCanControl(true);
    }

    [ClientRpc]
    private void PlayButtonSoundClientRpc()
    {
        if (buttonPressSound != null)
        {
            buttonPressSound.Play();
        }
        
        anim.SetTrigger("Push");
    }
}
