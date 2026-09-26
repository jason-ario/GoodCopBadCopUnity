using System.Collections;
using UnityEngine;

public class InkStampPickup : PickableObject
{
    public StampContainer.StampType StampType;

    /// <summary>Ink stamps always spawn at their authored location; they are not saved or restored.</summary>
    public override bool IsPersistedInSave => false;
}
