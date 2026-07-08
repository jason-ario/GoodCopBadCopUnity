using UnityEngine;

/// <summary>
/// Day 3 — booth mess cleanup.
///
/// Activates the booth mess left from the previous shift and triggers the
/// Clean Booth Mess task at day start so players must scrub all blood splatters.
///
/// TriggerTask() is server-authoritative; CleanBoothMessTask guards internally.
/// </summary>
public class Day_03 : DayBase
{
    [Header("Day 3 Booth Mess")]
    [Tooltip("The Clean Booth Mess task — triggered at the start of Day 3. Server-only.")]
    [SerializeField] private CleanBoothMessTask _cleanBoothMessTask;

    // -------------------------------------------------------------------------
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated();
        _cleanBoothMessTask?.TriggerTask();
    }

    public override void ShiftEnded()        => base.ShiftEnded();
    public override void NightPhaseStarted() => base.NightPhaseStarted();
    public override void DayCompleted()      => base.DayCompleted();
}
