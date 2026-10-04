using UnityEngine;

public class CigarettePack : ContainerPickableObject
{
    protected override string BuildInteractText(int itemsRemaining)
        => "to grab cigarette";

    /// <summary>Reticle hold prompt: "Hold E to grab a cigarette".</summary>
    protected override string ExtractVerb => "grab a cigarette";
}
