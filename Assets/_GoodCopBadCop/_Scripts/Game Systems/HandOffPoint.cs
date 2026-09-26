using System.Collections.Generic;
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

    [Tooltip("How far (meters) a folder may sit from this board's collider and still count as resting on it. " +
             "Used for the position-based fallbacks (stamping a folder already at the window, or a drop whose " +
             "aim raycast grazed the frame next to the board instead of the board itself).")]
    [SerializeField] private float restingTolerance = 0.2f;

    private static readonly List<HandOffPoint> _activePoints = new List<HandOffPoint>();
    private Collider[] _colliders;

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

    private void Awake()
    {
        _colliders = GetComponents<Collider>();
        if (_colliders.Length == 0)
        {
            Collider parentCol = GetComponentInParent<Collider>();
            _colliders = parentCol != null ? new[] { parentCol } : new Collider[0];
        }
    }

    private void OnEnable() => _activePoints.Add(this);
    private void OnDisable() => _activePoints.Remove(this);

    /// <summary>True if <paramref name="worldPos"/> is inside / within tolerance of this board's collider(s).</summary>
    private bool IsPositionOnBoard(Vector3 worldPos)
    {
        foreach (Collider col in _colliders)
        {
            if (col == null || !col.enabled) continue;
            if (Vector3.Distance(worldPos, col.ClosestPoint(worldPos)) <= restingTolerance)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns the active HandOffPoint at <paramref name="worldPos"/>, or null. Used by the drop
    /// path as a fallback when the placement raycast resolved no board (aim grazed the window
    /// frame/ledge next to the board's collider) so the hand-off still registers first time.
    /// </summary>
    public static HandOffPoint FindAt(Vector3 worldPos)
    {
        foreach (HandOffPoint point in _activePoints)
        {
            if (point != null && point.IsPositionOnBoard(worldPos))
                return point;
        }
        return null;
    }

    /// <summary>
    /// Called on the stamping player's client once a folder's stamp sequence completes. If the
    /// folder was stamped while already resting on a hand-off point, its earlier placement was
    /// (correctly) rejected as unstamped and nothing would otherwise re-check it — the player had
    /// to pick it up and put it back down. This re-runs the placement for that case.
    /// </summary>
    public static void TryHandOffRestingFolder(FolderController folder)
    {
        if (folder == null || folder.IsHeld || folder.IsHandedOff || !folder.IsStamped) return;

        HandOffPoint point = FindAt(folder.transform.position);
        if (point == null) return;

        Debug.Log($"[HandOffPoint] Folder {folder.name} was stamped while resting on {point.name} — registering hand-off.");
        point.OnPlaced(folder);
    }

    public override void OnPlaced(PickableObject pickableObject)
    {
        FolderController folderController = pickableObject.GetComponent<FolderController>();
        if (folderController == null) return;

        // Guard against a double hand-off (e.g. the drop fallback and the stamp-complete
        // fallback both resolving the same folder).
        if (folderController.IsHandedOff) return;

        // Do NOT gate on SuspectController.Instance.CurrentSuspect here. CurrentSuspect is set
        // synchronously on the server but only arrives on non-host clients asynchronously via
        // AssignReferencesClientRpc → WaitForSpawnAndAssign. If a non-host player places the
        // folder before that RPC has resolved on their machine, this local check would silently
        // return and DeliverVerdict would never even be called — the handoff would appear to do
        // nothing for that player while working fine for the host. The server always has an
        // up-to-date suspectCharacter reference, so the "is there a suspect at the window" guard
        // is enforced authoritatively in SuspectController.ExecuteVerdict instead.

        // Gate BEFORE firing OnItemPlaced (via base.OnPlaced): tutorial/task listeners
        // subscribe to PlacementBoard.OnItemPlaced on this board and treat any fire as
        // "the folder was handed off", advancing the sequence. If an unstamped folder is
        // placed here, we must not fire that event at all — otherwise placing the folder
        // early (before stamping) incorrectly triggers the next tutorial step even though
        // no verdict is ever delivered. If it is stamped later while still resting here,
        // TryHandOffRestingFolder re-runs this placement.
        if (!folderController.IsStamped) return;

        base.OnPlaced(pickableObject);

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
