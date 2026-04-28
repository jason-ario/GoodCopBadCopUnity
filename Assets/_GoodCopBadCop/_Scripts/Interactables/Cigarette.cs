using UnityEngine;

public class Cigarette : PickableObject
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
