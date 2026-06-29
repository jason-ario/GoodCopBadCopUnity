using UnityEngine;

public class HandOffPoint : PlacementBoard
{
    /// <summary>
    /// When true, folder placement does not immediately call
    /// <see cref="SuspectController.DeliverVerdict"/>. Instead the folder is stored in
    /// <see cref="PendingVerdictFolder"/> so a deferred caller (e.g. a cutscene) can
    /// deliver the verdict at the right moment.
    /// Reset to false by whoever sets it once the deferred call is made.
    /// </summary>
    public static bool BlockVerdict { get; set; }

    /// <summary>The stamped folder held back by <see cref="BlockVerdict"/>.</summary>
    public static FolderController PendingVerdictFolder { get; private set; }

    /// <summary>Clears the deferred verdict state. Call after the verdict has been delivered or abandoned.</summary>
    public static void ClearPendingVerdict()
    {
        BlockVerdict = false;
        PendingVerdictFolder = null;
    }

    public override void OnPlaced(PickableObject pickableObject)
    {
        base.OnPlaced(pickableObject);

        FolderController folderController = pickableObject.GetComponent<FolderController>();
        if (folderController == null) return;
        if (SuspectController.Instance.CurrentSuspect == null) return;

        if (!folderController.IsStamped) return;

        if (BlockVerdict)
        {
            // Store for deferred delivery — do not call DeliverVerdict now.
            PendingVerdictFolder = folderController;
            return;
        }

        SuspectController.Instance.DeliverVerdict(folderController);
    }
}
