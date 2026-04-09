using UnityEngine;

public class FolderItem : PickableObject
{
    public FolderController insideThisFolder;

    public virtual void AddToFolder(FolderController folderController)
    {
        insideThisFolder = folderController;
        SetInteractable(folderController.IsOpen);
    }
    
    public virtual void RemovePromFolder()
    {
        insideThisFolder.RemoveDocument(this, playerPickupController);
    }
    
    public override void OnEquipped(PlayerPickupController player)
    {
        base.OnEquipped(player);
        if (insideThisFolder != null)
        {
            RemovePromFolder();
        }
    }
}
