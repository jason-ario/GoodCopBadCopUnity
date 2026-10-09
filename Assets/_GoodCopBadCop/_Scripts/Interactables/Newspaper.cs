using UnityEngine;

/// <summary>
/// Held newspaper / daily fax. LMB raises it to read; zoom mode "holds LMB" through the
/// same path (see <see cref="HeldDocumentInspection"/>).
/// </summary>
public class Newspaper : PickableObject
{
    /// <summary>LMB opens the newspaper for reading.</summary>
    public override string GetHeldUseVerb() => "Read";

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
