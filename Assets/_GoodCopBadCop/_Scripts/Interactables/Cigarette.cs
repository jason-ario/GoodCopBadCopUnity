using GoodCopBadCop.Effects;
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
   [SerializeField] private Light emberLight;
   [SerializeField] float minLightIntensity = 0f;
   [SerializeField] float maxLightIntensity = 1.5f;
   [SerializeField] float lightFadeSpeed = 3f;
   [SerializeField] int emissiveMaterialIndex = 1;
   [SerializeField] float emissionFadeSpeed = 3f;

   private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
   private MaterialPropertyBlock _emissionPropertyBlock;
   private Color _emissionOnColor;
   private float _currentEmissionT;

   protected override void Awake()
   {
      base.Awake();
      CacheEmissiveColor();
   }

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
      SnapLightOff();
      SnapEmissionOff();
   }

   void Update()
   {
      UpdateEmberLight();
      UpdateEmissiveFade();

      if (!isUsing) return;

      PlayerInstance.Instance.Heal(healAmount, EffectKeys.CigaretteHeal);
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

   /// <summary>Fades the ember light in when smoking starts and out when it stops.</summary>
   private void UpdateEmberLight()
   {
      if (emberLight == null) return;

      float targetIntensity = isUsing ? maxLightIntensity : minLightIntensity;
      emberLight.intensity = Mathf.MoveTowards(emberLight.intensity, targetIntensity, lightFadeSpeed * Time.deltaTime);
      emberLight.enabled = emberLight.intensity > 0.001f;
   }

   /// <summary>Immediately turns the ember light off, used when the item is put away.</summary>
   private void SnapLightOff()
   {
      if (emberLight == null) return;

      emberLight.intensity = minLightIntensity;
      emberLight.enabled = false;
   }

   /// <summary>Caches the emissive material's original emission color so it can be faded from black.</summary>
   private void CacheEmissiveColor()
   {
      if (_skinnedMeshRenderer == null) return;

      Material[] sharedMaterials = _skinnedMeshRenderer.sharedMaterials;
      if (emissiveMaterialIndex < 0 || emissiveMaterialIndex >= sharedMaterials.Length) return;

      Material emissiveMaterial = sharedMaterials[emissiveMaterialIndex];
      if (emissiveMaterial == null || !emissiveMaterial.HasProperty(EmissionColorId)) return;

      _emissionOnColor = emissiveMaterial.GetColor(EmissionColorId);
      _emissionPropertyBlock = new MaterialPropertyBlock();
   }

   /// <summary>Fades the emissive material's emission color in when smoking starts and out when it stops.</summary>
   private void UpdateEmissiveFade()
   {
      if (_skinnedMeshRenderer == null || _emissionPropertyBlock == null) return;

      float targetT = isUsing ? 1f : 0f;
      _currentEmissionT = Mathf.MoveTowards(_currentEmissionT, targetT, emissionFadeSpeed * Time.deltaTime);
      ApplyEmissionColor(Color.Lerp(Color.black, _emissionOnColor, _currentEmissionT));
   }

   /// <summary>Immediately turns the emissive material's emission off, used when the item is put away.</summary>
   private void SnapEmissionOff()
   {
      if (_skinnedMeshRenderer == null || _emissionPropertyBlock == null) return;

      _currentEmissionT = 0f;
      ApplyEmissionColor(Color.black);
   }

   private void ApplyEmissionColor(Color emissionColor)
   {
      _skinnedMeshRenderer.GetPropertyBlock(_emissionPropertyBlock, emissiveMaterialIndex);
      _emissionPropertyBlock.SetColor(EmissionColorId, emissionColor);
      _skinnedMeshRenderer.SetPropertyBlock(_emissionPropertyBlock, emissiveMaterialIndex);
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
