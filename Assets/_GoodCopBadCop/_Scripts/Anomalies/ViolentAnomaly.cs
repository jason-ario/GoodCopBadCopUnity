using UnityEngine;

/// <summary>
/// Behavior anomaly in which the suspect displays aggressive or violent outbursts,
/// such as throwing objects, striking surfaces, or lunging toward others.
/// </summary>
public class ViolentAnomaly : BehaviorAnomaly
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
