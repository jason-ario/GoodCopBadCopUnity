using UnityEngine;

/// <summary>
/// A no-tutorial, no-restriction test day for generic feature validation.
/// Assign this component to a child GameObject under CampaignManager, set DayNumber to
/// <see cref="TestDayNumber"/> in the Inspector, and optionally assign a SuspectSet.
/// If no SuspectSet is assigned, the previously active pool is reused unchanged.
///
/// Activate via DebugConsole (F10) to queue this as the next day, or call
/// CampaignManager.Instance.JumpToDay(Day_Test.TestDayNumber) directly from code.
/// </summary>
public class Day_Test : DayBase
{
    /// <summary>
    /// Canonical day number for the test day.
    /// Set <see cref="DayBase.DayNumber"/> to this value in the Inspector.
    /// </summary>
    public const int TestDayNumber = 99;

    public override void DayActivated()
    {
        // Clear any scripted overrides installed by tutorial days (e.g. Day_01).
        if (DailySuspectManager.Instance != null)
            DailySuspectManager.Instance.PopulateSuspectOverride = null;

        // Call base last so OnDayStart fires after cleanup.
        base.DayActivated();

        Debug.Log("[Day_Test] Test day activated — all normal systems active, no tutorial restrictions.");
    }

    public override void DayDeactivated()
    {
        // Ensure the override is cleared when moving to the next day.
        if (DailySuspectManager.Instance != null)
            DailySuspectManager.Instance.PopulateSuspectOverride = null;

        base.DayDeactivated();
    }
}
