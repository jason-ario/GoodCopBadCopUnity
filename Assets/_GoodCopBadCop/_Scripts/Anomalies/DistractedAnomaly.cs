using UnityEngine;

/// <summary>
/// Behavior anomaly in which the suspect loses focus mid-action — stopping,
/// staring blankly, or drifting attention toward random points of interest.
/// </summary>
public class DistractedAnomaly : BehaviorAnomaly
{
    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();
    }

    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();
    }

    public override void InitializeDisabled() { }
}
