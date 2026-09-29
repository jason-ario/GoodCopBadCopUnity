using UnityEngine;

/// <summary>
/// Held newspaper / daily fax. LMB raises it to read; zoom mode "holds LMB" through the
/// same path (see <see cref="HeldDocumentInspection"/>).
/// </summary>
public class Newspaper : PickableObject
{
    public override void OnStartUse()
    {
        base.OnStartUse();
        HeldDocumentInspection.BeginFromUse(playerPickupController);
    }
    
    public override void OnStopUse()
    {
        base.OnStopUse();
        HeldDocumentInspection.EndFromUse(playerPickupController);
    }
}
