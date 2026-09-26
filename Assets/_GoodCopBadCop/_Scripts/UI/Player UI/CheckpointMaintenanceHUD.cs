using UnityEngine;

/// <summary>
/// Drives the maintenance rows inside the HUD's Checkpoint Integrity panel — three
/// <see cref="MaintenanceTaskRow"/> rows each showing a single number: how many broken fence
/// segments, pieces of trash/gore, and unscrubbed graffiti/blood pieces are currently outstanding.
///
/// Counts include items that belong to an active task as well as ones that don't:
///   - Fences: every authored segment currently broken in the world
///     (<see cref="FenceRepairTask.BrokenFenceCount"/>), not only the active repair round.
///   - Trash: <see cref="TakeOutTrashTask"/> total minus deposited (in-bounds items only).
///   - Graffiti: graffiti pieces + in-bounds blood splatters counted by <see cref="CleanBloodTask"/>,
///     minus scrubbed.
///
/// After Day 1 these cleanup categories become optional (see <see cref="CleanupTaskGating"/>):
/// they no longer occupy a slot on the mandatory "CURRENT ORDERS" task list and no longer block
/// clock-out, but they still spawn, still show compass pips, and still drag down
/// <see cref="CheckpointIntegrityService"/>'s payout multiplier.
///
/// Purely a read-side display: never registers or blocks anything, just mirrors already-networked
/// state. Refreshes on task progress events and also polls at <see cref="_pollInterval"/>, because
/// fence damage outside an active repair round raises no client-side event.
/// </summary>
public class CheckpointMaintenanceHUD : MonoBehaviour
{
    [Tooltip("Row showing the number of broken perimeter fence segments.")]
    [SerializeField] private MaintenanceTaskRow _fenceRow;

    [Tooltip("Row showing the number of trash/gore items still to deposit.")]
    [SerializeField] private MaintenanceTaskRow _trashRow;

    [Tooltip("Row showing the number of graffiti pieces + in-bounds blood splatters still to scrub.")]
    [SerializeField] private MaintenanceTaskRow _graffitiRow;

    [Tooltip("Seconds between polled refreshes (catches world changes that raise no event).")]
    [SerializeField, Min(0.05f)] private float _pollInterval = 0.25f;

    private float _pollTimer;

    /// <summary>Number of maintenance rows with outstanding items. Used for the sidebar's collapsed badge count.</summary>
    public int IncompleteCount
    {
        get
        {
            int count = 0;
            if (GetOutstandingFenceCount() > 0) count++;
            if (GetOutstandingTrashCount() > 0) count++;
            if (GetOutstandingGraffitiCount() > 0) count++;
            return count;
        }
    }

    /// <summary>
    /// Combined graffiti + counted blood progress, used for the graffiti row. Blood only contributes
    /// what <see cref="CleanBloodTask"/> has registered, which is limited to in-bounds splatters
    /// (plus any out-of-bounds splatter credited as a bonus once scrubbed).
    /// </summary>
    public static void GetGraffitiAndBloodCounts(out int scrubbed, out int total)
    {
        CleanGraffitiTask graffiti = CleanGraffitiTask.Instance;
        CleanBloodTask blood = CleanBloodTask.Instance;

        scrubbed = (graffiti != null ? graffiti.ScrubbedCount : 0) + (blood != null ? blood.ScrubbedCount : 0);
        total    = (graffiti != null ? graffiti.TotalGraffitiCount : 0) + (blood != null ? blood.TotalCount : 0);
    }

    /// <summary>Broken fence segments currently in the world (task-tracked or not).</summary>
    public static int GetOutstandingFenceCount()
    {
        FenceRepairTask fence = FenceRepairTask.Instance;
        return fence != null ? fence.BrokenFenceCount : 0;
    }

    /// <summary>Counted trash/gore items not yet deposited.</summary>
    public static int GetOutstandingTrashCount()
    {
        TakeOutTrashTask trash = TakeOutTrashTask.Instance;
        return trash != null ? Mathf.Max(0, trash.TotalCount - trash.DepositedCount) : 0;
    }

    /// <summary>Counted graffiti + blood pieces not yet scrubbed.</summary>
    public static int GetOutstandingGraffitiCount()
    {
        GetGraffitiAndBloodCounts(out int scrubbed, out int total);
        return Mathf.Max(0, total - scrubbed);
    }

    private void OnEnable()
    {
        FenceRepairTask.OnProgressChanged     += Refresh;
        FenceRepairTask.OnAllFencesRepaired   += Refresh;
        TakeOutTrashTask.OnProgressChanged    += Refresh;
        TakeOutTrashTask.OnAllItemsDeposited  += Refresh;
        CleanGraffitiTask.OnProgressChanged   += Refresh;
        CleanBloodTask.OnProgressChanged      += Refresh;

        _pollTimer = 0f;
        Refresh();
    }

    private void OnDisable()
    {
        FenceRepairTask.OnProgressChanged     -= Refresh;
        FenceRepairTask.OnAllFencesRepaired   -= Refresh;
        TakeOutTrashTask.OnProgressChanged    -= Refresh;
        TakeOutTrashTask.OnAllItemsDeposited  -= Refresh;
        CleanGraffitiTask.OnProgressChanged   -= Refresh;
        CleanBloodTask.OnProgressChanged      -= Refresh;
    }

    private void Update()
    {
        _pollTimer += Time.unscaledDeltaTime;
        if (_pollTimer < _pollInterval) return;

        _pollTimer = 0f;
        Refresh();
    }

    private void Refresh()
    {
        if (_fenceRow != null)    _fenceRow.SetCount(GetOutstandingFenceCount());
        if (_trashRow != null)    _trashRow.SetCount(GetOutstandingTrashCount());
        if (_graffitiRow != null) _graffitiRow.SetCount(GetOutstandingGraffitiCount());
    }
}
