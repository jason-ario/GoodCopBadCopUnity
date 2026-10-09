using Unity.Netcode;
using UnityEngine;

public class FolderItem : PickableObject
{
    public FolderController insideThisFolder;

    private const ulong NoPaperworkSuspect = ulong.MaxValue;

    /// <summary>
    /// NetworkObjectId of the suspect this document was issued for (ID card / Application).
    /// <see cref="NoPaperworkSuspect"/> for documents that do not belong to a suspect (exam
    /// pages, evidence). Networked so every client's trash-can check reads the same owner.
    /// </summary>
    private readonly NetworkVariable<ulong> _paperworkSuspectId = new(NoPaperworkSuspect);

    /// <summary>Server-only. Records which suspect this document belongs to.</summary>
    public void SetPaperworkSuspectServer(SuspectCharacter suspect)
    {
        if (!IsServer) return;
        _paperworkSuspectId.Value = suspect != null && suspect.NetworkObject != null
            ? suspect.NetworkObject.NetworkObjectId
            : NoPaperworkSuspect;
    }

    /// <summary>
    /// True while this document belongs to the suspect currently at the booth window and their
    /// verdict has not been delivered yet. Such documents can't be thrown away. Once the suspect
    /// is gone (despawned, replaced, turned full mutant) or their verdict is closed, it's false.
    /// </summary>
    public bool BelongsToUnresolvedSuspectAtWindow()
    {
        ulong suspectId = _paperworkSuspectId.Value;
        if (suspectId == NoPaperworkSuspect) return false;

        SuspectController controller = SuspectController.Instance;
        SuspectCharacter current = controller != null ? controller.CurrentSuspect : null;
        if (current == null || current.NetworkObject == null) return false;
        if (current.NetworkObject.NetworkObjectId != suspectId) return false;

        return !current.IsVerdictClosed && !current.IsMutantInteractionClosed;
    }

    /// <summary>
    /// Prevents the document from becoming interactable unless it is inside a folder that is
    /// both open AND not currently held by any player. Filed documents (ID card, Application,
    /// exam pages) should only ever be grabbable when the folder holding them has been placed
    /// down and opened — never while someone is carrying the folder around, open or not.
    /// Every code path that tries to re-enable a document (e.g. the base-class
    /// OnHoldingClientChanged callback, or the holder-clear that fires when a notebook
    /// releases a page) goes through this method, so a single guard here is sufficient.
    /// This only governs the base class's raycast-interaction markers (e.g. an exam page's
    /// InteractableCollider) — the document's own physical root collider(s) are governed
    /// separately and directly by <see cref="RefreshFolderState"/>, which is the single
    /// source of truth FolderController calls for every add/open/close/hold/drop event.
    /// </summary>
    public override void SetInteractable(bool value)
    {
        // Block re-enabling unless inside a folder that is both open and not held.
        if (value && insideThisFolder != null &&
            (!insideThisFolder.IsOpen || insideThisFolder.IsHeld))
            return;

        base.SetInteractable(value);
    }

    /// <summary>
    /// The single, simple rule for a filed document's own root physical collider(s) and its
    /// raycast-interaction state, recomputed fresh from the folder's current live state:
    /// • Folder open AND not held  → collider(s) enabled as triggers (grabbable, never
    ///   physically shoves the carrying player or collides with the folder body).
    /// • Folder closed, OR held    → collider(s) disabled entirely (can't block raycasts
    ///   aimed at a closed folder, can't physically collide with anything while carried).
    /// Called directly by FolderController whenever a document is added, or the folder's
    /// open/closed or held/dropped state changes — no separate NetworkVariable round-trip
    /// or indirection, so there is nothing else that can clobber or race this state.
    /// </summary>
    public void RefreshFolderState()
    {
        if (insideThisFolder == null) return;

        bool interactable = insideThisFolder.IsOpen && !insideThisFolder.IsHeld;

        foreach (Collider col in GetComponents<Collider>())
        {
            col.isTrigger = true;
            col.enabled = interactable;
        }

        SetInteractable(interactable);
    }

    public virtual void AddToFolder(FolderController folder)
    {
        insideThisFolder = folder;
        folder.documents.Add(this);
        RefreshFolderState();
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
