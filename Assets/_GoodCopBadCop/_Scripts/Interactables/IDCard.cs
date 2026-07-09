using TMPro;
using GoodCopBadCop.SuspectPaperwork;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class IDCard : FolderItem
{
   [SerializeField] private TextMeshPro nameText;
   [SerializeField] private TextMeshPro birthDateText;
   [SerializeField] private TextMeshPro expDateText;
   [SerializeField] TextMeshPro idNoText;
   [SerializeField] private TextMeshPro residentText;
   [SerializeField] private GameObject seal;
   [SerializeField] private SpriteRenderer sealRenderer;
   [SerializeField] private MeshRenderer idPhoto;
   [SerializeField] private MeshRenderer cardSurfaceRenderer;
   [SerializeField] private Texture defaultCardTexture;
   [SerializeField] private Texture fakeCardTexture;
   [SerializeField] private Sprite defaultSealSprite;
   [SerializeField] private Sprite fakeSealSprite;

   private readonly NetworkVariable<FixedString512Bytes> syncedFullName = new(new FixedString512Bytes(string.Empty));
   private readonly NetworkVariable<FixedString512Bytes> syncedBirthDate = new(new FixedString512Bytes(string.Empty));
   private readonly NetworkVariable<FixedString512Bytes> syncedExpiry = new(new FixedString512Bytes(string.Empty));
   private readonly NetworkVariable<FixedString512Bytes> syncedIdNumber = new(new FixedString512Bytes(string.Empty));
   private readonly NetworkVariable<bool> syncedIsResident = new(false);
   private readonly NetworkVariable<bool> syncedIsFakeId = new(false);
   private readonly NetworkVariable<NetworkObjectReference> syncedSuspectRef = new();

   private const string BaseMapProperty = "_BaseMap";
   private const string MainTexProperty = "_MainTex";

   public override void OnNetworkSpawn()
   {
      base.OnNetworkSpawn();
      CacheDefaultVisuals();

      syncedFullName.OnValueChanged += OnFullNameChanged;
      syncedBirthDate.OnValueChanged += OnBirthDateChanged;
      syncedExpiry.OnValueChanged += OnExpiryChanged;
      syncedIdNumber.OnValueChanged += OnIdNumberChanged;
      syncedIsResident.OnValueChanged += OnResidentChanged;
      syncedIsFakeId.OnValueChanged += OnFakeIdChanged;
      syncedSuspectRef.OnValueChanged += OnSuspectRefChanged;

      ApplySyncedTextState();
      ApplyIdVisualState(syncedIsFakeId.Value, syncedIsResident.Value);
      ApplyPhotoFromSuspectRef(syncedSuspectRef.Value);
   }

   public override void OnNetworkDespawn()
   {
      syncedFullName.OnValueChanged -= OnFullNameChanged;
      syncedBirthDate.OnValueChanged -= OnBirthDateChanged;
      syncedExpiry.OnValueChanged -= OnExpiryChanged;
      syncedIdNumber.OnValueChanged -= OnIdNumberChanged;
      syncedIsResident.OnValueChanged -= OnResidentChanged;
      syncedIsFakeId.OnValueChanged -= OnFakeIdChanged;
      syncedSuspectRef.OnValueChanged -= OnSuspectRefChanged;

      base.OnNetworkDespawn();
   }

   /// <summary>
   /// Populates the card locally (host) and stores all field values in NetworkVariables.
   /// Must be called after Spawn() so late-joining clients receive the same state.
   /// </summary>
   public void SetInfo(SuspectCharacter suspectCharacter)
   {
      SetPaperworkState(
         new SuspectPaperworkState(
            suspectCharacter.Data.FirstName + " " + suspectCharacter.Data.LastName,
            suspectCharacter.Data.DateOfBirth,
            suspectCharacter.Data.Sex,
            suspectCharacter.Data.IDNumber,
            suspectCharacter.Data.FirstName + " " + suspectCharacter.Data.LastName,
            suspectCharacter.Data.DateOfBirth,
            suspectCharacter.Data.Sex,
            suspectCharacter.Data.IDNumber,
            suspectCharacter.Data.EntryPermitExpiryDate,
            string.Empty,
            suspectCharacter.Data.EntryPermitExpiryDate,
            suspectCharacter.Data.IsResident,
            true,
            true,
            false,
            suspectCharacter.IDPhoto),
         suspectCharacter);
   }

   public void SetPaperworkState(SuspectPaperworkState state, SuspectCharacter suspectCharacter)
   {
      if (!IsServer)
         return;

      syncedFullName.Value = ToFixedString(state.FullName);
      syncedBirthDate.Value = ToFixedString(state.BirthDate);
      syncedExpiry.Value = ToFixedString(state.ExpirationDate);
      syncedIdNumber.Value = ToFixedString(state.IdNumber);
      syncedIsResident.Value = state.IsResident;
      syncedIsFakeId.Value = state.IsFakeId;

      if (suspectCharacter != null && suspectCharacter.TryGetComponent(out NetworkObject suspectNetworkObject))
         syncedSuspectRef.Value = new NetworkObjectReference(suspectNetworkObject);

      ApplySyncedTextState();
      ApplyIdVisualState(state.IsFakeId, state.IsResident);
      if (state.IdPhoto != null)
         idPhoto.material.mainTexture = state.IdPhoto;
   }

   /// <summary>Editor preview path: applies state without touching NGO state.</summary>
   public void ApplyPreviewState(SuspectPaperworkState state)
   {
      nameText.text = state.FullName;
      birthDateText.text = state.BirthDate;
      expDateText.text = state.ExpirationDate;
      idNoText.text = state.IdNumber;
      residentText.text = state.IsResident ? "* Resident of Saplavi *" : "Non-Resident";
      ApplyIdVisualState(state.IsFakeId, state.IsResident);
      if (state.IdPhoto != null)
         idPhoto.material.mainTexture = state.IdPhoto;
   }

   /// <summary>Applies all display values locally, resolving the ID photo from the suspect NetworkObject.</summary>
   private void ApplySyncedTextState()
   {
      nameText.text = syncedFullName.Value.ToString();
      birthDateText.text = syncedBirthDate.Value.ToString();
      expDateText.text = syncedExpiry.Value.ToString();
      idNoText.text = syncedIdNumber.Value.ToString();
      residentText.text = syncedIsResident.Value ? "* Resident of Saplavi *" : "Non-Resident";
      ApplyIdVisualState(syncedIsFakeId.Value, syncedIsResident.Value);
   }

   private void OnFullNameChanged(FixedString512Bytes previous, FixedString512Bytes current) => nameText.text = current.ToString();
   private void OnBirthDateChanged(FixedString512Bytes previous, FixedString512Bytes current) => birthDateText.text = current.ToString();
   private void OnExpiryChanged(FixedString512Bytes previous, FixedString512Bytes current) => expDateText.text = current.ToString();
   private void OnIdNumberChanged(FixedString512Bytes previous, FixedString512Bytes current) => idNoText.text = current.ToString();
   private void OnResidentChanged(bool previous, bool current)
   {
      residentText.text = current ? "* Resident of Saplavi *" : "Non-Resident";
      ApplyIdVisualState(syncedIsFakeId.Value, current);
   }
   private void OnFakeIdChanged(bool previous, bool current) => ApplyIdVisualState(current, syncedIsResident.Value);

   private void OnSuspectRefChanged(NetworkObjectReference previous, NetworkObjectReference current)
   {
      ApplyPhotoFromSuspectRef(current);
   }

   private void ApplyPhotoFromSuspectRef(NetworkObjectReference suspectRef)
   {
      StartCoroutine(ApplyPhotoWhenReady(suspectRef));
   }

   private IEnumerator ApplyPhotoWhenReady(NetworkObjectReference suspectRef)
   {
      const int maxFramesToWait = 60;
      for (int i = 0; i < maxFramesToWait; i++)
      {
         if (suspectRef.TryGet(out NetworkObject suspectNetObj))
         {
            SuspectCharacter suspect = suspectNetObj.GetComponent<SuspectCharacter>();
            if (suspect != null && suspect.IDPhoto != null)
               idPhoto.material.mainTexture = suspect.IDPhoto;
            yield break;
         }

         yield return null;
      }
   }

   private void CacheDefaultVisuals()
   {
      MeshRenderer surfaceRenderer = GetCardSurfaceRenderer();
      if (surfaceRenderer != null && defaultCardTexture == null)
         defaultCardTexture = GetMaterialTexture(surfaceRenderer.material);

      SpriteRenderer renderer = GetSealRenderer();
      if (renderer != null && defaultSealSprite == null)
         defaultSealSprite = renderer.sprite;
   }

   private void ApplyIdVisualState(bool isFakeId, bool isResident)
   {
      CacheDefaultVisuals();

      MeshRenderer surfaceRenderer = GetCardSurfaceRenderer();
      if (surfaceRenderer != null)
         SetMaterialTexture(surfaceRenderer.material, isFakeId && fakeCardTexture != null ? fakeCardTexture : defaultCardTexture);

      SpriteRenderer renderer = GetSealRenderer();
      if (renderer != null)
         renderer.sprite = isFakeId && fakeSealSprite != null ? fakeSealSprite : defaultSealSprite;

      if (seal != null)
         seal.SetActive(isResident || isFakeId);
   }

   private MeshRenderer GetCardSurfaceRenderer()
   {
      if (cardSurfaceRenderer != null)
         return cardSurfaceRenderer;

      foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
      {
         if (renderer == idPhoto || renderer.GetComponent<TextMeshPro>() != null)
            continue;

         cardSurfaceRenderer = renderer;
         return cardSurfaceRenderer;
      }

      return null;
   }

   private SpriteRenderer GetSealRenderer()
   {
      if (sealRenderer != null)
         return sealRenderer;

      if (seal != null)
         sealRenderer = seal.GetComponent<SpriteRenderer>();

      return sealRenderer;
   }

   private static Texture GetMaterialTexture(Material material)
   {
      if (material == null)
         return null;

      if (material.HasProperty(BaseMapProperty))
         return material.GetTexture(BaseMapProperty);

      if (material.HasProperty(MainTexProperty))
         return material.GetTexture(MainTexProperty);

      return material.mainTexture;
   }

   private static void SetMaterialTexture(Material material, Texture texture)
   {
      if (material == null || texture == null)
         return;

      bool assigned = false;
      if (material.HasProperty(BaseMapProperty))
      {
         material.SetTexture(BaseMapProperty, texture);
         assigned = true;
      }

      if (material.HasProperty(MainTexProperty))
      {
         material.SetTexture(MainTexProperty, texture);
         assigned = true;
      }

      if (!assigned)
         material.mainTexture = texture;
   }

   private static FixedString512Bytes ToFixedString(string value)
   {
      const int maxCharacters = 120;
      string safeValue = value ?? string.Empty;
      if (safeValue.Length > maxCharacters)
         safeValue = safeValue.Substring(0, maxCharacters);

      return new FixedString512Bytes(safeValue);
   }

   public override void OnStartUse()
   {
      playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);
   }

   public override void OnStopUse()
   {
      playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
   }
}
