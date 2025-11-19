using UnityEngine;

public class Beer : PickableObject
{
    public override void OnPickedUp()
    {
        base.OnPickedUp();
        Debug.Log("Player picked up the beer.");
        // Later: add drinking animation, sloshing, etc.
    }

    public override void OnDropped()
    {
        base.OnDropped();
        Debug.Log("Player dropped the beer.");
        // Maybe spillFX?
    }
}