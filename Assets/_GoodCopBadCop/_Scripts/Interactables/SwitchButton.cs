using Unity.Netcode;
using UnityEngine;

public class SwitchButton : Interactable
{
    [SerializeField] private AudioSource buttonPressSound;
    [SerializeField] private Animator anim;
    public override void Interact(PlayerInteractionController player)
    {
        GameManager.Instance.TryStartLevel();
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
