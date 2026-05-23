using UnityEngine;

/// <summary>
/// Base component placed on each per-day child GameObject under CampaignManager (Day 1 … Day 30).
/// CampaignManager activates the correct child and calls OnDayActivated / OnDayDeactivated.
/// Override these virtuals in day-specific subclasses to add scene logic without touching the core system.
/// All day child GameObjects are kept inactive by default; CampaignManager activates exactly one at a time.
/// </summary>
public class DayContext : MonoBehaviour
{
    [Tooltip("1-based day number. Must match the day this GameObject represents.")]
    public int DayNumber;

    /// <summary>
    /// Called by CampaignManager when this day begins.
    /// The GameObject will already be active when this is invoked.
    /// </summary>
    public virtual void OnDayActivated()
    {
        Debug.Log($"[DayContext] Day {DayNumber} activated.");
    }

    /// <summary>
    /// Called by CampaignManager when this day ends and the next is about to begin.
    /// </summary>
    public virtual void OnDayDeactivated()
    {
        Debug.Log($"[DayContext] Day {DayNumber} deactivated.");
    }
}
