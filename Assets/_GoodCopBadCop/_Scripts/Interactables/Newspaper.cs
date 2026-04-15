using UnityEngine;

public class Newspaper : PickableObject
{
    public override void OnStartUse()
    {
        base.OnStartUse();
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);
    }
    
    public override void OnStopUse()
    {
        base.OnStopUse();
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
    }
}
