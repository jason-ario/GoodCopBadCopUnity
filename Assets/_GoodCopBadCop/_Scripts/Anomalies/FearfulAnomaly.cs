using UnityEngine;

/// <summary>
/// Behavior anomaly in which the suspect exhibits signs of extreme fear —
/// cowering, flinching, backing away, or covering their face.
/// </summary>
public class FearfulAnomaly : BehaviorAnomaly
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
