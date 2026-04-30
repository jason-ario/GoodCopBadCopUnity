using UnityEngine;

public class Cigarette : PickableObject
{
   [SerializeField] float healAmount = 1;
   [SerializeField] float radiationAmount = 1;
   [SerializeField] private GameObject particles;
   [SerializeField] SkinnedMeshRenderer _skinnedMeshRenderer;
   [SerializeField] float reductionAmountPerFrame = .01f;
   [SerializeField] private Transform particlePos1;
   [SerializeField] private Transform particlePos2;
   
   public override void OnStartUse()
   {
      base.OnStartUse();
      playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);
   }

   public override void OnEquipped(PlayerPickupController player)
   {
      base.OnEquipped(player);
      particles.SetActive(true);
   }
   
   public override void OnUnequip(PlayerPickupController player)
   {
      base.OnUnequip(player);
      
      particles.SetActive(false);
   }

   void Update()
   {
      if (!isUsing) return;
      
      PlayerInstance.Instance.Heal(healAmount * Time.deltaTime);
      PlayerInstance.Instance.PlayerRadiation.AddRadiation(radiationAmount);
      float blendShapeWeight = _skinnedMeshRenderer.GetBlendShapeWeight(0) + reductionAmountPerFrame * Time.deltaTime;
         
      if (blendShapeWeight >= 100)
      {
         playerPickupController.DestroyEquippedItem();
         return;
      }
      
      _skinnedMeshRenderer.SetBlendShapeWeight(0, blendShapeWeight);
         
      particles.transform.position = Vector3.Lerp(particlePos1.position, particlePos2.position, (100 / blendShapeWeight));
   }
   
   public override void OnStopUse()
   {
      base.OnStopUse();
      playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
   }
}
