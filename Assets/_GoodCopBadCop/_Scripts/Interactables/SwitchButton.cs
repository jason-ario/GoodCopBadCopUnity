using Unity.Netcode;
using UnityEngine;

public class SwitchButton : Interactable
{
    [SerializeField] private AudioSource buttonPressSound;
    [SerializeField] private Animator anim;
    [SerializeField] Transform ikTarget;
    public override void Interact(PlayerInteractionController player)
    {
        GameManager.Instance.TryStartLevel();
        player.playerAnimationController.SetAnimTrigger("PressButton");
        player.playerAnimationController.TurnRightArmRigOnAndOff(.2f,.5f);
        player.playerAnimationController.CamRightArmRigIKTarget = ikTarget;
        player.playerAnimationController.RightArmRigIKTarget = ikTarget;
        PlayButtonSoundClientRpc();
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
