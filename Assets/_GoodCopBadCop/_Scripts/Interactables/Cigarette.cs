using GoodCopBadCop.Effects;
using UnityEngine;
using UnityEngine.Serialization;

public class Cigarette : PickableObject
{
   [SerializeField] float healAmount = 1;
   [Tooltip("Radiation added per second while smoking (frame-rate independent).")]
   [FormerlySerializedAs("radiationAmount")]
   [SerializeField] float radiationPerSecond = 0.1f;
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

   private ParticleSystem[] _smokeSystems;
   private bool _isSmokeEmitting;
   private bool _isLocallyEquipped;

   protected override void Awake()
   {
      base.Awake();
      CacheEmissiveColor();
      InitSmoke();
   }

   /// <summary>LMB (held) smokes the cigarette.</summary>
   public override string GetHeldUseVerb() => Loc.N("Smoke");

   public override void OnStartUse()
   {
      base.OnStartUse();
      playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);
      PlaySmokingSound();
      SetSmokingNearClipPlaneActive(true);
   }

   public override void OnEquipped(PlayerPickupController player)
   {
      base.OnEquipped(player);
      _isLocallyEquipped = true;
      RefreshSmoke();
   }

   public override void OnUnequip(PlayerPickupController player)
   {
      base.OnUnequip(player);
      _isLocallyEquipped = false;
      RefreshSmoke();
      StopSmokingSound();
      SnapLightOff();
      SnapEmissionOff();
      SetSmokingNearClipPlaneActive(false);
   }

   void Update()
   {
      RefreshSmoke();
      UpdateEmberLight();
      UpdateEmissiveFade();

      if (!isUsing) return;

      PlayerInstance.Instance.Heal(healAmount, EffectKeys.CigaretteHeal);
      PlayerInstance.Instance.PlayerRadiation.AddRadiation(radiationPerSecond * Time.deltaTime);
      float blendShapeWeight = _skinnedMeshRenderer.GetBlendShapeWeight(0) + reductionAmountPerFrame * Time.deltaTime;

      if (blendShapeWeight >= 100)
      {
         StopSmokingSound();
         playerPickupController.DestroyEquippedItem();
         return;
      }

      _skinnedMeshRenderer.SetBlendShapeWeight(0, blendShapeWeight);

      if (particles != null && particlePos1 != null && particlePos2 != null)
         particles.transform.position = Vector3.Lerp(particlePos1.position, particlePos2.position, blendShapeWeight / 100f);
   }

   /// <summary>
   /// Smoke must start off on every peer. Before this, the emitter played on awake, so remote
   /// clients (which never get OnEquipped/OnUnequip for someone else's cigarette) smoked forever.
   /// </summary>
   private void InitSmoke()
   {
      if (particles == null) return;

      _smokeSystems = particles.GetComponentsInChildren<ParticleSystem>(true);
      foreach (ParticleSystem ps in _smokeSystems)
      {
         ParticleSystem.MainModule main = ps.main;
         main.playOnAwake = false;
         ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
      }

      _isSmokeEmitting = false;
   }

   /// <summary>
   /// Smoke while held. Uses the replicated holder (IsHeld) so every peer agrees, plus the
   /// local equip flag so the holder sees it instantly. Runs every frame, so it also recovers
   /// after stow/unstow and late joins.
   /// </summary>
   private void RefreshSmoke()
   {
      if (_smokeSystems == null || _smokeSystems.Length == 0) return;

      bool shouldEmit = _isLocallyEquipped || (IsSpawned && IsHeld);
      if (shouldEmit == _isSmokeEmitting) return;
      _isSmokeEmitting = shouldEmit;

      if (shouldEmit)
      {
         if (!particles.activeSelf) particles.SetActive(true);
         foreach (ParticleSystem ps in _smokeSystems) ps.Play(false);
      }
      else
      {
         // Stop emitting but let live puffs fade out over their short lifetime.
         foreach (ParticleSystem ps in _smokeSystems) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
      }
   }

   public override void OnStopUse()
   {
      base.OnStopUse();
      playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
      StopSmokingSound();
      SetSmokingNearClipPlaneActive(false);
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

   /// <summary>Tightens (or restores) the player's render camera near clip plane while smoking.</summary>
   private void SetSmokingNearClipPlaneActive(bool isSmoking)
   {
      if (playerPickupController == null) return;

      PlayerCameraController cameraController = playerPickupController.GetComponent<PlayerCameraController>();
      if (cameraController == null) return;

      cameraController.SetNearClipPlaneTightened(isSmoking);
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
