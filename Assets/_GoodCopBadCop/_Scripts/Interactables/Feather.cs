using UnityEngine;

public class Feather : PickableObject
{
    public override void OnPickedUp()
    {
        base.OnPickedUp();
        Debug.Log("Player picked up the feather.");
        // Later: add drinking animation, sloshing, etc.
    }

    public override void OnDropped()
    {
        base.OnDropped();
        Debug.Log("Player dropped the beer.");
        // Maybe spillFX?
    }
}
