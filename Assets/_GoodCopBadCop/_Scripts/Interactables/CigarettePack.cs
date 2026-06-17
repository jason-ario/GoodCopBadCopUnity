using UnityEngine;

public class CigarettePack : ContainerPickableObject
{
    protected override string BuildInteractText(int itemsRemaining)
        => $"Extract Cigarette ({itemsRemaining} left)";
    
}
