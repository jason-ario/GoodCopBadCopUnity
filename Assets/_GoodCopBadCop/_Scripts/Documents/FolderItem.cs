using Unity.Netcode;
using UnityEngine;
using UnityEngine.WSA;

public class FolderItem : PickableObject
{
    public FolderController insideThisFolder;

    public virtual void AddToFolder(FolderController folder)
    {
        insideThisFolder = folder;
        folder.documents.Add(this);
        SetInteractable(folder.IsOpen);
    }
    
    public virtual void RemovePromFolder()
    {
        insideThisFolder.RemoveDocument(this, playerPickupController);
        insideThisFolder.documents.Remove(this);
        insideThisFolder = null;
    }
    
    public override void OnEquipped(PlayerPickupController player)
    {
        base.OnEquipped(player);
        if (insideThisFolder != null)
        {
            insideThisFolder.UnregisterDocumentServerRpc(new NetworkObjectReference(NetworkObject));
            RemovePromFolder();
        }
    }
}
