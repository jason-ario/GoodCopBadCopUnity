using UnityEngine;

public class CigarettePack : ContainerPickableObject
{
    protected override string BuildInteractText(int itemsRemaining)
        => "to grab cigarette";
    
}
