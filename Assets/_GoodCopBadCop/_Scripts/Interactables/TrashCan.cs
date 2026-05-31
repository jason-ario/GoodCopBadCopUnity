using Unity.VisualScripting;
using UnityEngine;

public class TrashCan : Interactable
{
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip throwTrashSound;
    
    public override void Interact(PlayerInteractionController player)
    {
        //throw trash
    }

    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        base.InteractWithItem(playerInteractionController, item);
        playerInteractionController.pickupController.DestroyEquippedItem();
        audioSource.PlayOneShot(throwTrashSound);
    }
}
