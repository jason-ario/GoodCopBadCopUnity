using HighlightPlus;
using TMPro;
using UnityEngine;

public class ApplicationLetter : FolderItem
{
    [SerializeField] private TextMeshPro nameText;
    [SerializeField] private TextMeshPro birthDateText;
    [SerializeField] private TextMeshPro sexText;
    [SerializeField] private TextMeshPro reasonForEntryText;
    private FolderController insideThisFolder;
    
    public void SetInfo(SuspectData suspectData)
    {
        nameText.text = suspectData.FirstName + " " + suspectData.LastName;
        birthDateText.text = suspectData.DateOfBirth;
        sexText.text = suspectData.Sex;
        reasonForEntryText.text =
            suspectData.reasonsForEntry[UnityEngine.Random.Range(0, suspectData.reasonsForEntry.Length)];

        //Change fonts
        nameText.font = suspectData.handwritingFont;
        reasonForEntryText.font = suspectData.handwritingFont;
        birthDateText.font = suspectData.handwritingFont;
        sexText.font = suspectData.handwritingFont;
    }

    public void SetInsideFolder(FolderController folder)
    {
        insideThisFolder = folder;
    }
    
    public override void OnEquipped(PlayerPickupController player)
    {
        base.OnEquipped(player);
      
        if (insideThisFolder)
        {
            insideThisFolder.RemoveDocument(this, player);
        }
    }
}
