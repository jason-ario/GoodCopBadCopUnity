using Unity.Netcode;
using UnityEngine;
using UnityEngine.WSA;

public class FolderItem : PickableObject
{
    public FolderController insideThisFolder;

    /// <summary>
    /// Prevents the document from becoming interactable while it is inside a folder that
    /// is either held by any player or currently closed. Documents should only be
    /// interactable when the folder is open and not held.
    /// Every code path that tries to re-enable a document (e.g. the base-class
    /// OnHoldingClientChanged callback, or the holder-clear that fires when a notebook
    /// releases a page) goes through this method, so a single guard here is sufficient.
    /// </summary>
    public override void SetInteractable(bool value)
    {
        // Block re-enabling when inside a folder that is held OR closed.
        if (value && insideThisFolder != null && (insideThisFolder.IsHeld || !insideThisFolder.IsOpen))
            return;

        base.SetInteractable(value);
    }

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
            return;
        }

        // insideThisFolder is only set on the client that originally called AddToFolder.
        // On any other client that picks up this document, resolve the folder from the
        // active SocketFollow target so we can still broadcast ClearSocketFollow to everyone.
        SocketFollow socketFollow = GetComponent<SocketFollow>();
        if (socketFollow != null && socketFollow.Target != null)
        {
            FolderController folder = socketFollow.Target.GetComponentInParent<FolderController>();
            if (folder != null)
                folder.UnregisterDocumentServerRpc(new NetworkObjectReference(NetworkObject));
        }
    }
}
