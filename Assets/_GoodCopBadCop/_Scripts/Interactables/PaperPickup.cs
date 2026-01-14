using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class PaperPickup : PickableObject
{
    private bool isStamping;
    
    public override void OnStartUse()
    {
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);
        UIController.Instance.OpenNewspaper();
    }
    
    public override void OnStopUse()
    {
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
        UIController.Instance.CloseNewspaper();
    }
    
    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableItemData heldItem)
    {
        playerPickupController = playerInteractionController.GetComponent<PlayerPickupController>();
        StartCoroutine(UseStamp());
    }
    
    IEnumerator UseStamp()
    {
        if (isStamping) yield break;
        PlayerInstance.Instance.CanControl = false;
        isStamping = true;
        playerPickupController.PlayerAnimationController.SetAnimTrigger("UseStamp");
        yield return new WaitForSeconds(2); 
        isStamping = false;
        PlayerInstance.Instance.CanControl = true;
    }
}
