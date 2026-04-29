using UnityEngine;

public class Cigarette : PickableObject
{
   [SerializeField] float healAmount = 1;
   [SerializeField] float radiationAmount = 1;
   public override void OnStartUse()
   {
      base.OnStartUse();
      playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);
   }
   
   void Update()
   {
      if (isUsing)
      {
         PlayerInstance.Instance.Heal(healAmount * Time.deltaTime);
         PlayerInstance.Instance.PlayerRadiation.AddRadiation(radiationAmount);
      }
   }
   
   public override void OnStopUse()
   {
      base.OnStopUse();
      playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
   }
}
