using UnityEngine;

/// <summary>
/// Drives the "MAINTENANCE" section of the consolidated HUD sidebar (see
/// <see cref="HUDSidebarController"/>) — three <see cref="MaintenanceTaskRow"/> rows showing
/// how many broken fences, pieces of trash/gore, and unscrubbed graffiti pieces are currently
/// outstanding, each as a "current / total" counter with a progress-filled icon.
///
/// After Day 1 these three cleanup categories become optional (see <see cref="CleanupTaskGating"/>):
/// they no longer occupy a slot on the mandatory "CURRENT ORDERS" task list and no longer block
/// clock-out, but they still spawn, still show compass pips, and still drag down
/// <see cref="CheckpointIntegrityService"/>'s payout multiplier. This section is the always-visible
/// replacement so the player can still see exactly what's left without it being a task-list entry.
///
/// Purely a read-side display: never registers or blocks anything, just mirrors already-networked
/// task state. Shows 0/0 for any task that hasn't spawned yet.
/// </summary>
public class CheckpointMaintenanceHUD : MonoBehaviour
{
    [Tooltip("Row showing perimeter fence segments repaired / total.")]
    [SerializeField] private MaintenanceTaskRow _fenceRow;

    [Tooltip("Row showing trash/gore items deposited / total.")]
    [SerializeField] private MaintenanceTaskRow _trashRow;

    [Tooltip("Row showing graffiti pieces scrubbed / total.")]
    [SerializeField] private MaintenanceTaskRow _graffitiRow;

    /// <summary>Number of maintenance rows that are not yet fully complete. Used for the sidebar's collapsed badge count.</summary>
    public int IncompleteCount
    {
        get
        {
            int count = 0;
            if (!IsRowComplete(FenceRepairTask.Instance?.RepairedCount, FenceRepairTask.Instance?.TotalCount)) count++;
            if (!IsRowComplete(TakeOutTrashTask.Instance?.DepositedCount, TakeOutTrashTask.Instance?.TotalCount)) count++;
            if (!IsRowComplete(CleanGraffitiTask.Instance?.ScrubbedCount, CleanGraffitiTask.Instance?.TotalGraffitiCount)) count++;
            return count;
        }
    }

    private void OnEnable()
    {
        FenceRepairTask.OnProgressChanged     += Refresh;
        FenceRepairTask.OnAllFencesRepaired   += Refresh;
        TakeOutTrashTask.OnProgressChanged    += Refresh;
        TakeOutTrashTask.OnAllItemsDeposited  += Refresh;
        CleanGraffitiTask.OnProgressChanged   += Refresh;

        if (_fenceRow != null) _fenceRow.SetLabel("Fix broken fence");
        if (_trashRow != null) _trashRow.SetLabel("Throw away trash");
        if (_graffitiRow != null) _graffitiRow.SetLabel("Remove graffiti");

        Refresh();
    }

    private void OnDisable()
    {
        FenceRepairTask.OnProgressChanged     -= Refresh;
        FenceRepairTask.OnAllFencesRepaired   -= Refresh;
        TakeOutTrashTask.OnProgressChanged    -= Refresh;
        TakeOutTrashTask.OnAllItemsDeposited  -= Refresh;
        CleanGraffitiTask.OnProgressChanged   -= Refresh;
    }

    private void Refresh()
    {
        FenceRepairTask fence = FenceRepairTask.Instance;
        _fenceRow?.SetProgress(fence != null ? fence.RepairedCount : 0, fence != null ? fence.TotalCount : 0);

        TakeOutTrashTask trash = TakeOutTrashTask.Instance;
        _trashRow?.SetProgress(trash != null ? trash.DepositedCount : 0, trash != null ? trash.TotalCount : 0);

        CleanGraffitiTask graffiti = CleanGraffitiTask.Instance;
        _graffitiRow?.SetProgress(graffiti != null ? graffiti.ScrubbedCount : 0, graffiti != null ? graffiti.TotalGraffitiCount : 0);

        HUDSidebarController.Instance?.RefreshBadge();
    }

    private static bool IsRowComplete(int? current, int? total)
    {
        if (total == null || total <= 0) return true;
        return current != null && current >= total;
    }
}
