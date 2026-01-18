using UnityEngine;

public class InvitationLetter : PickableObject
{
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

}
