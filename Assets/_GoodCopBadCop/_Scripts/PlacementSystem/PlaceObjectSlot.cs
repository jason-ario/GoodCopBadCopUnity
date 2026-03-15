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
        set
        {
            if (value)
            {
                ShowObjectPlacedVisual();
            }
            else
            {
                HideObjectPlacedVisual();
            }

            _isPlaced = value;
        }
    }
    
    void Start()
    {
        objectPlacedVisual.SetActive(startPlaced);
        _isPlaced = startPlaced;
    }
    
    public Transform PlaceObjectPos => placementPos;
    
    public void ShowObjectPlacedVisual()
    {
        objectPlacedVisual.SetActive(true);
    }
    
    public void HideObjectPlacedVisual()
    {
        objectPlacedVisual.SetActive(false);
    }
    
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
