using UnityEngine;

public class PlaceObjectSlot : MonoBehaviour
{
    public PickableObject itemThatCanBePlaced;
    [SerializeField] private GameObject placeObjectVisual;
    [SerializeField] private GameObject objectPlacedVisual;
    [SerializeField] private Transform placementPos;
    public bool startPlaced;
    private bool _isPlaced;

    public bool IsPlaced
    {
        get => _isPlaced;
        set => _isPlaced = value;
    }
    
    void Start()
    {
        _isPlaced = startPlaced;
    }
    
    public Transform PlaceObjectPos => placementPos;
    
    public void ShowPlaceObjectVisual()
    {
        if (IsPlaced) return;
        
        placeObjectVisual.SetActive(true);
    }
    
    public void HidePlacedVisual()
    {
        placeObjectVisual.SetActive(false);
    }
}
