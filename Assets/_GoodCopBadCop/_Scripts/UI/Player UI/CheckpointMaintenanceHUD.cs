using TMPro;
using UnityEngine;

/// <summary>
/// Always-on HUD readout — intended to sit directly beneath <see cref="CheckpointIntegrityBar"/> —
/// showing how many broken fences, pieces of trash/gore, and unscrubbed graffiti pieces are
/// currently outstanding: "[Fence icon] X  [Trash icon] X  [Graffiti icon] X".
///
/// After Day 1 these three cleanup categories become optional (see <see cref="CleanupTaskGating"/>):
/// they no longer occupy a slot on the mandatory HUD task list and no longer block clock-out, but
/// they still spawn, still show compass pips, and still drag down <see cref="CheckpointIntegrityService"/>'s
/// payout multiplier. This readout is the always-visible replacement so the player can still see
/// exactly what's left without it being a task-list entry.
///
/// Purely a read-side display: never registers or blocks anything, just mirrors already-networked
/// task state. Shows 0 for any task that hasn't spawned yet / has nothing outstanding.
/// </summary>
public class CheckpointMaintenanceHUD : MonoBehaviour
{
    [Tooltip("Shows the number of perimeter fence segments still broken.")]
    [SerializeField] private TextMeshProUGUI _fenceCountText;

    [Tooltip("Shows the number of trash/gore items still uncollected.")]
    [SerializeField] private TextMeshProUGUI _trashCountText;

    [Tooltip("Shows the number of graffiti pieces still unscrubbed.")]
    [SerializeField] private TextMeshProUGUI _graffitiCountText;

    private void OnEnable()
    {
        FenceRepairTask.OnProgressChanged     += Refresh;
        FenceRepairTask.OnAllFencesRepaired   += Refresh;
        TakeOutTrashTask.OnProgressChanged    += Refresh;
        TakeOutTrashTask.OnAllItemsDeposited  += Refresh;
        CleanGraffitiTask.OnProgressChanged   += Refresh;

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
        SetCount(_fenceCountText, GetFenceRemaining());
        SetCount(_trashCountText, GetTrashRemaining());
        SetCount(_graffitiCountText, GetGraffitiRemaining());
    }

    private static int GetFenceRemaining()
    {
        FenceRepairTask task = FenceRepairTask.Instance;
        return task == null ? 0 : Mathf.Max(0, task.TotalCount - task.RepairedCount);
    }

    private static int GetTrashRemaining()
    {
        TakeOutTrashTask task = TakeOutTrashTask.Instance;
        return task == null ? 0 : Mathf.Max(0, task.TotalCount - task.DepositedCount);
    }

    private static int GetGraffitiRemaining()
    {
        CleanGraffitiTask task = CleanGraffitiTask.Instance;
        return task == null ? 0 : Mathf.Max(0, task.TotalGraffitiCount - task.ScrubbedCount);
    }

    private static void SetCount(TextMeshProUGUI label, int value)
    {
        if (label != null)
            label.text = value.ToString();
    }
}
