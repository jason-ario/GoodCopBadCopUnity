using UnityEngine;

public class PlaceObjectSlot : MonoBehaviour
{
    public PickableItemData itemThatCanBePlaced;
    [SerializeField] private GameObject placeObjectVisual;
    [SerializeField] private Transform placementPos;
    public Transform PlaceObjectPos => placementPos;
    public void ShowPlacedVisual()
    {
        placeObjectVisual.SetActive(true);
    }
    
    public void HidePlacedVisual()
    {
        placeObjectVisual.SetActive(false);
    }
}
