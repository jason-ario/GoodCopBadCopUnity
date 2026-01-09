using System;
using HighlightPlus;
using UnityEngine;

public class ObjectPlacer : MonoBehaviour
{
    public static ObjectPlacer Instance;
    [SerializeField] private Transform container;
    [SerializeField] private PickableObject[] pickableObjects;
    private PickableItemData _pickableItemData;
    public bool IsActive;

    private void Awake()
    {
        Instance = this;
    }

    public void SetItem(PickableItemData itemData)
    {
        _pickableItemData = itemData;
        
        foreach (var pickableObject in pickableObjects)
        {
            if (pickableObject.ItemData == itemData)
            {
                pickableObject.gameObject.SetActive(true);
            }
            else
            {
                pickableObject.gameObject.SetActive(false);
            }
        }
    }

    public void ActivatePlacer(PlacementBoard placementBoard)
    {
        container.gameObject.SetActive(true);
        IsActive = true;
    }

    public void DeactivatePlacer()
    {
        container.gameObject.SetActive(false);
        IsActive = false;
    }

    public GameObject GetPickableObject(PickableItemData heldObject)
    {
        foreach (var pickableObject in pickableObjects)
        {
            if (pickableObject.ItemData == heldObject)
            {
                return pickableObject.gameObject;
            }
        }

        return null;
    }
}
