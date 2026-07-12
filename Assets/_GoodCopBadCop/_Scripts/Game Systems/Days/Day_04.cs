using UnityEngine;

public class Day_04 : DayBase
{
    [Header("Day 4 — Power")]
    [Tooltip("Restores power at the start of Day 4, which was cut at the end of Day 3. " +
             "The ElectricityController NetworkVariable persists across the day boundary, " +
             "so PowerOn() must be called explicitly here.")]
    [SerializeField] private ElectricityController _electricityController;

    public override void DayActivated()
    {
        base.DayActivated();

        // Power was cut by Day_03's clock-out sequence. Restore it on Day 4.
        // PowerOn() has an internal IsServer guard so calling it here is safe on all clients.
        ElectricityController ec = _electricityController != null
            ? _electricityController
            : FindAnyObjectByType<ElectricityController>();

        if (ec != null)
            ec.PowerOn();
        else
            Debug.LogWarning("[Day_04] ElectricityController not found — power will remain off from Day 3.", this);
    }

    public override void ShiftEnded()        => base.ShiftEnded();
    public override void NightPhaseStarted() => base.NightPhaseStarted();
    public override void DayCompleted()      => base.DayCompleted();
    public override void DayDeactivated()    => base.DayDeactivated();
}
