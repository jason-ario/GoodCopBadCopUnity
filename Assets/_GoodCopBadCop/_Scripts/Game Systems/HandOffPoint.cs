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

    /// <summary>
    /// Sets the deferred verdict folder on THIS process's static state. Only meaningful when
    /// called on the server — see <see cref="OnPlaced"/> for why a server-side write is
    /// required regardless of which client performed the placement.
    /// </summary>
    public static void SetPendingVerdictFolder(FolderController folder)
    {
        PendingVerdictFolder = folder;
    }

    public override void OnPlaced(PickableObject pickableObject)
    {
        base.OnPlaced(pickableObject);

        FolderController folderController = pickableObject.GetComponent<FolderController>();
        if (folderController == null) return;

        // Do NOT gate on SuspectController.Instance.CurrentSuspect here. CurrentSuspect is set
        // synchronously on the server but only arrives on non-host clients asynchronously via
        // AssignReferencesClientRpc → WaitForSpawnAndAssign. If a non-host player places the
        // folder before that RPC has resolved on their machine, this local check would silently
        // return and DeliverVerdict would never even be called — the handoff would appear to do
        // nothing for that player while working fine for the host. The server always has an
        // up-to-date suspectCharacter reference, so the "is there a suspect at the window" guard
        // is enforced authoritatively in SuspectController.ExecuteVerdict instead.

        if (!folderController.IsStamped) return;

        if (BlockVerdict)
        {
            // Store for deferred delivery — do not call DeliverVerdict now.
            // BlockVerdict/PendingVerdictFolder are per-process statics: DayActivated sets
            // BlockVerdict on every client independently, but the eventual deferred delivery
            // (Day_01.DeliverDeferredVerdict, invoked from a server-only dialogue callback)
            // only ever reads PendingVerdictFolder on the SERVER. If Player 2 (a non-host
            // client) is the one who places the folder, this line alone only sets Player 2's
            // own local static — the server's copy stays null and the deferred verdict is
            // silently dropped, even though everything appears to work locally for Player 2.
            // NotifyPendingVerdictHandoff routes the folder reference to the server (via RPC
            // when called on a non-host client) so the server's static is always populated,
            // regardless of who placed the folder.
            PendingVerdictFolder = folderController;
            folderController.NotifyPendingVerdictHandoff();
            return;
        }

        SuspectController.Instance.DeliverVerdict(folderController);
    }
}
