using TMPro;
using UnityEngine;
using UnityEngine.WSA;

public class IDCard : FolderItem
{
   [SerializeField] private TextMeshPro nameText;
   [SerializeField] private TextMeshPro birthDateText;
   [SerializeField] private TextMeshPro expDateText;
   [SerializeField] TextMeshPro idNoText;
   [SerializeField] private TextMeshPro residentText;
   [SerializeField] private GameObject seal;
   [SerializeField] private MeshRenderer idPhoto;
   
   public void SetInfo(SuspectCharacter suspectCharacter)
   {
      nameText.text = suspectCharacter.Data.FirstName + " " + suspectCharacter.Data.LastName;
      birthDateText.text = suspectCharacter.Data.DateOfBirth;
      expDateText.text = suspectCharacter.Data.EntryPermitExpiryDate;
      idNoText.text = suspectCharacter.Data.IDNumber;
      idPhoto.material.mainTexture = suspectCharacter.Data.IDPhoto;
      bool isResident = suspectCharacter.Data.IsResident;
      residentText.text = isResident ? "* Resident of Saplavi *" : "Non-Resident";
      seal.SetActive(isResident);
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
