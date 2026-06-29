using System;
using UnityEngine;

public class PlacementBoard : MonoBehaviour
{
    [SerializeField] private bool isHanging;
    
    public bool IsHanging => isHanging;

    /// <summary>
    /// Fired locally whenever an item is successfully placed on this board.
    /// Subscribe from tutorial systems that need to react to a specific board being used.
    /// </summary>
    public event Action<PickableObject> OnItemPlaced;

    public virtual void OnPlaced(PickableObject pickableObject)
    {
        Debug.Log("On Placed");
        OnItemPlaced?.Invoke(pickableObject);
    }
}
