using HighlightPlus;
using UnityEngine;

public class ApplicationLetter : PickableObject
{
    [SerializeField] private NetworkDrawableLine _drawableLine;
    
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
    
    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableItemData itemData)
    {
        if (itemData.name == "RedPencil")
        {
            EnterDrawMode(playerInteractionController);
        }
    }

    void EnterDrawMode(PlayerInteractionController playerInteractionController)
    {
        playerInteractionController.GetComponent<PlayerMovementController>().SetCanControl(false);
        playerInteractionController.enabled = false;
        _drawableLine.EnterDrawMode();
        GetComponent<HighlightEffect>().enabled = false;
    }
}
