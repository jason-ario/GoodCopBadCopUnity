using TMPro;
using Unity.Netcode;
using UnityEngine;

public class IDCard : FolderItem
{
   [SerializeField] private TextMeshPro nameText;
   [SerializeField] private TextMeshPro birthDateText;
   [SerializeField] private TextMeshPro expDateText;
   [SerializeField] TextMeshPro idNoText;
   [SerializeField] private TextMeshPro residentText;
   [SerializeField] private GameObject seal;
   [SerializeField] private MeshRenderer idPhoto;

   /// <summary>
   /// Populates the card locally (host) and broadcasts all field values to clients.
   /// Must be called after Spawn() so the ClientRpc can be delivered.
   /// </summary>
   public void SetInfo(SuspectCharacter suspectCharacter)
   {
      ApplyInfo(
         suspectCharacter.Data.FirstName + " " + suspectCharacter.Data.LastName,
         suspectCharacter.Data.DateOfBirth,
         suspectCharacter.Data.EntryPermitExpiryDate,
         suspectCharacter.Data.IDNumber,
         suspectCharacter.Data.IsResident,
         suspectCharacter.GetComponent<NetworkObject>()
      );

      SyncToClientsClientRpc(
         suspectCharacter.Data.FirstName + " " + suspectCharacter.Data.LastName,
         suspectCharacter.Data.DateOfBirth,
         suspectCharacter.Data.EntryPermitExpiryDate,
         suspectCharacter.Data.IDNumber,
         suspectCharacter.Data.IsResident,
         suspectCharacter.GetComponent<NetworkObject>()
      );
   }

   /// <summary>Applies all display values locally, resolving the ID photo from the suspect NetworkObject.</summary>
   private void ApplyInfo(string fullName, string dob, string expiry, string idNumber, bool isResident, NetworkObjectReference suspectRef)
   {
      nameText.text = fullName;
      birthDateText.text = dob;
      expDateText.text = expiry;
      idNoText.text = idNumber;
      residentText.text = isResident ? "* Resident of Saplavi *" : "Non-Resident";
     // seal.SetActive(isResident);

      if (suspectRef.TryGet(out NetworkObject suspectNetObj))
      {
         SuspectCharacter suspect = suspectNetObj.GetComponent<SuspectCharacter>();
         if (suspect != null)
            idPhoto.material.mainTexture = suspect.Data.IDPhoto;
      }
   }

   [ClientRpc]
   private void SyncToClientsClientRpc(string fullName, string dob, string expiry, string idNumber, bool isResident, NetworkObjectReference suspectRef)
   {
      // Host already applied values in SetInfo; skip to avoid double-apply.
      if (IsServer) return;
      ApplyInfo(fullName, dob, expiry, idNumber, isResident, suspectRef);
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
