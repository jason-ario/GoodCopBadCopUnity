using HighlightPlus;
using UnityEngine;

public class Paper : PickableObject
{
    [SerializeField] private NetworkDrawableLine _drawableLine;
    public override void OnStartUse()
    {
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);
    }
    
    public override void OnStopUse()
    {
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
    }
    
    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        if (item.ItemData.name == "RedPencil")
        {
            base.InteractWithItem(playerInteractionController, item);
            EnterDrawMode(playerInteractionController);
        }
    }

    void EnterDrawMode(PlayerInteractionController playerInteractionController)
    {
        playerInteractionController.GetComponent<PlayerMovementController>().SetCanControl(false);
        playerInteractionController.enabled = false;
        _drawableLine.EnterDrawMode(playerInteractionController.GetComponent<PlayerPickupController>());
        UIController.Instance.ShowBackButton(() => ExitDrawMode(playerInteractionController));
        GetComponent<HighlightEffect>().enabled = false;
    }

    void ExitDrawMode(PlayerInteractionController playerInteractionController)
    {
        playerInteractionController.GetComponent<PlayerMovementController>().SetCanControl(true);
        _drawableLine.ExitDrawMode();
        playerInteractionController.enabled = true;
        UIController.Instance.HideBackButton();
    }
}
