using Unity.Netcode;
using UnityEngine;

public class FolderItem : PickableObject
{
    public FolderController insideThisFolder;

    /// <summary>
    /// Whether re-enabling should be blocked while the owning folder is currently held by a
    /// player (in addition to always being blocked while the folder is closed). True for
    /// ID card / Application (you should not be able to grab those out of a folder someone
    /// is actively carrying), but overridden to false by <see cref="ExamPage"/>, which must
    /// stay grabbable/interactable even while the folder is held — that's the normal flow of
    /// carrying an open folder around while filing/rearranging exam pages in it.
    /// </summary>
    protected virtual bool BlockInteractableWhileFolderHeld => true;

    /// <summary>
    /// Prevents the document from becoming interactable while it is inside a folder that
    /// is either held by any player (see <see cref="BlockInteractableWhileFolderHeld"/>) or
    /// currently closed. Documents should only be
    /// interactable when the folder is open and not held.
    /// Every code path that tries to re-enable a document (e.g. the base-class
    /// OnHoldingClientChanged callback, or the holder-clear that fires when a notebook
    /// releases a page) goes through this method, so a single guard here is sufficient.
    /// </summary>
    public override void SetInteractable(bool value)
    {
        // Block re-enabling when inside a folder that is closed, or (for subclasses that
        // opt in via BlockInteractableWhileFolderHeld) currently held.
        if (value && insideThisFolder != null &&
            ((BlockInteractableWhileFolderHeld && insideThisFolder.IsHeld) || !insideThisFolder.IsOpen))
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
