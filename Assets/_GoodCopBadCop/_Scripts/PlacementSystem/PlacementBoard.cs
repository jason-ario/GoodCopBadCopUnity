using UnityEngine;

public class PlacementBoard : MonoBehaviour
{
    [SerializeField] private bool isHanging;
    
    public bool IsHanging => isHanging;
    
    public virtual void OnPlaced(PickableObject pickableObject)
    {
        Debug.Log("On Placed");
    }
}
