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
   [SerializeField] private AudioSource smokingAudioSource;
   [SerializeField] private AudioClip smokingSound;

   public override void OnStartUse()
   {
      base.OnStartUse();
      playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);
      PlaySmokingSound();
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
      StopSmokingSound();
   }

   void Update()
   {
      if (!isUsing) return;
      
      PlayerInstance.Instance.Heal(healAmount);
      PlayerInstance.Instance.PlayerRadiation.AddRadiation(radiationAmount);
      float blendShapeWeight = _skinnedMeshRenderer.GetBlendShapeWeight(0) + reductionAmountPerFrame * Time.deltaTime;
         
      if (blendShapeWeight >= 100)
      {
         StopSmokingSound();
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
      StopSmokingSound();
   }

   /// <summary>Starts looping the smoking sound effect.</summary>
   private void PlaySmokingSound()
   {
      if (smokingAudioSource == null || smokingSound == null) return;

      smokingAudioSource.clip = smokingSound;
      smokingAudioSource.loop = true;
      smokingAudioSource.Play();
   }

   /// <summary>Stops the looping smoking sound effect.</summary>
   private void StopSmokingSound()
   {
      if (smokingAudioSource == null || !smokingAudioSource.isPlaying) return;

      smokingAudioSource.Stop();
   }
}
